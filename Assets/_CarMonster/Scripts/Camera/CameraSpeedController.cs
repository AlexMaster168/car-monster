using System.Collections;
using UnityEngine;
#if CINEMACHINE_3
using Unity.Cinemachine;
#elif CINEMACHINE_2
using Cinemachine;
#endif

namespace CarMonster
{
    /// <summary>
    /// «Сок» камеры: FOV растёт со скоростью и нитро, тряска при ударах/приземлениях,
    /// во время слоу-мо тейкдауна включается отдельная камера с фокусом на разбитой машине.
    /// Cinemachine 3 (Unity 6) и Cinemachine 2 (2022.3) поддерживаются через версионные дефайны;
    /// без Cinemachine работает с обычной Camera.
    /// </summary>
    public class CameraSpeedController : MonoBehaviour
    {
        [SerializeField] private GameBalanceSO balance;
        [SerializeField] private ArcadeCarController target;

#if CINEMACHINE_3
        [SerializeField] private CinemachineCamera followCamera;
        [Tooltip("Камера тейкдауна (Priority выше followCamera, объект выключен по умолчанию).")]
        [SerializeField] private CinemachineCamera takedownCamera;
        [SerializeField] private CinemachineImpulseSource impulseSource;
#elif CINEMACHINE_2
        [SerializeField] private CinemachineVirtualCamera followCamera;
        [SerializeField] private CinemachineVirtualCamera takedownCamera;
        [SerializeField] private CinemachineImpulseSource impulseSource;
#else
        [SerializeField] private Camera fallbackCamera;
        [Tooltip("Трансформ, который трясём (обычно сама камера внутри рига).")]
        [SerializeField] private Transform shakeTransform;
#endif

        private NitroSystem _nitro;
        private TakedownSystem _takedown;
        private float _currentFov;
        private Coroutine _focusRoutine;
#if !CINEMACHINE_3 && !CINEMACHINE_2
        private float _shakeAmount;
        private Vector3 _shakeBasePosition;
        private Transform _focusTarget;
#endif

        private void Awake()
        {
            if (balance == null)
            {
                Debug.LogError("CameraSpeedController: не назначен GameBalanceSO.", this);
                enabled = false;
                return;
            }
            SetTarget(target);
            _currentFov = balance.cameraFx.baseFov;
#if !CINEMACHINE_3 && !CINEMACHINE_2
            if (fallbackCamera == null) fallbackCamera = Camera.main;
            if (shakeTransform == null && fallbackCamera != null) shakeTransform = fallbackCamera.transform;
            if (shakeTransform != null) _shakeBasePosition = shakeTransform.localPosition;
#endif
            SetTakedownCameraActive(false);
        }

        private void OnEnable()
        {
            TakedownSystem.AnyImpact += OnImpact;
            TakedownSystem.AnyTakedown += OnTakedown;
            if (target != null) target.Landed += OnLanded;
        }

        private void OnDisable()
        {
            TakedownSystem.AnyImpact -= OnImpact;
            TakedownSystem.AnyTakedown -= OnTakedown;
            if (target != null) target.Landed -= OnLanded;
        }

        public void SetTarget(ArcadeCarController car)
        {
            if (target != null && isActiveAndEnabled) target.Landed -= OnLanded;
            target = car;
            _nitro = car != null ? car.GetComponent<NitroSystem>() : null;
            _takedown = car != null ? car.GetComponent<TakedownSystem>() : null;
            if (target != null && isActiveAndEnabled) target.Landed += OnLanded;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            GameBalanceSO.CameraTuning c = balance.cameraFx;
            // unscaled — чтобы FOV и тряска жили и во время слоу-мо.
            float dt = Time.unscaledDeltaTime;

            float speed01 = Mathf.Clamp01(target.SpeedKmh / Mathf.Max(1f, target.Data.maxSpeedKmh));
            float fov = Mathf.Lerp(c.baseFov, c.maxSpeedFov, speed01 * speed01);
            if (_nitro != null && _nitro.IsBoosting)
            {
                fov += c.nitroFovBonus;
                if (_nitro.IsPerfectBoost) fov += c.perfectFovBonus;
            }

            _currentFov = Mathf.Lerp(_currentFov, fov, 1f - Mathf.Exp(-c.fovLerpSpeed * dt));
            ApplyFov(_currentFov);

#if !CINEMACHINE_3 && !CINEMACHINE_2
            UpdateFallbackShakeAndFocus(dt);
#endif
        }

