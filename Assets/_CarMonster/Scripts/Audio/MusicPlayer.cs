using System.Threading.Tasks;
using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Музыка локации. Играет свои клипы из LocationSO, а если их нет — генерирует трек
    /// в фоновом потоке (без фриза) и крутит его петлёй. В слоу-мо трек «плывёт»: ниже питч + фильтр.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class MusicPlayer : MonoBehaviour
    {
        private const int SampleRate = 44100;

        [SerializeField] private LocationSO location;
        [SerializeField] private GameBalanceSO balance;

        public bool IsReady => _source != null && _source.clip != null;
        public AudioClip CurrentClip => _source != null ? _source.clip : null;
        public LocationSO Location => location;

        private AudioSource _source;
        private AudioLowPassFilter _lowPass;
        private Task<float[]> _generation;
        private int _clipIndex;
        private float _volume01;
        private float _targetVolume01 = 1f;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 0f;
            _source.priority = 0;
            _source.ignoreListenerPause = true;
            _lowPass = GetComponent<AudioLowPassFilter>();
            if (_lowPass == null) _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            _lowPass.cutoffFrequency = 22000f;
        }

        private void Start()
        {
            if (location == null) return;

            if (location.musicClips != null && location.musicClips.Length > 0)
            {
                _clipIndex = Random.Range(0, location.musicClips.Length);
                PlayClip(location.musicClips[_clipIndex], location.musicClips.Length == 1);
                return;
            }

            MusicStyle style = location.musicStyle;
            _generation = Task.Run(() => ProceduralMusic.Generate(style, SampleRate));
        }

        /// <summary>Громкость относительно настроек (0..1), плавно: например, приглушить на экране результатов.</summary>
        public void SetVolume01(float value) => _targetVolume01 = Mathf.Clamp01(value);

        private void Update()
        {
            if (_generation != null && _generation.IsCompleted)
            {
                if (_generation.Status == TaskStatus.RanToCompletion)
                {
                    float[] data = _generation.Result;
                    var clip = AudioClip.Create($"Music_{location.displayName}", data.Length / 2, 2, SampleRate, false);
                    clip.SetData(data, 0);
                    PlayClip(clip, true);
                }
                else
                {
                    Debug.LogError($"MusicPlayer: генерация трека упала: {_generation.Exception}", this);
                }
                _generation = null;
            }

            if (_source.clip == null) return;

            // Плейлист из своих клипов: следующий трек по окончании текущего.
            if (!_source.loop && !_source.isPlaying && location.musicClips.Length > 1)
            {
                _clipIndex = (_clipIndex + 1) % location.musicClips.Length;
                PlayClip(location.musicClips[_clipIndex], false);
            }

            float dt = Time.unscaledDeltaTime;
            float fade = balance != null ? balance.audio.musicFadeSpeed : 1.5f;
            _volume01 = Mathf.MoveTowards(_volume01, _targetVolume01, fade * dt);
            float baseVolume = balance != null ? balance.audio.musicVolume : 0.55f;
            _source.volume = baseVolume * _volume01;

            bool slow = TimeScaleController.IsSlowMo;
            float targetPitch = slow && balance != null ? balance.audio.slowMoMusicPitch : 1f;
            float targetCutoff = slow && balance != null ? balance.audio.slowMoMusicCutoff : 22000f;
            _source.pitch = Mathf.MoveTowards(_source.pitch, targetPitch, dt * 2f);
            _lowPass.cutoffFrequency = Mathf.Lerp(_lowPass.cutoffFrequency, targetCutoff, 1f - Mathf.Exp(-8f * dt));
        }

        private void PlayClip(AudioClip clip, bool loop)
        {
            _source.clip = clip;
            _source.loop = loop;
            _source.Play();
        }

#if UNITY_EDITOR
        public void EditorSetup(LocationSO loc, GameBalanceSO balanceAsset)
        {
            location = loc;
            balance = balanceAsset;
        }
#endif
    }
}
