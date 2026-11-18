using System;
using System.Collections;
using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Единственный владелец Time.timeScale: хитстоп + слоу-мо после тейкдауна и пауза.
    /// fixedDeltaTime масштабируется вместе с timeScale, чтобы физика в слоу-мо оставалась плавной.
    /// </summary>
    public class TimeScaleController : MonoBehaviour
    {
        private static TimeScaleController _instance;
        private const float RecoverDuration = 0.15f;

        public static event Action SlowMoStarted;
        public static event Action SlowMoEnded;
        public static bool IsSlowMo { get; private set; }
        public static bool IsPaused { get; private set; }

        private float _baseFixedDeltaTime = 0.02f;
        private Coroutine _routine;
        private float _currentScale = 1f;

        private static TimeScaleController Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("[TimeScaleController]");
                _instance = go.AddComponent<TimeScaleController>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _baseFixedDeltaTime = Time.fixedDeltaTime;
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _baseFixedDeltaTime;
            IsSlowMo = false;
            IsPaused = false;
            _instance = null;
        }

        /// <summary>Короткий стоп-кадр, затем слоу-мо. Длительности — в реальном времени.</summary>
        public static void PlayHitstop(float freezeDuration, float slowScale, float slowDuration)
        {
            TimeScaleController c = Instance;
            if (c._routine != null) c.StopCoroutine(c._routine);
            c._routine = c.StartCoroutine(c.HitstopRoutine(freezeDuration, slowScale, slowDuration));
        }

        public static void SetPaused(bool paused)
        {
            TimeScaleController c = Instance;
            IsPaused = paused;
            c.Apply(paused ? 0f : c._currentScale);
        }

        public static void ResetTime()
        {
            TimeScaleController c = Instance;
            if (c._routine != null) c.StopCoroutine(c._routine);
            c._routine = null;
            IsPaused = false;
            c.SetScale(1f);
            if (IsSlowMo)
            {
                IsSlowMo = false;
                SlowMoEnded?.Invoke();
            }
        }

        private IEnumerator HitstopRoutine(float freeze, float slowScale, float slowDuration)
        {
            IsSlowMo = true;
            SlowMoStarted?.Invoke();

            SetScale(0.02f);
            float t = 0f;
            while (t < freeze)
            {
                if (!IsPaused) t += Time.unscaledDeltaTime;
                yield return null;
            }

            SetScale(slowScale);
            t = 0f;
            while (t < slowDuration)
            {
                if (!IsPaused) t += Time.unscaledDeltaTime;
                yield return null;
            }

            t = 0f;
            while (t < RecoverDuration)
            {
                if (!IsPaused) t += Time.unscaledDeltaTime;
                SetScale(Mathf.Lerp(slowScale, 1f, t / RecoverDuration));
                yield return null;
            }

            SetScale(1f);
            IsSlowMo = false;
            _routine = null;
            SlowMoEnded?.Invoke();
        }

        private void SetScale(float scale)
        {
            _currentScale = scale;
            if (!IsPaused) Apply(scale);
        }

        private void Apply(float scale)
        {
            Time.timeScale = scale;
            Time.fixedDeltaTime = _baseFixedDeltaTime * Mathf.Max(scale, 0.01f);
        }
    }
}
