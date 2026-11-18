using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarMonster
{
    public enum RaceState
    {
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
    /// Менеджер заезда на время: отсчёт перед стартом, таймер, бонусы за чекпоинты и тейкдауны,
    /// круги, респавн разбитых/упавших машин на трассу через N секунд.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TimeAttackManager : MonoBehaviour
    {
        public static TimeAttackManager Instance { get; private set; }

        [SerializeField] private GameBalanceSO balance;
        [SerializeField] private ArcadeCarController player;
        [SerializeField] private WaypointCircuit circuit;
        [Tooltip("По порядку прохождения. [0] — старт/финиш.")]
        [SerializeField] private Checkpoint[] checkpoints = Array.Empty<Checkpoint>();
        [Tooltip("Если пусто — все ArcadeCarController на сцене.")]
        [SerializeField] private List<ArcadeCarController> racers = new List<ArcadeCarController>();
        [SerializeField] private LayerMask respawnBlockMask = ~0;

        public event Action<RaceState> StateChanged;
        public event Action<float, TimeBonusReason> TimeBonusAdded;
        public event Action<int> CheckpointPassed;
        public event Action<int> LapCompleted;
        public event Action<ArcadeCarController> CarRespawned;

        public RaceState State { get; private set; } = RaceState.Countdown;
        public float TimeLeft { get; private set; }
        public float ElapsedTime { get; private set; }
        public float CountdownRemaining { get; private set; }
        public int CurrentLap { get; private set; } = 1;
        public int TotalLaps => balance.timeAttack.laps;
        public int NextCheckpointIndex { get; private set; }
        public int TakedownCount { get; private set; }
        public ArcadeCarController Player => player;
        public WaypointCircuit Circuit => circuit;
        public GameBalanceSO Balance => balance;
        public IReadOnlyList<ArcadeCarController> Racers => racers;

        private readonly HashSet<ArcadeCarController> _respawning = new HashSet<ArcadeCarController>();
        private Checkpoint _lastPlayerCheckpoint;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (balance == null)
            {
                Debug.LogError("TimeAttackManager: не назначен GameBalanceSO.", this);
                enabled = false;
                return;
            }

            if (racers.Count == 0)
                racers.AddRange(FindObjectsByType<ArcadeCarController>(FindObjectsSortMode.None));
            if (player != null && !racers.Contains(player)) racers.Add(player);

            for (int i = 0; i < checkpoints.Length; i++)
                if (checkpoints[i] != null) checkpoints[i].Index = i;
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
            TimeLeft = balance.timeAttack.startTime;
            ElapsedTime = 0f;
            CurrentLap = 1;
            NextCheckpointIndex = checkpoints.Length > 1 ? 1 : 0;
            _lastPlayerCheckpoint = checkpoints.Length > 0 ? checkpoints[0] : null;
            CountdownRemaining = balance.timeAttack.countdownDuration;
            SetAllControls(false);
            SetState(RaceState.Countdown);
        }

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
                        if (player != null) player.SetControlEnabled(false);
                        SetState(RaceState.TimeOver);
                    }
                    CheckFallenCars();
                    break;
            }
        }

        private void CheckFallenCars()
        {
            float killY = balance.timeAttack.killY;
            for (int i = 0; i < racers.Count; i++)
            {
                ArcadeCarController car = racers[i];
                if (car != null && car.Body.position.y < killY && !_respawning.Contains(car))
                    RequestRespawn(car, 0.5f);
            }
        }

        private void SetAllControls(bool value)
        {
            for (int i = 0; i < racers.Count; i++)
            {
                ArcadeCarController car = racers[i];
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

        public void AddTime(float seconds, TimeBonusReason reason)
        {
            if (State != RaceState.Racing || seconds <= 0f) return;
            TimeLeft += seconds;
            TimeBonusAdded?.Invoke(seconds, reason);
        }

        public void NotifyCheckpoint(Checkpoint checkpoint, ArcadeCarController car)
        {
            if (car != player || State != RaceState.Racing || checkpoints.Length == 0) return;
            if (checkpoint.Index != NextCheckpointIndex) return;

            _lastPlayerCheckpoint = checkpoint;
            CheckpointPassed?.Invoke(checkpoint.Index);

            if (checkpoint.Index == 0)
            {
                LapCompleted?.Invoke(CurrentLap);
                if (CurrentLap >= TotalLaps)
                {
                    SetAllControls(false);
                    SetState(RaceState.Finished);
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
            if (attacker != null && attacker.Car == player)
            {
                TakedownCount++;
                AddTime(balance.combat.takedownTimeBonus, TimeBonusReason.Takedown);
            }
            if (victim != null) RequestRespawn(victim.Car, balance.timeAttack.respawnDelay);
        }

        /// <summary>Вернуть машину на трассу через delay секунд игрового времени.</summary>
        public void RequestRespawn(ArcadeCarController car, float delay)
        {
            if (car == null || _respawning.Contains(car)) return;
            _respawning.Add(car);
            StartCoroutine(RespawnRoutine(car, delay));
        }

        private IEnumerator RespawnRoutine(ArcadeCarController car, float delay)
        {
            yield return new WaitForSeconds(delay);

            GetRespawnPose(car, out Vector3 position, out Quaternion rotation);

            if (car.TryGetComponent(out TakedownSystem td))
            {
                td.Repair();
                td.SetInvulnerable(balance.combat.respawnInvulnerability);
            }
            car.Teleport(position, rotation);
            car.SetControlEnabled(State == RaceState.Racing || (State == RaceState.TimeOver && car != player));
            if (car.TryGetComponent(out RacerAI ai)) ai.ResetProgress();

            _respawning.Remove(car);
            CarRespawned?.Invoke(car);
        }

        private void GetRespawnPose(ArcadeCarController car, out Vector3 position, out Quaternion rotation)
        {
            GameBalanceSO.TimeAttackTuning t = balance.timeAttack;

            if (circuit != null && circuit.PointCount >= 2)
            {
                float distance = circuit.GetClosestDistance(car.Body.position) - t.respawnBackOffset;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    position = circuit.GetPoint(distance) + Vector3.up * 1.2f;
                    if (!IsBlocked(position, car) || attempt == 5)
                    {
                        rotation = Quaternion.LookRotation(circuit.GetDirection(distance), Vector3.up);
                        return;
                    }
                    distance += 7f;
                }
            }

            Transform fallback = _lastPlayerCheckpoint != null ? _lastPlayerCheckpoint.transform : transform;
            position = fallback.position + Vector3.up * 1.2f;
            rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(fallback.forward, Vector3.up).normalized, Vector3.up);
        }

        private readonly Collider[] _blockBuffer = new Collider[16];

        private bool IsBlocked(Vector3 position, ArcadeCarController self)
        {
            int count = Physics.OverlapSphereNonAlloc(position, balance.timeAttack.respawnClearRadius, _blockBuffer,
                respawnBlockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = _blockBuffer[i].attachedRigidbody;
                if (body != null && body != self.Body && body.TryGetComponent(out ArcadeCarController _)) return true;
            }
            return false;
        }

        /// <summary>Перезапуск заезда (перезагрузка сцены делается снаружи — это сброс таймера).</summary>
        public void RestartTimer()
        {
            StopAllCoroutines();
            _respawning.Clear();
            Start();
        }

#if UNITY_EDITOR
        public void EditorSetup(GameBalanceSO balanceAsset, ArcadeCarController playerCar, WaypointCircuit track,
            Checkpoint[] gates, List<ArcadeCarController> allRacers)
        {
            balance = balanceAsset;
            player = playerCar;
            circuit = track;
            checkpoints = gates;
            racers = allRacers;
        }
#endif
    }
}
