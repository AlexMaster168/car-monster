using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Процедурный звук двигателя без сэмплов. Главный поток считает виртуальную коробку передач и обороты,
    /// аудиопоток (OnAudioFilterRead) синтезирует: гармоники частоты вспышек цилиндров, бубнёж,
    /// шум впуска/выхлопа, свист турбины, выстрелы на сбросе газа, визг шин в дрифте и шипение нитро.
    /// Электрокары — вой мотора и инвертора. В слоу-мо всё опускается по высоте.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(ArcadeCarController))]
    public class EngineAudio : MonoBehaviour
    {
        private const int Harmonics = 7;

        [SerializeField] private GameBalanceSO balance;
        [Tooltip("Звук своей машины игрока: 2D, громче. Ставится при спавне.")]
        [SerializeField] private bool isPlayer;

        public int Gear { get; private set; } = 1;
        public float Rpm { get; private set; }
        public bool IsPlayer => isPlayer;

        private static AudioClip _dcClip;

        private ArcadeCarController _car;
        private NitroSystem _nitro;
        private AudioSource _source;
        private EngineSoundProfile _profile;
        private float[] _gearTopSpeeds;
        private readonly float[] _harmonicWeights = new float[Harmonics];
        private float _shiftCooldown;
        private float _lastThrottle;

        // Параметры для аудиопотока (пишутся в Update, читаются в OnAudioFilterRead).
        private volatile float _aRpm;
        private volatile float _aThrottle;
        private volatile float _aBoost;
        private volatile float _aSqueal;
        private volatile float _aPitchScale = 1f;
        private volatile float _aGain;
        private volatile int _aPops;

        // Состояние аудиопотока.
        private int _sampleRate = 48000;
        private float _smoothRpm;
        private double _phase;
        private double _turboPhase;
        private double _whinePhase;
        private double _squealPhase;
        private float _cycleAmp = 1f;
        private float _noiseLp;
        private float _hissLp;
        private float _squealLp;
        private float _popEnv;
        private float _popTimer;
        private int _popsLeft;
        private uint _rng = 22695477u;
        private float _outLp;

        private void Awake()
        {
            _car = GetComponent<ArcadeCarController>();
            _nitro = GetComponent<NitroSystem>();
            _source = GetComponent<AudioSource>();
            _sampleRate = AudioSettings.outputSampleRate;
            _rng ^= (uint)GetInstanceID();
            BuildGearbox();
            ConfigureSource();
        }

        private void Start()
        {
            _source.clip = GetDcClip();
            _source.loop = true;
            _source.Play();
        }

        public void SetIsPlayer(bool value)
        {
            isPlayer = value;
            ConfigureSource();
        }

        private void ConfigureSource()
        {
            if (_source == null) return;
            _source.playOnAwake = false;
            _source.spatialBlend = isPlayer ? 0.15f : 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 6f;
            _source.maxDistance = 140f;
            _source.dopplerLevel = isPlayer ? 0f : 0.6f;
            _source.priority = isPlayer ? 16 : 96;
            _source.volume = 1f;
        }

        /// <summary>Клип из единиц: источник «играет», а синтез умножается на него — так работает 3D-затухание.</summary>
        private static AudioClip GetDcClip()
        {
            if (_dcClip != null) return _dcClip;
            int sr = AudioSettings.outputSampleRate;
            var ones = new float[sr];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            _dcClip = AudioClip.Create("EngineDC", sr, 1, sr, false);
            _dcClip.SetData(ones, 0);
            return _dcClip;
        }

        private void BuildGearbox()
        {
            _profile = _car.Data != null ? _car.Data.engineSound : new EngineSoundProfile();
            bool electric = _profile.type == EngineType.Electric;
            int gears = electric ? 1 : Mathf.Max(1, _profile.gears);
            _gearTopSpeeds = new float[gears];
            float top = (_car.Data != null ? _car.Data.MaxSpeedMs : 90f) * 1.12f;
            for (int g = 0; g < gears; g++)
            {
                // Прогрессивные передачи: короткие первые, длинные последние.
                float k = (g + 1f) / gears;
                _gearTopSpeeds[g] = top * Mathf.Pow(k, 0.75f);
            }
            float exponent = 2.2f - _profile.brightness * 1.4f;
            for (int k = 1; k <= Harmonics; k++) _harmonicWeights[k - 1] = 1f / Mathf.Pow(k, exponent);

            Rpm = _profile.idleRpm;
            _smoothRpm = Rpm;
        }

        private void Update()
        {
            if (_car.Data == null) return;
            float dt = Time.deltaTime;
            float speed = Mathf.Abs(_car.ForwardSpeed);
            CarInputSource input = _car.InputSource;
            float throttle = _car.ControlEnabled && input != null ? Mathf.Abs(input.Throttle) : 0f;
            bool boosting = _nitro != null && _nitro.IsBoosting;
            if (boosting) throttle = 1f;

            UpdateGearbox(speed, throttle, dt);

            // Сброс газа на высоких оборотах — выстрелы в выхлоп.
            if (_lastThrottle > 0.7f && throttle < 0.2f && Rpm > _profile.redlineRpm * 0.6f &&
                _profile.type != EngineType.Electric && Random.value < _profile.backfire)
            {
                _aPops = Random.Range(2, 6);
            }
            _lastThrottle = throttle;

            float lateral = Mathf.Abs(Vector3.Dot(_car.Body.GetVelocity(), transform.right));
            float squeal = _car.IsGrounded ? Mathf.Clamp01((lateral - 4f) / 10f) : 0f;
            if (_car.IsDrifting) squeal = Mathf.Max(squeal, 0.6f);

            float volume = balance != null
                ? (isPlayer ? balance.audio.engineVolume : balance.audio.botEngineVolume)
                : 0.6f;

            _aRpm = Rpm;
            _aThrottle = throttle;
            _aBoost = boosting ? (_nitro.IsPerfectBoost ? 1f : 0.6f) : 0f;
            _aSqueal = squeal;
            _aPitchScale = _profile.pitch * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Time.timeScale * 1.5f));
            _aGain = volume * _profile.volume;
        }

        private void UpdateGearbox(float speed, float throttle, float dt)
        {
            float idle = _profile.idleRpm;
            float redline = _profile.redlineRpm;
            int maxGear = _gearTopSpeeds.Length;
            _shiftCooldown -= dt;

            if (_shiftCooldown <= 0f)
            {
                if (Gear < maxGear && speed > _gearTopSpeeds[Gear - 1] * 0.96f)
                {
                    Gear++;
                    _shiftCooldown = 0.25f;
                    if (_profile.type != EngineType.Electric && Random.value < _profile.backfire * 0.6f) _aPops = 1;
                }
                else if (Gear > 1 && speed < _gearTopSpeeds[Gear - 2] * 0.7f)
                {
                    Gear--;
                    _shiftCooldown = 0.25f;
                }
            }

            float gearTop = _gearTopSpeeds[Gear - 1];
            float target = Mathf.Lerp(idle, redline, Mathf.Clamp01(speed / gearTop));

            // Пробуксовка/воздух: двигатель раскручивается газом сильнее колёс.
            if (!_car.IsGrounded || _car.IsDrifting)
                target = Mathf.Max(target, Mathf.Lerp(idle, redline * 0.95f, throttle));
            // На месте — перегазовка.
            if (speed < 2f) target = Mathf.Max(target, idle + (redline - idle) * 0.55f * throttle);

            Rpm = Mathf.Lerp(Rpm, Mathf.Min(target, redline), 1f - Mathf.Exp(-10f * dt));
        }

        private float NextNoise()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 8388608f - 1f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_profile == null) return;

            float targetRpm = _aRpm;
            float throttle = _aThrottle;
            float boost = _aBoost;
            float squeal = _aSqueal;
            float pitchScale = _aPitchScale;
            float gain = _aGain;
            if (_aPops > 0)
            {
                _popsLeft += _aPops;
                _aPops = 0;
            }

            float sr = _sampleRate;
            bool electric = _profile.type == EngineType.Electric;
            int cylinders = Mathf.Max(1, _profile.Cylinders);
            float roughness = _profile.roughness;
            float turbo = _profile.turbo;
            float redline = Mathf.Max(1000f, _profile.redlineRpm);
            int frames = data.Length / channels;

            for (int i = 0; i < frames; i++)
            {
                _smoothRpm += (targetRpm - _smoothRpm) * 0.0015f;
                float rpm01 = Mathf.Clamp01(_smoothRpm / redline);
                float sample;

                if (electric)
                {
                    // Вой мотора + инвертор + низкий гул.
                    float whineHz = (90f + _smoothRpm * 0.16f) * pitchScale;
                    _phase += whineHz / sr;
                    _whinePhase += whineHz * 3.02f / sr;
                    if (_phase > 1.0) _phase -= 1.0;
                    if (_whinePhase > 1.0) _whinePhase -= 1.0;
                    float p = (float)_phase * 2f * Mathf.PI;
                    sample = Mathf.Sin(p) * 0.45f + Mathf.Sin(2f * p) * 0.18f +
                             Mathf.Sin((float)_whinePhase * 2f * Mathf.PI) * 0.12f * (0.3f + throttle);
                    float noise = NextNoise();
                    _noiseLp += 0.05f * (noise - _noiseLp);
                    sample += _noiseLp * (0.15f + rpm01 * 0.3f);
                    sample *= 0.55f + 0.45f * throttle;
                }
                else
                {
                    float firingHz = _smoothRpm / 60f * cylinders * 0.5f * pitchScale;
                    _phase += firingHz / sr;
                    if (_phase >= 1.0)
                    {
                        _phase -= 1.0;
                        // Каждая вспышка чуть разная — «живой» мотор.
                        _cycleAmp = 1f + NextNoise() * roughness * 0.45f;
                    }

                    float p = (float)_phase * 2f * Mathf.PI;
                    float osc = 0f;
                    for (int k = 1; k <= Harmonics; k++)
                        osc += Mathf.Sin(p * k) * _harmonicWeights[k - 1];

                    float rumble = Mathf.Sin(p * 0.5f) * roughness * 0.8f;
                    float noise = NextNoise();
                    float cutoff = Mathf.Clamp(firingHz * 6f, 200f, 9000f);
                    _noiseLp += (1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sr)) * (noise - _noiseLp);

                    float load = 0.45f + 0.55f * throttle;
                    sample = (osc * 0.55f * _cycleAmp + rumble) * load + _noiseLp * (0.12f + 0.35f * throttle);

                    if (turbo > 0f)
                    {
                        _turboPhase += (1800f + _smoothRpm * 0.55f) * pitchScale / sr;
                        if (_turboPhase > 1.0) _turboPhase -= 1.0;
                        sample += Mathf.Sin((float)_turboPhase * 2f * Mathf.PI) * turbo * throttle * rpm01 * 0.06f;
                    }

                    float drive = 1.4f + 2.2f * throttle + boost;
                    sample = (float)System.Math.Tanh(sample * drive) * 0.55f;
                }

                // Выстрелы.
                if (_popsLeft > 0 && _popEnv < 0.05f)
                {
                    _popTimer -= 1f / sr;
                    if (_popTimer <= 0f)
                    {
                        _popEnv = 1f;
                        _popsLeft--;
                        _popTimer = 0.04f + (NextNoise() * 0.5f + 0.5f) * 0.09f;
                    }
                }
                if (_popEnv > 0.0005f)
                {
                    sample += NextNoise() * _popEnv * 0.9f;
                    _popEnv *= 0.9965f;
                }

                // Нитро — шипение.
                if (boost > 0f)
                {
                    float n = NextNoise();
                    _hissLp += 0.3f * (n - _hissLp);
                    sample += (n - _hissLp) * boost * 0.22f;
                }

                // Визг шин.
                if (squeal > 0.01f)
                {
                    float sqHz = (900f + NextNoise() * 60f) * pitchScale;
                    _squealPhase += sqHz / sr;
                    if (_squealPhase > 1.0) _squealPhase -= 1.0;
                    _squealLp += 0.02f * (NextNoise() - _squealLp);
                    sample += Mathf.Sin((float)_squealPhase * 2f * Mathf.PI) * (0.6f + _squealLp * 2f) * squeal * 0.16f;
                }

                // Лёгкий срез верхов в слоу-мо.
                _outLp += (pitchScale < 0.9f ? 0.15f : 1f) * (sample - _outLp);
                float outSample = _outLp * gain;

                for (int ch = 0; ch < channels; ch++)
                    data[i * channels + ch] *= outSample;
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(GameBalanceSO balanceAsset) => balance = balanceAsset;
#endif
    }
}
