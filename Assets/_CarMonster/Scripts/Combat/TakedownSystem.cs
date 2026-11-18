using System;
using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Контактный бой. Каждая машина сама решает, «атаковала» ли она соперника в момент
    /// столкновения: скорость сближения × масса × угол × нитро. HP соперника ушли в ноль — Knockout:
    /// отстрел деталей, искры/взрыв, хитстоп + слоу-мо, +время и нитро атакующему.
    /// </summary>
    [RequireComponent(typeof(ArcadeCarController))]
    public class TakedownSystem : MonoBehaviour
    {
        [SerializeField] private GameBalanceSO balance;
        [Tooltip("Если пусто — берутся все DetachablePart из дочерних объектов.")]
        [SerializeField] private DetachablePart[] detachableParts;

        [Header("FX (необязательно)")]
        [Tooltip("Искры: переносится в точку удара и делает Emit.")]
        [SerializeField] private ParticleSystem impactSparks;
        [SerializeField] private ParticleSystem wreckExplosion;
        [SerializeField] private ParticleSystem wreckSmoke;
        [SerializeField] private int sparksPerImpact = 40;

        /// <summary>attacker, victim — машина выбита.</summary>
        public static event Action<TakedownSystem, TakedownSystem> AnyTakedown;
        /// <summary>attacker, victim, damage, point — любой засчитанный удар.</summary>
        public static event Action<TakedownSystem, TakedownSystem, float, Vector3> AnyImpact;

        public event Action<TakedownSystem> Wrecked;
        public event Action Repaired;

        public ArcadeCarController Car { get; private set; }
        public NitroSystem Nitro { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth => Car.Data.durability;
        public float Health01 => MaxHealth > 0f ? Health / MaxHealth : 0f;
        public bool IsWrecked { get; private set; }
        public bool IsInvulnerable => Time.time < _invulnerableUntil;
        public TakedownSystem LastAttacker { get; private set; }
        public GameBalanceSO Balance => balance;

        private float _invulnerableUntil;
        private int _nextPartIndex;

        private void Awake()
        {
            Car = GetComponent<ArcadeCarController>();
            Nitro = GetComponent<NitroSystem>();
            if (balance == null)
            {
                Debug.LogError($"[{name}] TakedownSystem: не назначен GameBalanceSO.", this);
                enabled = false;
                return;
            }
            if (detachableParts == null || detachableParts.Length == 0)
                detachableParts = GetComponentsInChildren<DetachablePart>(true);
            ShuffleParts();
        }

        private void Start()
        {
            Health = MaxHealth;
            if (wreckSmoke != null) wreckSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetInvulnerable(float seconds) => _invulnerableUntil = Time.time + seconds;

        private void OnCollisionEnter(Collision collision)
        {
            if (!enabled || IsWrecked) return;

            ContactPoint contact = collision.GetContact(0);
            Rigidbody otherBody = collision.rigidbody;

            if (otherBody == null || !otherBody.TryGetComponent(out TakedownSystem other))
            {
                // Стена/отбойник: только искры, урона нет — аркада.
                float wallSpeed = collision.relativeVelocity.magnitude * 3.6f;
                if (wallSpeed > balance.combat.minImpactSpeedKmh)
                    EmitSparks(contact.point, contact.normal, Mathf.Clamp01(wallSpeed / 200f));
                return;
            }

            if (other == this || other.IsWrecked || other.IsInvulnerable || IsInvulnerable) return;
            TryAttack(other, collision, contact);
        }

        private void TryAttack(TakedownSystem victim, Collision collision, ContactPoint contact)
        {
            GameBalanceSO.CombatTuning c = balance.combat;
            Rigidbody myBody = Car.Body;
            Rigidbody theirBody = victim.Car.Body;

            Vector3 toVictim = theirBody.worldCenterOfMass - myBody.worldCenterOfMass;
            toVictim.y = 0f;
            if (toVictim.sqrMagnitude < 0.0001f) return;
            toVictim.Normalize();

            // relativeVelocity = скорость ДРУГОГО относительно меня, нам нужна наша относительно его.
            float closingMs = Vector3.Dot(-collision.relativeVelocity, toVictim);
            float closingKmh = closingMs * 3.6f;
            if (closingKmh < c.minImpactSpeedKmh) return;

            float frontness = Vector3.Dot(transform.forward, toVictim);
            if (frontness < c.minAttackFrontness) return; // это нас ударили, а не мы

            float sideHit = 1f - Mathf.Abs(Vector3.Dot(victim.transform.forward, toVictim));
            float angleMultiplier = Mathf.Lerp(1f, c.sideHitMultiplier, sideHit) * Mathf.Lerp(0.6f, 1f, frontness);

            float massRatio = myBody.mass / Mathf.Max(1f, theirBody.mass);
            float massMultiplier = Mathf.Clamp(Mathf.Pow(massRatio, c.massInfluence), 0.35f, 2.8f);

            bool nitro = Nitro != null && Nitro.IsBoosting;
            float nitroMultiplier = nitro ? (Nitro.IsPerfectBoost ? c.perfectNitroDamageMultiplier : c.nitroDamageMultiplier) : 1f;
            bool combatHit = nitro || closingKmh >= c.highSpeedImpactKmh;

            float damage = closingKmh * c.damagePerKmh * Car.Data.ramPower * massMultiplier * angleMultiplier * nitroMultiplier;
            if (!combatHit) damage *= c.weakHitMultiplier;

            victim.ReceiveHit(this, damage, contact.point, toVictim, combatHit);
        }

        /// <summary>Нанести урон. Публичный, чтобы его могли вызывать ловушки/взрывы трассы.</summary>
        public void ReceiveHit(TakedownSystem attacker, float rawDamage, Vector3 point, Vector3 direction, bool heavy)
        {
            if (IsWrecked || IsInvulnerable) return;

            float damage = rawDamage * (1f - Car.Data.armor);
            if (damage <= 0f) return;

            Health -= damage;
            LastAttacker = attacker;
            EmitSparks(point, -direction, Mathf.Clamp01(damage / 60f) + 0.25f);
            AnyImpact?.Invoke(attacker, this, damage, point);

            if (Health <= 0f)
            {
                Knockout(attacker, direction);
                return;
            }

            if (heavy && damage >= balance.combat.partDetachDamage)
                DetachNextPart(direction);
        }

        public void Knockout(TakedownSystem attacker, Vector3 direction)
        {
            if (IsWrecked) return;
            IsWrecked = true;
            Health = 0f;

            GameBalanceSO.CombatTuning c = balance.combat;
            Car.SetControlEnabled(false);
            if (Nitro != null) Nitro.CancelBoost();

            for (int i = 0; i < detachableParts.Length; i++)
                DetachPart(detachableParts[i], direction);

            Rigidbody body = Car.Body;
            body.AddForce((direction + Vector3.up * 0.6f).normalized * c.knockoutLaunchSpeed, ForceMode.VelocityChange);
            body.AddTorque(UnityEngine.Random.onUnitSphere * 6f, ForceMode.VelocityChange);

            if (wreckExplosion != null) wreckExplosion.Play(true);
            if (wreckSmoke != null) wreckSmoke.Play(true);

            if (attacker != null && attacker.Nitro != null)
                attacker.Nitro.AddCharge(c.takedownNitroReward, NitroGainSource.Takedown);

            TimeScaleController.PlayHitstop(c.hitstopFreezeDuration, c.slowMoTimeScale, c.slowMoDuration);

            Wrecked?.Invoke(this);
            AnyTakedown?.Invoke(attacker, this);
        }

        /// <summary>Полный ремонт: детали на место, HP в максимум, управление включено.</summary>
        public void Repair()
        {
            for (int i = 0; i < detachableParts.Length; i++)
                if (detachableParts[i] != null) detachableParts[i].Restore();

            ShuffleParts();
            Health = MaxHealth;
            IsWrecked = false;
            LastAttacker = null;
            Car.SetControlEnabled(true);
            if (wreckSmoke != null) wreckSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Repaired?.Invoke();
        }

        private void DetachNextPart(Vector3 direction)
        {
            while (_nextPartIndex < detachableParts.Length)
            {
                DetachablePart part = detachableParts[_nextPartIndex++];
                if (part != null && !part.IsDetached)
                {
                    DetachPart(part, direction);
                    return;
                }
            }
        }

        private void DetachPart(DetachablePart part, Vector3 direction)
        {
            if (part == null || part.IsDetached) return;
            Vector3 outward = (part.transform.position - Car.Body.worldCenterOfMass).normalized;
            Vector3 velocity = (outward + direction * 0.5f + Vector3.up * 0.7f).normalized * balance.combat.partEjectSpeed;
            part.Detach(velocity, Car.Body.GetVelocity());
        }

        private void ShuffleParts()
        {
            _nextPartIndex = 0;
            for (int i = detachableParts.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (detachableParts[i], detachableParts[j]) = (detachableParts[j], detachableParts[i]);
            }
        }

        private void EmitSparks(Vector3 point, Vector3 normal, float intensity01)
        {
            if (impactSparks == null) return;
            Transform t = impactSparks.transform;
            t.position = point;
            if (normal.sqrMagnitude > 0.001f) t.rotation = Quaternion.LookRotation(normal);
            impactSparks.Emit(Mathf.Max(1, Mathf.RoundToInt(sparksPerImpact * intensity01)));
        }

#if UNITY_EDITOR
        public void EditorSetup(GameBalanceSO balanceAsset, ParticleSystem sparks, ParticleSystem explosion, ParticleSystem smoke)
        {
            balance = balanceAsset;
            impactSparks = sparks;
            wreckExplosion = explosion;
            wreckSmoke = smoke;
        }
#endif
    }
}
