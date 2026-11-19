using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarMonster
{
    public enum RaceState
    {
        Setup,
        Countdown,
        Racing,
        Finished,
        TimeOver
    }

    public enum TimeBonusReason
    {
        Checkpoint,
        Takedown,
        Lap
    }

    /// <summary>
    /// Менеджер заезда на время: гараж (Setup) → спавн игрока и ботов из префабов → отсчёт → гонка.
    /// Таймер, бонусы за чекпоинты/круги/тейкдауны, респавн разбитых и упавших машин, кредиты за заезд.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TimeAttackManager : MonoBehaviour
    {
        public static TimeAttackManager Instance { get; private set; }

        [SerializeField] private GameBalanceSO balance;
        [SerializeField] private GameCatalogSO catalog;
        [SerializeField] private LocationSO location;
        [SerializeField] private WaypointCircuit circuit;
        [Tooltip("По порядку прохождения. [0] — старт/финиш.")]
        [SerializeField] private Checkpoint[] checkpoints = Array.Empty<Checkpoint>();
        [Tooltip("Стартовая решётка. [0] — игрок, остальные — боты.")]
        [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();
        [SerializeField] private CameraSpeedController cameraController;
        [SerializeField] private LayerMask respawnBlockMask = ~0;
        [SerializeField] private string defaultCarCode = "R35-GT";

        public event Action<RaceState> StateChanged;
        public event Action<float, TimeBonusReason> TimeBonusAdded;
        public event Action<int> CheckpointPassed;
        public event Action<int> LapCompleted;
        public event Action<ArcadeCarController> CarRespawned;
        public event Action<ArcadeCarController> PlayerChanged;
        public event Action<RaceResult> RaceEnded;

        public RaceState State { get; private set; } = RaceState.Setup;
        public float TimeLeft { get; private set; }
        public float ElapsedTime { get; private set; }
        public float CountdownRemaining { get; private set; }
        public int CurrentLap { get; private set; } = 1;
        public int TotalLaps => balance.timeAttack.laps;
        public int NextCheckpointIndex { get; private set; }
        public int CheckpointsPassed { get; private set; }
        public int TakedownCount { get; private set; }
        public RaceResult LastResult { get; private set; }
        public ArcadeCarController Player { get; private set; }
        public WaypointCircuit Circuit => circuit;
        public GameBalanceSO Balance => balance;
        public GameCatalogSO Catalog => catalog;
        public LocationSO Location => location;
        public IReadOnlyList<ArcadeCarController> Racers => _racers;

        private readonly List<ArcadeCarController> _racers = new List<ArcadeCarController>();
        private readonly HashSet<ArcadeCarController> _respawning = new HashSet<ArcadeCarController>();
        private readonly Collider[] _blockBuffer = new Collider[16];
        private Checkpoint _lastPlayerCheckpoint;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (balance == null || catalog == null)
            {
                Debug.LogError("TimeAttackManager: не назначены GameBalanceSO / GameCatalogSO.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < checkpoints.Length; i++)
                if (checkpoints[i] != null) checkpoints[i].Index = i;
            PlayerProfile.EnsureInitialized(balance);
        }

        private void OnEnable() => TakedownSystem.AnyTakedown += OnTakedown;

        private void OnDisable() => TakedownSystem.AnyTakedown -= OnTakedown;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            TimeScaleController.ResetTime();
            ResetTimer();

            CarDataSO preferred = catalog.FindCar(RaceSession.PlayerCarCode) ??
                                  catalog.FindCar(PlayerProfile.LastCarCode) ??
                                  catalog.FindCar(defaultCarCode);
            if (preferred == null && catalog.cars.Length > 0) preferred = catalog.cars[0];
            if (!RaceSession.AutoStart && !PlayerProfile.IsOwned(preferred)) preferred = FirstOwnedCar() ?? preferred;

            SetPreviewCar(preferred);
            SetState(RaceState.Setup);

            if (RaceSession.AutoStart) BeginRace();
        }

        private CarDataSO FirstOwnedCar()
        {
            foreach (CarDataSO car in catalog.cars)
                if (PlayerProfile.IsOwned(car)) return car;
            return null;
        }

        private void ResetTimer()
        {
            TimeLeft = balance.timeAttack.startTime;
            ElapsedTime = 0f;
            CurrentLap = 1;
            CheckpointsPassed = 0;
            TakedownCount = 0;
            NextCheckpointIndex = checkpoints.Length > 1 ? 1 : 0;
            _lastPlayerCheckpoint = checkpoints.Length > 0 ? checkpoints[0] : null;
            CountdownRemaining = balance.timeAttack.countdownDuration;
        }

        // ---------- Setup / spawning ----------

        /// <summary>Показать машину на старте (гараж). Заменяет текущую машину игрока.</summary>
        public void SetPreviewCar(CarDataSO data)
        {
            if (State != RaceState.Setup || data == null) return;
            if (Player != null)
            {
                _racers.Remove(Player);
                Player.gameObject.SetActive(false); // сразу убрать из физики, Destroy отложенный
                Destroy(Player.gameObject);
                Player = null;
            }

            Player = SpawnCar(data, 0, true);
            if (Player == null) return;
            Player.SetControlEnabled(false);
            _racers.Add(Player);
            if (cameraController != null) cameraController.SetTarget(Player);
            PlayerChanged?.Invoke(Player);
        }

        /// <summary>Старт заезда с текущей машиной игрока: спавн ботов и отсчёт.</summary>
        public void BeginRace()
        {
            if (State != RaceState.Setup || Player == null) return;
            PlayerProfile.LastCarCode = Player.Data.chassisCode;
            RaceSession.PlayerCarCode = Player.Data.chassisCode;

            SpawnBots();
            ResetTimer();
            SetAllControls(false);
            SetState(RaceState.Countdown);
        }

        private void SpawnBots()
        {
            int botCount = Mathf.Max(0, spawnPoints.Length - 1);
            var picks = new List<CarDataSO>(botCount);

            if (RaceSession.BotCarCodes != null)
            {
                foreach (string code in RaceSession.BotCarCodes)
                {
                    CarDataSO car = catalog.FindCar(code);
                    if (car != null && picks.Count < botCount) picks.Add(car);
                }
            }
            else
            {
                var pool = new List<CarDataSO>();
                foreach (CarDataSO car in catalog.cars)
                    if (car != null && car != Player.Data && car.prefab != null) pool.Add(car);
                for (int i = 0; i < botCount && pool.Count > 0; i++)
                {
                    int index = UnityEngine.Random.Range(0, pool.Count);
                    picks.Add(pool[index]);
                    pool.RemoveAt(index);
                }
            }

            for (int i = 0; i < picks.Count; i++)
            {
                ArcadeCarController bot = SpawnCar(picks[i], i + 1, false);
                if (bot == null) continue;
                bool armored = picks[i].hasRamPlow;
                float lane = spawnPoints[i + 1].InverseTransformPoint(spawnPoints[0].position).x * -0.6f;
                bot.GetComponent<RacerAI>().Configure(balance, circuit, Player,
                    UnityEngine.Random.Range(0.55f, 0.9f),
                    armored ? 1f : UnityEngine.Random.Range(0.3f, 0.9f),
                    Mathf.Clamp(lane, -6f, 6f));
                _racers.Add(bot);
            }
        }

        private ArcadeCarController SpawnCar(CarDataSO data, int spawnIndex, bool isPlayer)
        {
            if (data.prefab == null)
            {
                Debug.LogError($"У машины {data.chassisCode} не назначен prefab. Запусти Car Monster → Create Car Roster.", data);
                return null;
            }
            Transform spawn = spawnPoints.Length > spawnIndex ? spawnPoints[spawnIndex] : transform;
            GameObject go = Instantiate(data.prefab, spawn.position, spawn.rotation);
            go.name = $"{(isPlayer ? "Player" : "AI")}_{data.chassisCode}";

            var car = go.GetComponent<ArcadeCarController>();
            if (isPlayer)
            {
                go.tag = "Player";
                car.SetInputSource(go.AddComponent<PlayerCarInput>());
            }
            else
            {
                go.AddComponent<RacerAI>();
            }

            if (go.TryGetComponent(out EngineAudio engine)) engine.SetIsPlayer(isPlayer);
            return car;
        }

        // ---------- Race loop ----------

        private void Update()
        {
            float dt = Time.deltaTime;
            switch (State)
            {
                case RaceState.Countdown:
                    CountdownRemaining -= dt;
                    if (CountdownRemaining <= 0f)
                    {
                        CountdownRemaining = 0f;
                        SetAllControls(true);
                        SetState(RaceState.Racing);
                    }
                    break;

                case RaceState.Racing:
                    TimeLeft -= dt;
                    ElapsedTime += dt;
                    if (TimeLeft <= 0f)
                    {
                        TimeLeft = 0f;
                        if (Player != null) Player.SetControlEnabled(false);
                        EndRace(false);
                    }
                    CheckFallenCars();
                    CheckOffTrack();
                    break;
            }
        }

        private void CheckFallenCars()
        {
            float killY = balance.timeAttack.killY;
            for (int i = 0; i < _racers.Count; i++)
            {
                ArcadeCarController car = _racers[i];
                if (car != null && car.Body.position.y < killY && !_respawning.Contains(car))
                    RequestRespawn(car, 0.5f);
            }
        }

        private void SetAllControls(bool value)
        {
            for (int i = 0; i < _racers.Count; i++)
            {
                ArcadeCarController car = _racers[i];
                if (car == null) continue;
                if (value && car.TryGetComponent(out TakedownSystem td) && td.IsWrecked) continue;
                car.SetControlEnabled(value);
            }
        }

        private void SetState(RaceState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        private void EndRace(bool finished)
        {
            var result = new RaceResult
            {
                finished = finished,
                checkpoints = CheckpointsPassed,
                takedowns = TakedownCount,
                timeLeft = TimeLeft,
                elapsed = ElapsedTime
            };
            result.credits = RaceRewards.Calculate(result, balance.economy);
            if (!RaceSession.DisableRewards) PlayerProfile.AddCredits(result.credits);
            LastResult = result;

            SetState(finished ? RaceState.Finished : RaceState.TimeOver);
            RaceEnded?.Invoke(result);
        }

        public void AddTime(float seconds, TimeBonusReason reason)
        {
            if (State != RaceState.Racing || seconds <= 0f) return;
            TimeLeft += seconds;
            TimeBonusAdded?.Invoke(seconds, reason);
        }

        public void NotifyCheckpoint(Checkpoint checkpoint, ArcadeCarController car)
        {
            if (car != Player || State != RaceState.Racing || checkpoints.Length == 0) return;
            if (checkpoint.Index != NextCheckpointIndex) return;

            _lastPlayerCheckpoint = checkpoint;
            CheckpointsPassed++;
            CheckpointPassed?.Invoke(checkpoint.Index);

            if (checkpoint.Index == 0)
            {
                LapCompleted?.Invoke(CurrentLap);
                if (CurrentLap >= TotalLaps)
                {
                    SetAllControls(false);
                    EndRace(true);
                    return;
                }
                CurrentLap++;
            }

            float bonus = checkpoint.TimeBonusOverride >= 0f
                ? checkpoint.TimeBonusOverride
                : balance.timeAttack.checkpointTimeBonus;
            AddTime(bonus, checkpoint.Index == 0 ? TimeBonusReason.Lap : TimeBonusReason.Checkpoint);

            NextCheckpointIndex = (NextCheckpointIndex + 1) % checkpoints.Length;
        }

        private void OnTakedown(TakedownSystem attacker, TakedownSystem victim)
        {
            if (attacker != null && attacker.Car == Player && State == RaceState.Racing)
            {
                TakedownCount++;
                AddTime(balance.combat.takedownTimeBonus, TimeBonusReason.Takedown);
            }
            if (victim != null) RequestRespawn(victim.Car, balance.timeAttack.respawnDelay);
        }

        // ---------- Respawn ----------

        /// <summary>Вернуть машину на трассу через delay секунд игрового времени.</summary>
        public void RequestRespawn(ArcadeCarController car, float delay)
        {
            if (car == null || _respawning.Contains(car)) return;
            if (Debug.isDebugBuild) Debug.Log($"[CarMonster] respawn {car.name} at {car.Body.position}");
            _respawning.Add(car);
            StartCoroutine(RespawnRoutine(car, delay));
        }

        private IEnumerator RespawnRoutine(ArcadeCarController car, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (car == null) yield break;

            GetRespawnPose(car, out Vector3 position, out Quaternion rotation);

            if (car.TryGetComponent(out TakedownSystem td))
            {
                // Самосборка: обломки слетаются, машина продолжает гонку с разгона с того же места.
                yield return td.Reassemble(position, rotation);
                td.SetInvulnerable(balance.combat.respawnInvulnerability);
            }
            else
            {
                car.Teleport(position, rotation);
            }

            if (car == null) yield break;
            car.SetControlEnabled(State == RaceState.Racing || (State == RaceState.TimeOver && car != Player));
            if (car.TryGetComponent(out RacerAI ai)) ai.ResetProgress();
            ResetOffTrack(car);

            _respawning.Remove(car);
            CarRespawned?.Invoke(car);
        }

        public bool IsRespawning(ArcadeCarController car) => _respawning.Contains(car);

        // ---------- Off-track ----------

        private readonly Dictionary<ArcadeCarController, float> _offTrackTime = new Dictionary<ArcadeCarController, float>();
        private float _nextOffTrackCheck;
        private const float OffTrackCheckInterval = 0.25f;

        private void CheckOffTrack()
        {
            if (circuit == null || Time.time < _nextOffTrackCheck) return;
            _nextOffTrackCheck = Time.time + OffTrackCheckInterval;
            GameBalanceSO.TimeAttackTuning t = balance.timeAttack;

            for (int i = 0; i < _racers.Count; i++)
            {
                ArcadeCarController car = _racers[i];
                if (car == null || _respawning.Contains(car) || !car.ControlEnabled) continue;
                if (car.TryGetComponent(out TakedownSystem td) && td.IsWrecked) continue;

                circuit.DistanceToTrack(car.Body.position, out float lateral, out float below);
                bool off = lateral > t.offTrackDistance || below > 6f;
                _offTrackTime.TryGetValue(car, out float time);
                time = off ? time + OffTrackCheckInterval : 0f;
                _offTrackTime[car] = time;
                if (time >= t.offTrackTime) RequestRespawn(car, 0f);
            }
        }

        private void ResetOffTrack(ArcadeCarController car) => _offTrackTime[car] = 0f;

        private void GetRespawnPose(ArcadeCarController car, out Vector3 position, out Quaternion rotation)
        {
            GameBalanceSO.TimeAttackTuning t = balance.timeAttack;

            if (circuit != null && circuit.PointCount >= 2)
            {
                float distance = circuit.SkipGaps(circuit.GetClosestDistance(car.Body.position) + t.respawnAheadOffset);
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    position = circuit.GetPoint(distance) + Vector3.up * 1.2f;
                    rotation = Quaternion.LookRotation(circuit.GetDirection(distance), Vector3.up);
                    if (!IsBlocked(position, rotation, car) || attempt == 9) return;
                    distance = circuit.SkipGaps(distance + 6f);
                }
            }

            Transform fallback = _lastPlayerCheckpoint != null ? _lastPlayerCheckpoint.transform : transform;
            position = fallback.position + Vector3.up * 1.2f;
            rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(fallback.forward, Vector3.up).normalized, Vector3.up);
        }

        /// <summary>
        /// Место занято: другая машина рядом или стена/блок в объёме кузова (дорога и земля не мешают —
        /// коробка проверки висит над асфальтом).
        /// </summary>
        private bool IsBlocked(Vector3 position, Quaternion rotation, ArcadeCarController self)
        {
            int count = Physics.OverlapSphereNonAlloc(position, balance.timeAttack.respawnClearRadius, _blockBuffer,
                respawnBlockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = _blockBuffer[i].attachedRigidbody;
                if (body != null && body != self.Body && body.TryGetComponent(out ArcadeCarController _)) return true;
            }

            count = Physics.OverlapBoxNonAlloc(position + rotation * Vector3.up * 0.9f, new Vector3(1.4f, 0.7f, 3.2f),
                _blockBuffer, rotation, respawnBlockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _blockBuffer[i];
                Rigidbody body = c.attachedRigidbody;
                if (body == self.Body) continue;
                if (body != null && !body.isKinematic) continue; // реквизит и обломки разлетятся сами
                return true;
            }
            return false;
        }

        // ---------- Flow ----------

        /// <summary>Перезапуск с той же машиной и сразу в отсчёт.</summary>
        public void RestartRace()
        {
            RaceSession.AutoStart = true;
            if (Player != null) RaceSession.PlayerCarCode = Player.Data.chassisCode;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Вернуться в гараж (эта же или другая локация).</summary>
        public void OpenGarage(LocationSO targetLocation = null)
        {
            RaceSession.AutoStart = false;
            if (Player != null) RaceSession.PlayerCarCode = Player.Data.chassisCode;
            RaceSession.BotCarCodes = null;
            string scene = targetLocation != null && !string.IsNullOrEmpty(targetLocation.sceneName)
                ? targetLocation.sceneName
                : SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(scene);
        }

#if UNITY_EDITOR
        public void EditorSetup(GameBalanceSO balanceAsset, GameCatalogSO catalogAsset, LocationSO loc,
            WaypointCircuit track, Checkpoint[] gates, Transform[] grid, CameraSpeedController cam)
        {
            balance = balanceAsset;
            catalog = catalogAsset;
            location = loc;
            circuit = track;
            checkpoints = gates;
            spawnPoints = grid;
            cameraController = cam;
        }
#endif
    }
}