        private void ApplyFov(float fov)
        {
#if CINEMACHINE_3
            if (followCamera != null) followCamera.Lens.FieldOfView = fov;
#elif CINEMACHINE_2
            if (followCamera != null) followCamera.m_Lens.FieldOfView = fov;
#else
            if (fallbackCamera != null) fallbackCamera.fieldOfView = fov;
#endif
        }

        public void Shake(float strength)
        {
            strength = Mathf.Min(strength, balance.cameraFx.maxShake);
            if (strength <= 0.01f) return;
#if CINEMACHINE_3 || CINEMACHINE_2
            if (impulseSource != null) impulseSource.GenerateImpulseWithForce(strength);
#else
            _shakeAmount = Mathf.Max(_shakeAmount, strength);
#endif
        }

        private void OnImpact(TakedownSystem attacker, TakedownSystem victim, float damage, Vector3 point)
        {
            if (_takedown == null || (attacker != _takedown && victim != _takedown)) return;
            Shake(damage * balance.cameraFx.shakePerDamage);
        }

        private void OnLanded(float airTime)
        {
            if (airTime > 0.4f) Shake(airTime * balance.cameraFx.landingShakePerSecondAir);
        }

        private void OnTakedown(TakedownSystem attacker, TakedownSystem victim)
        {
            if (_takedown == null || (attacker != _takedown && victim != _takedown)) return;
            Shake(balance.cameraFx.takedownShake);
            if (_focusRoutine != null) StopCoroutine(_focusRoutine);
            _focusRoutine = StartCoroutine(FocusRoutine(victim.transform));
        }

        private IEnumerator FocusRoutine(Transform victim)
        {
#if CINEMACHINE_3 || CINEMACHINE_2
            if (takedownCamera != null)
            {
                takedownCamera.Follow = victim;
                takedownCamera.LookAt = victim;
            }
#else
            _focusTarget = victim;
#endif
            SetTakedownCameraActive(true);

            float duration = balance.combat.hitstopFreezeDuration + balance.combat.slowMoDuration + balance.cameraFx.takedownFocusHold;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            SetTakedownCameraActive(false);
#if !CINEMACHINE_3 && !CINEMACHINE_2
            _focusTarget = null;
#endif
            _focusRoutine = null;
        }

        private void SetTakedownCameraActive(bool active)
        {
#if CINEMACHINE_3 || CINEMACHINE_2
            if (takedownCamera != null) takedownCamera.gameObject.SetActive(active);
#endif
        }

#if !CINEMACHINE_3 && !CINEMACHINE_2
        private void UpdateFallbackShakeAndFocus(float dt)
        {
            if (shakeTransform != null)
            {
                _shakeAmount = Mathf.MoveTowards(_shakeAmount, 0f, dt * 3f);
                shakeTransform.localPosition = _shakeBasePosition + Random.insideUnitSphere * (_shakeAmount * 0.25f);
            }
            if (_focusTarget != null && fallbackCamera != null)
            {
                Transform cam = fallbackCamera.transform;
                Quaternion look = Quaternion.LookRotation(_focusTarget.position - cam.position);
                cam.rotation = Quaternion.Slerp(cam.rotation, look, 1f - Mathf.Exp(-8f * dt));
            }
        }
#endif

#if UNITY_EDITOR && CINEMACHINE_3
        public void EditorSetup(GameBalanceSO balanceAsset, ArcadeCarController car, CinemachineCamera follow,
            CinemachineCamera takedown, CinemachineImpulseSource impulse)
        {
            balance = balanceAsset;
            target = car;
            followCamera = follow;
            takedownCamera = takedown;
            impulseSource = impulse;
        }
#endif
    }
}
