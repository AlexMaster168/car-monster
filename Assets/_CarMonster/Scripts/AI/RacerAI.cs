using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Бот-соперник. Подаёт ввод в тот же ArcadeCarController, что и игрок (честная физика).
    /// Едет по WaypointCircuit, тормозит перед поворотами, дрифтит в крутых, объезжает препятствия
    /// тремя лучами, жмёт нитро на прямых (и ловит идеальный впрыск в зависимости от скилла),
    /// подрезает игрока и пытается его таранить.
    /// </summary>
    [RequireComponent(typeof(ArcadeCarController))]
    public class RacerAI : CarInputSource
    {
        [SerializeField] private GameBalanceSO balance;
        [SerializeField] private WaypointCircuit circuit;
        [Tooltip("Цель для агрессии. Если пусто — берётся игрок из TimeAttackManager.")]
        [SerializeField] private ArcadeCarController rival;

        [Header("Personality")]
        [Range(0f, 1f)] [SerializeField] private float skill = 0.7f;
        [Range(0f, 1f)] [SerializeField] private float aggression = 0.6f;
        [Tooltip("Смещение от осевой трассы, м (+ вправо).")]
        [SerializeField] private float laneOffset;
        [Tooltip("Множитель целевой скорости (простая настройка сложности).")]
        [Range(0.6f, 1.2f)] [SerializeField] private float paceMultiplier = 1f;

        public float Progress { get; private set; }
        public float TotalDistance { get; private set; }

        private ArcadeCarController _car;
        private NitroSystem _nitro;
        private TakedownSystem _takedown;
        private int _segment = -1;
        private float _lastProgress;
        private float _nitroReadyAt;
        private bool _perfectDecided;
        private float _stuckTimer;
        private float _reverseUntil;
        private float _lanePhase;
        private float _ramDecisionAt;
        private bool _wantsRam;

        private void Awake()
        {
            _car = GetComponent<ArcadeCarController>();
            _nitro = GetComponent<NitroSystem>();
            _takedown = GetComponent<TakedownSystem>();
            _car.SetInputSource(this);
            _lanePhase = Random.value * 10f;
        }

        private void Start()
        {
            if (rival == null && TimeAttackManager.Instance != null) rival = TimeAttackManager.Instance.Player;
            ResetProgress();
        }

        public void SetCircuit(WaypointCircuit value, ArcadeCarController rivalCar)
        {
            circuit = value;
            rival = rivalCar;
            ResetProgress();
        }

        public void ResetProgress()
        {
            if (circuit == null) return;
            Progress = circuit.GetClosestDistance(transform.position, -1, 0, out _segment);
            _lastProgress = Progress;
            _stuckTimer = 0f;
            _reverseUntil = 0f;
        }

        private void FixedUpdate()
        {
            if (circuit == null || balance == null || !_car.ControlEnabled || (_takedown != null && _takedown.IsWrecked))
            {
                ResetInput();
                return;
            }

            GameBalanceSO.AITuning ai = balance.ai;
            float dt = Time.fixedDeltaTime;
            Vector3 position = _car.Body.position;
            float speed = _car.ForwardSpeed;

            UpdateProgress(position);

            if (Time.time < _reverseUntil)
            {
                Throttle = -1f;
                Steer = -SteerTowards(circuit.GetPoint(Progress + 10f));
                Drift = false;
                return;
            }

            // 1. Точка следования по трассе.
            float lookahead = ai.lookaheadBase + Mathf.Max(0f, speed) * ai.lookaheadPerMs;
            Vector3 trackDir = circuit.GetDirection(Progress + lookahead);
            Vector3 trackRight = Vector3.Cross(Vector3.up, trackDir).normalized;
            float lane = laneOffset + Mathf.Sin(Time.time * 0.15f + _lanePhase) * 1.2f;
            Vector3 target = circuit.GetPoint(Progress + lookahead) + trackRight * lane;

            // 2. Агрессия: таран или подрезание игрока.
            bool ramming = false;
            if (rival != null && rival != _car && aggression > 0.01f)
                ramming = ApplyAggression(position, trackDir, trackRight, ai, ref target);

            float steer = SteerTowards(target) + Avoidance(position, speed, ai) * ai.avoidStrength;

            // 3. Скорость по кривизне трассы впереди.
            float brakeLook = 15f + Mathf.Max(0f, speed) * 1.1f;
            float cornerAngle = Vector3.Angle(circuit.GetDirection(Progress + 4f), circuit.GetDirection(Progress + brakeLook));
            float maxMs = (_car.Data.maxSpeedKmh + _car.BoostSpeedBonusKmh) / 3.6f;
            float cornerMs = ai.cornerSpeedKmh / 3.6f * Mathf.Lerp(0.85f, 1.1f, skill);
            float targetSpeed = Mathf.Lerp(maxMs, cornerMs, Mathf.Clamp01(cornerAngle / ai.hardCornerAngle)) * paceMultiplier;

            float throttle;
            if (speed < targetSpeed) throttle = 1f;
            else if (speed > targetSpeed + 6f) throttle = -Mathf.Clamp01((speed - targetSpeed) / 15f);
            else throttle = 0.25f;
            if (ramming) throttle = 1f;

            bool drift = skill > 0.35f && cornerAngle > ai.driftCornerAngle &&
                         speed * 3.6f > _car.Data.driftMinSpeedKmh + 30f && Mathf.Abs(steer) > 0.45f;

            Throttle = throttle;
            Steer = Mathf.Clamp(steer, -1f, 1f);
            Drift = drift;

            // 4. Нитро.
            HandleNitro(cornerAngle, ramming, ai);

            // 5. Застрял — сдать назад.
            if (Throttle > 0.5f && Mathf.Abs(speed) < 1.5f)
            {
                _stuckTimer += dt;
                if (_stuckTimer > ai.stuckTime)
                {
                    _stuckTimer = 0f;
                    _reverseUntil = Time.time + ai.reverseTime;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }
        }

        private void UpdateProgress(Vector3 position)
        {
            float p = circuit.GetClosestDistance(position, _segment, 3, out _segment);
            float delta = p - _lastProgress;
            if (circuit.Loop)
            {
                float half = circuit.Length * 0.5f;
                if (delta < -half) delta += circuit.Length;
                else if (delta > half) delta -= circuit.Length;
            }
            TotalDistance += delta;
            _lastProgress = p;
            Progress = p;
        }

        private bool ApplyAggression(Vector3 position, Vector3 trackDir, Vector3 trackRight,
            GameBalanceSO.AITuning ai, ref Vector3 target)
        {
            if (rival.TryGetComponent(out TakedownSystem rivalTd) && (rivalTd.IsWrecked || rivalTd.IsInvulnerable))
                return false;

            Vector3 toRival = rival.Body.position - position;
            float distance = toRival.magnitude;
            if (distance > ai.attackRange) return false;

            float along = Vector3.Dot(toRival, trackDir);
            float lateral = Vector3.Dot(toRival, trackRight);

            if (along > 3f && along < ai.attackRange)
            {
                // Игрок впереди — целимся в точку упреждения и таранём.
                if (Time.time >= _ramDecisionAt)
                {
                    _ramDecisionAt = Time.time + 1.5f;
                    _wantsRam = Random.value < aggression;
                }
                if (!_wantsRam) return false;
                target = rival.Body.position + rival.Body.GetVelocity() * 0.35f;
                return true;
            }

            if (along < -1f && along > -18f && Mathf.Abs(lateral) < 6f)
            {
                // Игрок сзади — перекрываем его линию (подрезаем).
                target += trackRight * (lateral * aggression);
            }
            return false;
        }

        private float SteerTowards(Vector3 worldTarget)
        {
            Vector3 local = transform.InverseTransformPoint(worldTarget);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            return Mathf.Clamp(angle / balance.ai.steerAngleFull, -1f, 1f);
        }

        private float Avoidance(Vector3 position, float speed, GameBalanceSO.AITuning ai)
        {
            float length = ai.avoidRayLength + Mathf.Max(0f, speed) * ai.avoidRayPerMs;
            Vector3 origin = position + transform.up * 0.6f + transform.forward * 2.4f;
            Vector3 fwd = transform.forward;

            float left = CastWeight(origin, Quaternion.AngleAxis(-22f, transform.up) * fwd, length);
            float center = CastWeight(origin, fwd, length);
            float right = CastWeight(origin, Quaternion.AngleAxis(22f, transform.up) * fwd, length);

            float avoid = left - right;
            if (center > 0f) avoid += (left > right ? 1f : -1f) * center;
            return avoid;
        }

        private float CastWeight(Vector3 origin, Vector3 direction, float length)
        {
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, length, balance.ai.obstacleMask,
                    QueryTriggerInteraction.Ignore))
                return 0f;

            // Пологие поверхности (дорога, трамплины) — не препятствие.
            if (hit.normal.y > 0.55f) return 0f;
            if (hit.rigidbody != null && hit.rigidbody == _car.Body) return 0f;
            return 1f - hit.distance / length;
        }

        private void HandleNitro(float cornerAngle, bool ramming, GameBalanceSO.AITuning ai)
        {
            if (_nitro == null) return;

            if (_nitro.IsBoosting)
            {
                if (!_perfectDecided && _nitro.InGreenZone && _nitro.FilledBars > 0)
                {
                    _perfectDecided = true;
                    if (Random.value < skill * 0.8f) PressNitro();
                }
                return;
            }

            _perfectDecided = false;
            if (Time.time < _nitroReadyAt || _nitro.FilledBars < 1) return;

            bool straight = cornerAngle < ai.straightAngle;
            if (straight || ramming)
            {
                PressNitro();
                _nitroReadyAt = Time.time + ai.nitroCooldown;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(GameBalanceSO balanceAsset, WaypointCircuit track, float aiSkill, float aiAggression, float lane)
        {
            balance = balanceAsset;
            circuit = track;
            skill = aiSkill;
            aggression = aiAggression;
            laneOffset = lane;
        }
#endif
    }
}
