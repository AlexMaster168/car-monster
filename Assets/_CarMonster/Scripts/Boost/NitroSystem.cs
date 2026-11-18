using System;
using UnityEngine;

namespace CarMonster
{
    public enum NitroGainSource
    {
        Drift,
        Airtime,
        NearMiss,
        Takedown
    }

    /// <summary>
    /// Трёхуровневая шкала нитро с «Идеальным впрыском».
    /// Нажатие тратит 1 уровень и запускает буст. Пока буст идёт, курсор впрыска бежит 0→1.
    /// Повторное нажатие в зелёной зоне = Perfect: рывок, усиленный буст и огненный шлейф.
    /// Нажатие раньше зелёной зоны «срывает» впрыск — perfect в этом цикле уже не получить (антиспам).
    /// </summary>
    [RequireComponent(typeof(ArcadeCarController))]
    public class NitroSystem : MonoBehaviour
    {
        public const int BarCount = 3;
        private const int MaxTracked = 8;
        private const int OverlapBufferSize = 24;
        private const float NearMissScanInterval = 0.08f;

        [SerializeField] private GameBalanceSO balance;

        [Header("FX (необязательно)")]
        [SerializeField] private ParticleSystem[] boostParticles = Array.Empty<ParticleSystem>();
        [SerializeField] private ParticleSystem[] perfectFireParticles = Array.Empty<ParticleSystem>();
        [SerializeField] private TrailRenderer[] perfectFireTrails = Array.Empty<TrailRenderer>();

        /// <summary>Аргумент: true = идеальный впрыск.</summary>
        public event Action<bool> BoostStarted;
        public event Action BoostEnded;
        /// <summary>Источник и фактически начисленное количество.</summary>
        public event Action<NitroGainSource, float> ChargeGained;
        public event Action InjectionBotched;

        public float Charge { get; private set; }
        public float Capacity => _car.Data.nitroCapacityPerBar * BarCount;
        public float Charge01 => Capacity > 0f ? Charge / Capacity : 0f;
        public int FilledBars => Mathf.FloorToInt(Charge / _car.Data.nitroCapacityPerBar + 0.0001f);
        public bool IsBoosting { get; private set; }
        public bool IsPerfectBoost { get; private set; }
        public int PerfectChain { get; private set; }
        /// <summary>Курсор шкалы впрыска 0..1 (только пока идёт буст).</summary>
        public float InjectionCursor01 => IsBoosting ? 1f - _boostTimer / balance.nitro.boostDurationPerBar : 0f;
        public bool InGreenZone => IsBoosting && !_botched &&
                                   InjectionCursor01 >= balance.nitro.greenZoneStart &&
                                   InjectionCursor01 <= balance.nitro.greenZoneEnd;
        public float GreenZoneStart => balance.nitro.greenZoneStart;
        public float GreenZoneEnd => balance.nitro.greenZoneEnd;
        public GameBalanceSO Balance => balance;

        private struct TrackedRacer
        {
            public int id;
            public float minDistance;
            public float maxRelativeSpeed;
            public bool touched;
            public bool seenThisScan;
        }

        private ArcadeCarController _car;
        private float _boostTimer;
        private bool _botched;
        private readonly Collider[] _overlap = new Collider[OverlapBufferSize];
        private readonly TrackedRacer[] _tracked = new TrackedRacer[MaxTracked];
        private int _trackedCount;
        private float _nextScanTime;
        private float _nearMissCooldownUntil;

        private void Awake()
        {
            _car = GetComponent<ArcadeCarController>();
            if (balance == null)
            {
                Debug.LogError($"[{name}] NitroSystem: не назначен GameBalanceSO.", this);
                enabled = false;
                return;
            }
            SetFx(false, false);
        }

        private void OnEnable()
        {
            if (_car == null) return;
            _car.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (_car == null) return;
            _car.Landed -= OnLanded;
            if (IsBoosting) StopBoost();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            CarInputSource input = _car.InputSource;

            if (input != null && input.ConsumeNitroPress() && _car.ControlEnabled)
                HandleNitroPress();

            if (IsBoosting)
            {
                _boostTimer -= dt;
                if (_boostTimer <= 0f) StopBoost();
            }

            if (_car.IsDrifting)
                AddCharge(balance.nitro.chargePerDriftSecond * dt, NitroGainSource.Drift, false);

            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + NearMissScanInterval;
                ScanNearMisses();
            }
        }

        private void HandleNitroPress()
        {
            if (!IsBoosting)
            {
                if (FilledBars >= 1) StartBoost(false);
                return;
            }

            float cursor = InjectionCursor01;
            if (FilledBars < 1) return;

            if (cursor < balance.nitro.greenZoneStart)
            {
                if (_botched) return;
                _botched = true;
                InjectionBotched?.Invoke();
            }
            else if (!_botched && cursor <= balance.nitro.greenZoneEnd)
            {
                StartBoost(true);
            }
            else
            {
                StartBoost(false);
            }
        }

        private void StartBoost(bool perfect)
        {
            Charge -= _car.Data.nitroCapacityPerBar;
            if (Charge < 0f) Charge = 0f;

            IsBoosting = true;
            IsPerfectBoost = perfect;
            _botched = false;
            _boostTimer = balance.nitro.boostDurationPerBar;

            GameBalanceSO.NitroTuning n = balance.nitro;
            if (perfect)
            {
                PerfectChain = Mathf.Min(PerfectChain + 1, BarCount);
                float chain = 1f + n.perfectChainBonus * (PerfectChain - 1);
                _car.SetBoost(n.perfectAccelMultiplier * chain, n.perfectSpeedBonusKmh * chain);
                _car.AddForwardImpulse(n.perfectImpulse);
            }
            else
            {
                PerfectChain = 0;
                _car.SetBoost(n.boostAccelMultiplier, n.boostSpeedBonusKmh);
            }

            SetFx(true, perfect);
            BoostStarted?.Invoke(perfect);
        }

