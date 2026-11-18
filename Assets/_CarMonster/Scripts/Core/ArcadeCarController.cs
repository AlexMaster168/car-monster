using System;
using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Аркадная физика гиперкара: Rigidbody + raycast-подвеска без WheelCollider.
    /// Газ/руль/сцепление считаются напрямую, поэтому машина предсказуема на любой скорости.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-50)]
    public class ArcadeCarController : MonoBehaviour
    {
        [Serializable]
        public class Wheel
        {
            [Tooltip("Точка крепления подвески (верх стойки). Должна быть внутри коллайдера корпуса.")]
            public Transform anchor;
            [Tooltip("Меш колеса (необязательно).")]
            public Transform visual;
            public bool isFront;

            [NonSerialized] public bool grounded;
            [NonSerialized] public float compression;
            [NonSerialized] public float lastCompression;
            [NonSerialized] public float hitDistance;
            [NonSerialized] public Vector3 hitNormal;
            [NonSerialized] public float spinDegrees;
        }

        [SerializeField] private CarDataSO data;
        [SerializeField] private CarInputSource input;
        [SerializeField] private Wheel[] wheels = new Wheel[4];
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Если не задано — используется centerOfMassOffset.")]
        [SerializeField] private Transform centerOfMass;
        [SerializeField] private Vector3 centerOfMassOffset = new Vector3(0f, 0.3f, 0f);
        [SerializeField] private float maxVisualSteerAngle = 28f;
        [Tooltip("Дальность луча вниз для поиска поверхности приземления.")]
        [SerializeField] private float landingProbeDistance = 40f;
        [SerializeField] private float flipRecoverDelay = 2f;

        public event Action DriftStarted;
        /// <summary>Аргумент — длительность дрифта, сек.</summary>
        public event Action<float> DriftEnded;
        /// <summary>Аргумент — время в воздухе, сек.</summary>
        public event Action<float> Landed;

        public CarDataSO Data => data;
        public CarInputSource InputSource => input;
        public Rigidbody Body => _rb;
        public bool IsGrounded { get; private set; }
        public bool IsDrifting { get; private set; }
        public float DriftDirection { get; private set; }
        public float DriftTime { get; private set; }
        public float AirTime { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float SpeedKmh => _rb != null ? _rb.GetVelocity().magnitude * 3.6f : 0f;
        /// <summary>0..1 относительно базовой максималки (без нитро).</summary>
        public float Speed01 => data != null ? Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / data.MaxSpeedMs) : 0f;
        public bool ControlEnabled { get; private set; } = true;
        public float BoostAccelMultiplier { get; private set; } = 1f;
        public float BoostSpeedBonusKmh { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        private Rigidbody _rb;
        private float _throttle;
        private float _steer;
        private bool _driftHeld;
        private float _landingTimer;
        private float _flipTimer;
        private bool _wasGrounded = true;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (input == null) input = GetComponent<CarInputSource>();
            ApplyData();
        }

        /// <summary>Переприменить характеристики (например, после смены CarDataSO в рантайме).</summary>
        public void ApplyData()
        {
            if (data == null)
            {
                Debug.LogError($"[{name}] ArcadeCarController: не назначен CarDataSO.", this);
                enabled = false;
                return;
            }

            _rb.mass = data.mass;
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.SetDamping(0.02f, 1.5f);
            _rb.maxAngularVelocity = 25f;
            _rb.centerOfMass = centerOfMass != null
                ? transform.InverseTransformPoint(centerOfMass.position)
                : centerOfMassOffset;
        }

        public void SetData(CarDataSO newData)
        {
            data = newData;
            if (_rb != null) ApplyData();
        }

        public void SetInputSource(CarInputSource source) => input = source;

        public void SetControlEnabled(bool value)
        {
            ControlEnabled = value;
            if (!value)
            {
                _throttle = 0f;
                _steer = 0f;
                if (IsDrifting) EndDrift();
            }
        }

        /// <summary>Вызывается NitroSystem: множитель тяги и прибавка к максималке.</summary>
        public void SetBoost(float accelMultiplier, float speedBonusKmh)
        {
            BoostAccelMultiplier = Mathf.Max(1f, accelMultiplier);
            BoostSpeedBonusKmh = Mathf.Max(0f, speedBonusKmh);
        }

        /// <summary>Мгновенно добавить скорость вдоль носа (рывок нитро).</summary>
        public void AddForwardImpulse(float metersPerSecond)
        {
            _rb.AddForce(transform.forward * metersPerSecond, ForceMode.VelocityChange);
        }

        /// <summary>Телепорт с полным сбросом состояния (респавн).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _rb.SetVelocity(Vector3.zero);
            _rb.angularVelocity = Vector3.zero;
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (IsDrifting) EndDrift();
            AirTime = 0f;
            _landingTimer = 0f;
            _flipTimer = 0f;
            _wasGrounded = true;
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i].compression = 0f;
                wheels[i].lastCompression = 0f;
            }
            if (input != null) input.ResetInput();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            ReadInput();

            int groundedCount = UpdateSuspension(dt);
            IsGrounded = groundedCount >= 2;

            Vector3 velocity = _rb.GetVelocity();
            ForwardSpeed = Vector3.Dot(velocity, transform.forward);

            UpdateDriftState();

            if (IsGrounded)
            {
                if (!_wasGrounded) OnLanding();
                ApplyDrive();
                ApplySteering(dt);
                ApplyGrip(dt);
                ApplyLandingAlignment(dt);
                _rb.AddForce(-GroundNormal * (data.downforce * Speed01 * Speed01), ForceMode.Acceleration);
            }
            else
            {
                AirTime += dt;
                ApplyAirControl();
            }

            ApplySpeedCap();
            UpdateFlipRecovery(dt);
            _wasGrounded = IsGrounded;
        }

        private void ReadInput()
        {
            if (!ControlEnabled || input == null)
            {
                _throttle = 0f;
                _steer = 0f;
                _driftHeld = false;
                return;
            }

            _throttle = input.Throttle;
            _steer = input.Steer;
            _driftHeld = input.Drift;
            // Пока работает нитро — газ всегда в пол.
            if (BoostAccelMultiplier > 1.01f) _throttle = 1f;
        }

        private int UpdateSuspension(float dt)
        {
            int groundedCount = 0;
            Vector3 normalSum = Vector3.zero;
            Vector3 up = transform.up;
            float rayLength = data.suspensionLength + data.wheelRadius;
            float massPerWheel = _rb.mass / Mathf.Max(1, wheels.Length);

            for (int i = 0; i < wheels.Length; i++)
            {
                Wheel w = wheels[i];
                if (w.anchor == null) continue;

                w.lastCompression = w.compression;
                Vector3 origin = w.anchor.position;

                if (Physics.Raycast(origin, -up, out RaycastHit hit, rayLength, groundMask, QueryTriggerInteraction.Ignore))
                {
                    w.grounded = true;
                    w.hitDistance = hit.distance;
                    w.hitNormal = hit.normal;
                    float springLength = hit.distance - data.wheelRadius;
                    w.compression = 1f - Mathf.Clamp01(springLength / data.suspensionLength);

                    float compressionVelocity = (w.compression - w.lastCompression) / dt;
                    float accel = w.compression * data.springStrength + compressionVelocity * data.springDamper;
                    if (accel > 0f)
                        _rb.AddForceAtPosition(up * (accel * massPerWheel), origin, ForceMode.Force);

                    groundedCount++;
                    normalSum += hit.normal;
                }
                else
                {
                    w.grounded = false;
                    w.hitDistance = rayLength;
                    w.compression = 0f;
                }
            }

            GroundNormal = groundedCount > 0 ? normalSum.normalized : Vector3.up;
            return groundedCount;
        }

        private void UpdateDriftState()
        {
            float minSpeed = data.driftMinSpeedKmh / 3.6f;

            if (!IsDrifting)
            {
                if (_driftHeld && IsGrounded && ForwardSpeed > minSpeed && Mathf.Abs(_steer) > 0.3f)
                {
                    IsDrifting = true;
                    DriftDirection = Mathf.Sign(_steer);
                    DriftTime = 0f;
                    DriftStarted?.Invoke();
                }
                return;
            }

            bool lost = !_driftHeld || ForwardSpeed < minSpeed * 0.7f || !IsGrounded;
            if (lost) EndDrift();
            else DriftTime += Time.fixedDeltaTime;
        }

        private void EndDrift()
        {
            IsDrifting = false;
            float duration = DriftTime;
            DriftTime = 0f;
            DriftEnded?.Invoke(duration);
        }

        private void ApplyDrive()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, GroundNormal).normalized;
            float maxSpeed = (data.maxSpeedKmh + BoostSpeedBonusKmh) / 3.6f;
            float speed01 = Speed01;

            if (_throttle > 0.01f)
            {
                if (ForwardSpeed < -0.5f)
                {
                    _rb.AddForce(forward * (data.brakeDeceleration * _throttle), ForceMode.Acceleration);
                }
                else if (ForwardSpeed < maxSpeed)
                {
                    float launch = 1f + data.launchBoost * (1f - speed01) * (1f - speed01);
                    float accel = data.acceleration * data.accelerationCurve.Evaluate(speed01) * launch;
                    if (BoostAccelMultiplier > 1f)
                    {
                        // Нитро добавляет тягу сверху и не душится кривой на высоких скоростях.
                        accel = Mathf.Max(accel, data.acceleration * 0.35f) * BoostAccelMultiplier * data.nitroForceMultiplier;
                    }
                    if (IsDrifting) accel *= 0.75f;
                    _rb.AddForce(forward * (accel * _throttle), ForceMode.Acceleration);
                }
            }
            else if (_throttle < -0.01f)
            {
                if (ForwardSpeed > 0.5f)
                    _rb.AddForce(-forward * (data.brakeDeceleration * -_throttle), ForceMode.Acceleration);
                else if (ForwardSpeed > -data.reverseMaxSpeedKmh / 3.6f)
                    _rb.AddForce(forward * (data.acceleration * 0.5f * _throttle), ForceMode.Acceleration);
            }
            else
            {
                _rb.AddForce(-forward * (ForwardSpeed * data.coastDrag), ForceMode.Acceleration);
            }
        }

        private void ApplySteering(float dt)
        {
            float speed01 = Speed01;
            float lowSpeedFade = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / 6f);
            float targetYawDeg;

            if (IsDrifting)
            {
                // Руль в сторону заноса — дуга круче, против — шире. Отпустил кнопку — выход.
                float tightness = 1f + _steer * DriftDirection * data.driftSteerRange;
                targetYawDeg = DriftDirection * data.driftPower * tightness;
            }
            else
            {
                float rate = Mathf.Lerp(data.steerRateLowSpeed, data.steerRateHighSpeed, speed01);
                float direction = ForwardSpeed >= 0f ? 1f : -1f;
                targetYawDeg = _steer * rate * lowSpeedFade * direction;
            }

            Vector3 localAngular = transform.InverseTransformDirection(_rb.angularVelocity);
            localAngular.y = Mathf.MoveTowards(localAngular.y, targetYawDeg * Mathf.Deg2Rad, data.steerResponse * dt);
            _rb.angularVelocity = transform.TransformDirection(localAngular);
        }

        private void ApplyGrip(float dt)
        {
            Vector3 velocity = _rb.GetVelocity();
            Vector3 right = transform.right;
            float lateral = Vector3.Dot(velocity, right);
            float grip = IsDrifting ? data.driftGrip : data.grip;
            float removal = 1f - Mathf.Exp(-grip * dt);

            Vector3 planarBefore = Vector3.ProjectOnPlane(velocity, GroundNormal);
            velocity -= right * (lateral * removal);

            if (IsDrifting)
            {
                // Боковое скольжение «перетекает» в продольную скорость — дрифт не убивает темп.
                Vector3 planarAfter = Vector3.ProjectOnPlane(velocity, GroundNormal);
                float lostSpeed = planarBefore.magnitude - planarAfter.magnitude;
                if (lostSpeed > 0f && planarAfter.sqrMagnitude > 0.01f)
                    velocity += planarAfter.normalized * (lostSpeed * data.driftSpeedRetention);
            }

            _rb.SetVelocity(velocity);
        }

        private void ApplyAirControl()
        {
            _rb.AddForce(Vector3.down * data.extraAirGravity, ForceMode.Acceleration);

            Vector3 targetUp = Vector3.up;
            if (Physics.Raycast(_rb.worldCenterOfMass, Vector3.down, out RaycastHit hit, landingProbeDistance,
                    groundMask, QueryTriggerInteraction.Ignore))
            {
                targetUp = hit.normal;
            }

            Vector3 angular = _rb.angularVelocity;
            Vector3 yawPart = Vector3.Project(angular, targetUp);
            Vector3 tiltPart = angular - yawPart;
            Vector3 correction = Vector3.Cross(transform.up, targetUp);

            Vector3 torque = correction * data.airStabilization - tiltPart * data.airAngularDamping;
            torque += targetUp * (_steer * data.airYawControl) - yawPart * 0.5f;
            _rb.AddTorque(torque, ForceMode.Acceleration);
        }

        private void OnLanding()
        {
            float air = AirTime;
            AirTime = 0f;
            if (air < 0.1f) return;

            _landingTimer = data.landingAlignTime;

            // Гасим отскок вверх и «клевок» корпуса.
            Vector3 velocity = _rb.GetVelocity();
            float alongNormal = Vector3.Dot(velocity, GroundNormal);
            if (alongNormal > 0f) velocity -= GroundNormal * (alongNormal * 0.8f);
            _rb.SetVelocity(velocity);

            Landed?.Invoke(air);
        }

        private void ApplyLandingAlignment(float dt)
        {
            if (_landingTimer <= 0f) return;
            _landingTimer -= dt;

            Quaternion target = Quaternion.FromToRotation(transform.up, GroundNormal) * _rb.rotation;
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, target, 1f - Mathf.Exp(-data.landingAlignSpeed * dt)));

            Vector3 localAngular = transform.InverseTransformDirection(_rb.angularVelocity);
            localAngular.x *= 0.5f;
            localAngular.z *= 0.5f;
            _rb.angularVelocity = transform.TransformDirection(localAngular);
        }

        private void ApplySpeedCap()
        {
            float maxSpeed = (data.maxSpeedKmh + BoostSpeedBonusKmh) / 3.6f;
            if (ForwardSpeed > maxSpeed)
            {
                // Мягко сбрасываем лишнее после окончания нитро, без «стены».
                _rb.AddForce(-transform.forward * ((ForwardSpeed - maxSpeed) * 1.5f), ForceMode.Acceleration);
            }
        }

        private void UpdateFlipRecovery(float dt)
        {
            bool upsideDown = Vector3.Dot(transform.up, Vector3.up) < 0.2f;
            if (upsideDown && _rb.GetVelocity().sqrMagnitude < 9f)
            {
                _flipTimer += dt;
                if (_flipTimer >= flipRecoverDelay)
                {
                    _flipTimer = 0f;
                    Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                    Teleport(_rb.position + Vector3.up * 1.5f, Quaternion.LookRotation(forward.normalized, Vector3.up));
                }
            }
            else
            {
                _flipTimer = 0f;
            }
        }

        private void LateUpdate()
        {
            if (data == null) return;
            float dt = Time.deltaTime;
            float steerAngle = (IsDrifting ? -DriftDirection * 0.6f + _steer * 0.4f : _steer) * maxVisualSteerAngle;
            float spinDelta = ForwardSpeed / (2f * Mathf.PI * data.wheelRadius) * 360f * dt;

            for (int i = 0; i < wheels.Length; i++)
            {
                Wheel w = wheels[i];
                if (w.anchor == null || w.visual == null) continue;

                float springLength = Mathf.Min(w.hitDistance, data.suspensionLength + data.wheelRadius) - data.wheelRadius;
                w.visual.position = w.anchor.position - transform.up * Mathf.Max(0f, springLength);
                w.spinDegrees = (w.spinDegrees + spinDelta) % 360f;
                float yaw = w.isFront ? steerAngle : 0f;
                w.visual.rotation = transform.rotation * Quaternion.Euler(w.spinDegrees, yaw, 0f);
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(CarDataSO carData, CarInputSource source, Wheel[] wheelSetup)
        {
            data = carData;
            input = source;
            wheels = wheelSetup;
        }

        private void OnDrawGizmosSelected()
        {
            if (wheels == null || data == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null || wheels[i].anchor == null) continue;
                Vector3 p = wheels[i].anchor.position;
                Gizmos.DrawLine(p, p - transform.up * (data.suspensionLength + data.wheelRadius));
            }
        }
#endif
    }
}
