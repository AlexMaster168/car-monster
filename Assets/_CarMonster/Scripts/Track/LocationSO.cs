using UnityEngine;

namespace CarMonster
{
    public enum DecorStyle
    {
        CityTowers,
        DesertMesas,
        PortContainers
    }

    /// <summary>
    /// Локация: сцена, палитра (для сборщика трассы) и музыка. Свои треки кидаются в musicClips —
    /// тогда процедурная музыка не генерируется.
    /// </summary>
    [CreateAssetMenu(fileName = "Location_New", menuName = "Car Monster/Location", order = 2)]
    public class LocationSO : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Location";
        [Tooltip("Имя сцены в Build Settings.")]
        public string sceneName = "";
        [TextArea(2, 3)] public string description = "";

        [Header("Music")]
        [Tooltip("Свои треки. Если пусто — играет процедурный трек по musicStyle.")]
        public AudioClip[] musicClips = new AudioClip[0];
        public MusicStyle musicStyle = new MusicStyle();

        [Header("Track shape (сборщик сцены)")]
        public float straightLength = 520f;
        public float cornerRadius = 140f;
        [Tooltip("Амплитуда S-связки на обратной прямой, м.")]
        public float sBendAmplitude = 38f;
        [Tooltip("Количество полуволн S-связки (кратно 0.5).")]
        public float sBendWaves = 1f;

        [Header("Relief & attractions (сборщик сцены)")]
        [Tooltip("Высота главного холма на обратной половине трассы, м.")]
        public float hillHeight = 10f;
        [Tooltip("Волны рельефа поверх холма.")]
        public float hillWaves = 2f;
        [Tooltip("Гребни-трамплины на обратной прямой: высота, м (0 = нет).")]
        public float crestHeight = 2.5f;
        public int crestCount = 3;
        [Tooltip("Наклон дороги в поворотах (вираж), град.")]
        public float bankAngle = 12f;
        [Tooltip("Пропасть поперёк стартовой прямой, которую надо перепрыгнуть.")]
        public bool chasm;
        [Tooltip("Где начинается пропасть (доля стартовой прямой).")]
        [Range(0.2f, 0.8f)] public float chasmAt = 0.47f;
        public float chasmLength = 18f;
        [Tooltip("Неоновый тоннель на обратной прямой.")]
        public bool tunnel;
        public int boostPads = 5;
        [Tooltip("Плотность разлетающегося реквизита (конусы, бочки, знаки).")]
        [Range(0f, 2f)] public float propDensity = 1f;
        public Color barrierColorA = new Color(0.85f, 0.1f, 0.1f);
        public Color barrierColorB = new Color(0.92f, 0.92f, 0.92f);

        [Header("Look (сборщик сцены)")]
        public DecorStyle decor = DecorStyle.CityTowers;
        public Color groundColor = new Color(0.24f, 0.32f, 0.22f);
        public Color roadColor = new Color(0.13f, 0.13f, 0.15f);
        public Color decorColor = new Color(0.25f, 0.28f, 0.36f);
        public Color glowColor = new Color(0.1f, 0.8f, 1f);
        public Color fogColor = new Color(0.62f, 0.7f, 0.8f);
        public float fogStart = 250f;
        public float fogEnd = 900f;
        public Vector3 sunEuler = new Vector3(42f, -35f, 0f);
        public Color sunColor = Color.white;
        public float sunIntensity = 1.3f;
        public Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        public Color groundHorizon = new Color(0.37f, 0.35f, 0.34f);
        [Range(0f, 5f)] public float atmosphere = 1f;
        [Range(0f, 8f)] public float skyExposure = 1.3f;
        [Tooltip("Вместо процедурного неба — сплошной цвет (ночные локации).")]
        public bool solidSky;
        public Color solidSkyColor = new Color(0.07f, 0.04f, 0.12f);
    }
}
