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
        public bool IsInvulnerable => IsReassembling || Time.time < _invulnerableUntil;
        public bool IsReassembling { get; private set; }

        /// <summary>Машина собралась и продолжила гонку.</summary>
        public static event Action<TakedownSystem> AnyReassembled;
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
            CacheRenderers();
        }

        // ---------- Обугливание ----------

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private Renderer[] _bodyRenderers;
        private Color[] _originalColors;
        private MaterialPropertyBlock _block;

        private void CacheRenderers()
        {
            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            int count = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i] is MeshRenderer) count++;

            _bodyRenderers = new Renderer[count];
            _originalColors = new Color[count];
            int k = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (!(all[i] is MeshRenderer)) continue;
                _bodyRenderers[k] = all[i];
                Material m = all[i].sharedMaterial;
                _originalColors[k] = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                k++;
            }
            _block = new MaterialPropertyBlock();
        }

        /// <summary>0 = родной цвет, 1 = полностью обуглен.</summary>
        private void SetCharred(float amount)
        {
            if (_bodyRenderers == null) return;
            Color charred = balance.combat.charredColor;
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                Renderer r = _bodyRenderers[i];
                if (r == null) continue;
                if (amount <= 0.001f)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                _block.SetColor(BaseColorId, Color.Lerp(_originalColors[i], charred, amount));
                r.SetPropertyBlock(_block);
            }
        }

        // ---------- Стены ----------

        private void TryWallCrash(Collision collision, ContactPoint contact)
        {
            if (IsInvulnerable) return;
            // Пол, дорога, трамплины — не стены (приземления не бьют).
            if (Mathf.Abs(contact.normal.y) > 0.5f) return;

            float normalKmh = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal)) * 3.6f;
            GameBalanceSO.CombatTuning c = balance.combat;
            if (normalKmh < c.wallCrashMinKmh) return;

            if (Debug.isDebugBuild)
                Debug.Log($"[CarMonster] {name} wall crash into {collision.collider.name}: {normalKmh:0} km/h, normal {contact.normal}");
            Vector3 toContact = contact.point - Car.Body.worldCenterOfMass;
            toContact.y = 0f;
            Vector3 hitDirection = toContact.sqrMagnitude > 0.001f ? -toContact.normalized : -transform.forward;
            ReceiveHit(null, normalKmh * c.wallDamagePerKmh, contact.point, hitDirection, true);
        }

        // ---------- Самосборка ----------

        private int _partsReturning;

        /// <summary>
        /// Анимация самосборки: корпус по дуге летит к точке на трассе, обломки слетаются на места,
        /// цвет «отгорает» обратно, машина стартует со скоростью launchKmh. Вызывается менеджером заезда.
        /// </summary>
        public System.Collections.IEnumerator Reassemble(Vector3 position, Quaternion rotation)
        {
            GameBalanceSO.CombatTuning c = balance.combat;
            IsReassembling = true;
            if (Nitro != null) Nitro.CancelBoost();
            Car.SetControlEnabled(false);
            Car.SetFrozen(true);
            if (wreckSmoke != null) wreckSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            Vector3 hover = position + Vector3.up * c.reassembleHoverHeight;
            Vector3 control = (startPos + hover) * 0.5f + Vector3.up * (6f + Vector3.Distance(startPos, hover) * 0.15f);
            float startCharred = IsWrecked ? 1f : 0f;

            // 1. Корпус летит к точке сборки и выравнивается.
            float t = 0f;
            while (t < c.reassembleMoveTime)
            {
                t += Time.deltaTime;
                float s = Smooth(t / c.reassembleMoveTime);
                Vector3 a = Vector3.Lerp(startPos, control, s);
                Vector3 b = Vector3.Lerp(control, hover, s);
                transform.SetPositionAndRotation(Vector3.Lerp(a, b, s), Quaternion.Slerp(startRot, rotation, s));
                yield return null;
            }
            transform.SetPositionAndRotation(hover, rotation);
            Car.Body.position = hover;
            Car.Body.rotation = rotation;

            // 2. Детали слетаются.
            _partsReturning = 0;
            float delay = 0f;
            for (int i = 0; i < detachableParts.Length; i++)
            {
                DetachablePart part = detachableParts[i];
                if (part == null || !part.IsDetached) continue;
                _partsReturning++;
                part.BeginReturn(this, delay, c.reassemblePartTime);
                delay += c.reassembleStagger;
            }

            float total = delay + c.reassemblePartTime;
            t = 0f;
            while (t < total || _partsReturning > 0)
            {
                t += Time.deltaTime;
                SetCharred(startCharred * (1f - Mathf.Clamp01(t / Mathf.Max(0.1f, total))));
                // Лёгкое покачивание в воздухе.
                transform.position = hover + Vector3.up * (Mathf.Sin(t * 9f) * 0.08f);
                if (t > total + 3f) break; // страховка
                yield return null;
            }

            // 3. Полный ремонт и старт.
            for (int i = 0; i < detachableParts.Length; i++)
                if (detachableParts[i] != null && detachableParts[i].IsDetached) detachableParts[i].Restore();
            SetCharred(0f);
            ShuffleParts();
            Health = MaxHealth;
            IsWrecked = false;
            LastAttacker = null;
            EmitSparks(transform.position, Vector3.up, 1f);

            Car.SetFrozen(false);
            Car.Launch(hover, rotation, c.reassembleLaunchKmh / 3.6f);
            IsReassembling = false;
            Repaired?.Invoke();
            AnyReassembled?.Invoke(this);
        }

        /// <summary>Деталь долетела до своего места.</summary>
        public void OnPartReturned(DetachablePart part)
        {
            _partsReturning = Mathf.Max(0, _partsReturning - 1);
            EmitSparks(part.transform.position, Vector3.up, 0.35f);
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
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
                bool isStatic = otherBody == null || otherBody.isKinematic;
                float wallSpeed = collision.relativeVelocity.magnitude * 3.6f;
                if (wallSpeed > balance.combat.minImpactSpeedKmh)
                    EmitSparks(contact.point, contact.normal, Mathf.Clamp01(wallSpeed / 200f));
                if (isStatic) TryWallCrash(collision, contact);
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

            // Ковш: удар лбом бронемашины всегда боевой и усиленный.
            bool plowHit = Car.Data.hasRamPlow && frontness > 0.5f;
            if (plowHit) combatHit = true;

            float damage = closingKmh * c.damagePerKmh * Car.Data.ramPower * massMultiplier * angleMultiplier * nitroMultiplier;
            if (plowHit) damage *= Car.Data.plowDamageMultiplier;
            if (!combatHit) damage *= c.weakHitMultiplier;

            victim.ReceiveHit(this, damage, contact.point, toVictim, combatHit);

            if (plowHit && !victim.IsWrecked)
            {
                // Ковш отпихивает жертву вперёд-вбок с трассы.
                Vector3 shoveDir = (toVictim + Vector3.up * 0.25f).normalized;
                float shove = Car.Data.plowShoveSpeed * Mathf.Clamp01(closingKmh / 120f);
                theirBody.AddForce(shoveDir * shove, ForceMode.VelocityChange);
            }
        }

        /// <summary>Удар пришёлся в ковш (атакующий спереди этой машины).</summary>
        public bool IsPlowFacing(Vector3 hitDirection) =>
            Car.Data.hasRamPlow && Vector3.Dot(transform.forward, -hitDirection) > 0.5f;

        /// <summary>
        /// Нанести урон. direction — направление удара (от атакующего к этой машине).
        /// Публичный, чтобы его могли вызывать ловушки/взрывы трассы.
        /// </summary>
        public void ReceiveHit(TakedownSystem attacker, float rawDamage, Vector3 point, Vector3 direction, bool heavy)
        {
            if (IsWrecked || IsInvulnerable) return;

            float damage = rawDamage * (1f - Car.Data.armor);
            if (IsPlowFacing(direction)) damage *= 1f - Car.Data.plowFrontalArmor;
            if (damage <= 0f) return;
            if (Debug.isDebugBuild && damage > 20f)
                Debug.Log($"[CarMonster] {name} hit by {(attacker != null ? attacker.name : "wall")}: {damage:0} dmg");

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
            if (Debug.isDebugBuild)
                Debug.Log($"[CarMonster] {name} WRECKED by {(attacker != null ? attacker.name : "crash")} (hp before {Health:0})");
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
            SetCharred(1f);

            if (attacker != null && attacker.Nitro != null)
                attacker.Nitro.AddCharge(c.takedownNitroReward, NitroGainSource.Takedown);

            // Слоу-мо только когда в аварии участвует игрок — чтобы чужие краши вдалеке не тормозили игру.
            ArcadeCarController player = TimeAttackManager.Instance != null ? TimeAttackManager.Instance.Player : null;
            bool involvesPlayer = player == null || Car == player || (attacker != null && attacker.Car == player);
            if (involvesPlayer)
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
            SetCharred(0f);
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