        private void StopBoost()
        {
            IsBoosting = false;
            IsPerfectBoost = false;
            _botched = false;
            _boostTimer = 0f;
            _car.SetBoost(1f, 0f);
            SetFx(false, false);
            BoostEnded?.Invoke();
        }

        /// <summary>Прервать буст (например, при выбивании машины).</summary>
        public void CancelBoost()
        {
            if (IsBoosting) StopBoost();
            PerfectChain = 0;
        }

        public void ResetCharge(float amount = 0f)
        {
            CancelBoost();
            Charge = Mathf.Clamp(amount, 0f, Capacity);
        }

        /// <summary>Начислить заряд (с учётом nitroChargeMultiplier шасси). notify=false — без события (тихий набор в дрифте).</summary>
        public void AddCharge(float amount, NitroGainSource source, bool notify = true)
        {
            if (amount <= 0f) return;
            float before = Charge;
            Charge = Mathf.Min(Capacity, Charge + amount * _car.Data.nitroChargeMultiplier);
            float gained = Charge - before;
            if (notify && gained > 0f) ChargeGained?.Invoke(source, gained);
        }

        private void OnLanded(float airTime)
        {
            if (airTime < balance.nitro.minAirtime) return;
            AddCharge(airTime * balance.nitro.chargePerAirSecond, NitroGainSource.Airtime);
        }

        /// <summary>Касание = пролёт впритирку не засчитывается.</summary>
        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody other = collision.rigidbody;
            if (other == null) return;
            int id = other.GetInstanceID();
            for (int i = 0; i < _trackedCount; i++)
            {
                if (_tracked[i].id == id) _tracked[i].touched = true;
            }
        }

        private void ScanNearMisses()
        {
            GameBalanceSO.NitroTuning n = balance.nitro;
            Rigidbody self = _car.Body;
            Vector3 selfPos = self.worldCenterOfMass;
            Vector3 selfVel = self.GetVelocity();

            for (int i = 0; i < _trackedCount; i++) _tracked[i].seenThisScan = false;

            int count = Physics.OverlapSphereNonAlloc(selfPos, n.nearMissRadius, _overlap, n.racerMask,
                QueryTriggerInteraction.Ignore);

            for (int c = 0; c < count; c++)
            {
                Rigidbody other = _overlap[c].attachedRigidbody;
                if (other == null || other == self) continue;
                if (!other.TryGetComponent(out ArcadeCarController otherCar)) continue;
                if (otherCar.TryGetComponent(out TakedownSystem td) && td.IsWrecked) continue;

                int id = other.GetInstanceID();
                int index = FindTracked(id);
                if (index < 0)
                {
                    if (_trackedCount >= MaxTracked) continue;
                    index = _trackedCount++;
                    _tracked[index] = new TrackedRacer { id = id, minDistance = float.MaxValue };
                }
                else if (_tracked[index].seenThisScan)
                {
                    continue; // несколько коллайдеров одной машины
                }

                float distance = Vector3.Distance(selfPos, other.worldCenterOfMass);
                float relSpeed = (selfVel - other.GetVelocity()).magnitude * 3.6f;
                ref TrackedRacer t = ref _tracked[index];
                t.seenThisScan = true;
                if (distance < t.minDistance) t.minDistance = distance;
                if (relSpeed > t.maxRelativeSpeed) t.maxRelativeSpeed = relSpeed;
            }

            // Кто вышел из зоны без касания на хорошей скорости — near miss.
            for (int i = _trackedCount - 1; i >= 0; i--)
            {
                if (_tracked[i].seenThisScan) continue;
                TrackedRacer t = _tracked[i];
                if (!t.touched && t.maxRelativeSpeed >= n.nearMissMinRelativeSpeedKmh && Time.time >= _nearMissCooldownUntil)
                {
                    _nearMissCooldownUntil = Time.time + n.nearMissCooldown;
                    AddCharge(n.nearMissCharge, NitroGainSource.NearMiss);
                }
                _tracked[i] = _tracked[--_trackedCount];
            }
        }

        private int FindTracked(int id)
        {
            for (int i = 0; i < _trackedCount; i++)
                if (_tracked[i].id == id) return i;
            return -1;
        }

        private void SetFx(bool boost, bool perfect)
        {
            for (int i = 0; i < boostParticles.Length; i++)
            {
                if (boostParticles[i] == null) continue;
                if (boost) boostParticles[i].Play(true);
                else boostParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            for (int i = 0; i < perfectFireParticles.Length; i++)
            {
                if (perfectFireParticles[i] == null) continue;
                if (perfect) perfectFireParticles[i].Play(true);
                else perfectFireParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            for (int i = 0; i < perfectFireTrails.Length; i++)
            {
                if (perfectFireTrails[i] != null) perfectFireTrails[i].emitting = perfect;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(GameBalanceSO balanceAsset, ParticleSystem[] boostFx, ParticleSystem[] perfectFx, TrailRenderer[] trails)
        {
            balance = balanceAsset;
            boostParticles = boostFx;
            perfectFireParticles = perfectFx;
            perfectFireTrails = trails;
        }
#endif
    }
}
