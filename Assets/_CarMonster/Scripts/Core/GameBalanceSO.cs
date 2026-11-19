using System;
using UnityEngine;

namespace CarMonster
{
    /// <summary>
    /// Глобальный баланс заезда: нитро, бой, таймер, ИИ, камера. Один ассет на игру.
    /// Пер-машинные параметры лежат в <see cref="CarDataSO"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "GameBalance", menuName = "Car Monster/Game Balance", order = 1)]
    public class GameBalanceSO : ScriptableObject
    {
        [Serializable]
        public class NitroTuning
        {
            [Tooltip("Сколько длится один уровень буста, сек.")]
            public float boostDurationPerBar = 1.8f;
            public float boostAccelMultiplier = 1.8f;
            public float boostSpeedBonusKmh = 55f;

            [Header("Perfect Injection")]
            [Tooltip("Зелёная зона шкалы впрыска (доля от длительности текущего буста).")]
            [Range(0f, 1f)] public float greenZoneStart = 0.62f;
            [Range(0f, 1f)] public float greenZoneEnd = 0.84f;
            public float perfectAccelMultiplier = 2.5f;
            public float perfectSpeedBonusKmh = 85f;
            [Tooltip("Мгновенный рывок при идеальном впрыске, м/с.")]
            public float perfectImpulse = 7f;
            [Tooltip("Бонус к множителю за каждый идеальный впрыск подряд (макс. 3 в цепочке).")]
            public float perfectChainBonus = 0.15f;

            [Header("Charge sources (единицы заряда)")]
            public float chargePerDriftSecond = 22f;
            public float chargePerAirSecond = 30f;
            [Tooltip("Минимальное время в воздухе, чтобы засчитать прыжок.")]
            public float minAirtime = 0.45f;
            public float nearMissCharge = 18f;
            public float nearMissRadius = 4.2f;
            public float nearMissMinRelativeSpeedKmh = 50f;
            public float nearMissCooldown = 1.2f;
            [Tooltip("Слои, где искать соперников для пролётов впритирку.")]
            public LayerMask racerMask = ~0;
        }

        [Serializable]
        public class CombatTuning
        {
            [Tooltip("Минимальная скорость сближения для регистрации удара, км/ч.")]
            public float minImpactSpeedKmh = 18f;
            [Tooltip("Скорость сближения, при которой удар считается «боевым» даже без нитро, км/ч.")]
            public float highSpeedImpactKmh = 85f;
            [Tooltip("Урон за 1 км/ч скорости сближения.")]
            public float damagePerKmh = 0.9f;
            [Tooltip("Влияние соотношения масс (0 = игнорировать массу).")]
            [Range(0f, 1.5f)] public float massInfluence = 0.7f;
            [Tooltip("Множитель урона, если удар «слабый» (ни нитро, ни высокой скорости).")]
            [Range(0f, 1f)] public float weakHitMultiplier = 0.12f;
            public float nitroDamageMultiplier = 1.8f;
            public float perfectNitroDamageMultiplier = 2.3f;
            [Tooltip("Бонус за удар в борт (T-bone).")]
            public float sideHitMultiplier = 1.5f;
            [Tooltip("Минимальная «фронтальность» атакующего (dot forward→цель), чтобы удар считался его атакой.")]
            [Range(-1f, 1f)] public float minAttackFrontness = 0.2f;
            [Tooltip("При ударе сильнее этого значения отрывается одна деталь.")]
            public float partDetachDamage = 25f;
            public float partEjectSpeed = 9f;
            public float knockoutLaunchSpeed = 8f;

            [Header("Takedown reward & feel")]
            public float takedownTimeBonus = 5f;
            public float takedownNitroReward = 100f;
            public float hitstopFreezeDuration = 0.06f;
            [Tooltip("Длительность слоу-мо (реального времени).")]
            public float slowMoDuration = 0.8f;
            [Range(0.02f, 1f)] public float slowMoTimeScale = 0.2f;
            public float respawnInvulnerability = 2f;

            [Header("Wall crash (самоуничтожение об стены)")]
            [Tooltip("Удар в стену/отбойник с нормальной составляющей скорости выше этой — урон себе, км/ч.")]
            public float wallCrashMinKmh = 100f;
            [Tooltip("Урон себе за 1 км/ч нормальной скорости удара.")]
            public float wallDamagePerKmh = 1f;

            [Header("Self-reassembly (самосборка)")]
            [Tooltip("Время полёта корпуса к точке сборки, сек.")]
            public float reassembleMoveTime = 0.6f;
            [Tooltip("Время полёта одной детали обратно, сек.")]
            public float reassemblePartTime = 0.45f;
            [Tooltip("Задержка между стартами деталей, сек.")]
            public float reassembleStagger = 0.07f;
            public float reassembleHoverHeight = 0.6f;
            [Tooltip("С какой скоростью собранная машина продолжает гонку, км/ч.")]
            public float reassembleLaunchKmh = 90f;
            public Color charredColor = new Color(0.07f, 0.06f, 0.06f);
        }

        [Serializable]
        public class TimeAttackTuning
        {
            public float startTime = 45f;
            public float checkpointTimeBonus = 7f;
            public int laps = 3;
            public float countdownDuration = 3f;
            public float respawnDelay = 3f;
            [Tooltip("Насколько вперёд по трассе от места аварии собирается машина, м.")]
            public float respawnAheadOffset = 6f;
            public float respawnClearRadius = 2.8f;
            [Tooltip("Ниже этой высоты машина считается упавшей с трассы.")]
            public float killY = -30f;
            [Tooltip("Дальше от края дороги — «вылет с трассы», м.")]
            public float offTrackDistance = 12f;
            [Tooltip("Сколько можно быть вне трассы до авто-возврата, сек.")]
            public float offTrackTime = 2.5f;
        }

        [Serializable]
        public class AITuning
        {
            public float lookaheadBase = 8f;
            public float lookaheadPerMs = 0.35f;
            [Tooltip("Угол к цели, при котором руль в упоре, град.")]
            public float steerAngleFull = 28f;
            public float cornerSpeedKmh = 140f;
            [Tooltip("Угол поворота трассы впереди, при котором ИИ сбрасывает до cornerSpeed, град.")]
            public float hardCornerAngle = 70f;
            public float driftCornerAngle = 40f;
            public float straightAngle = 8f;
            public float avoidRayLength = 10f;
            public float avoidRayPerMs = 0.8f;
            public float avoidStrength = 1.1f;
            public LayerMask obstacleMask = ~0;
            public float attackRange = 35f;
            public float nitroCooldown = 2.5f;
            public float stuckTime = 2f;
            public float reverseTime = 1.2f;
        }

        [Serializable]
        public class CameraTuning
        {
            public float baseFov = 60f;
            public float maxSpeedFov = 78f;
            public float nitroFovBonus = 8f;
            public float perfectFovBonus = 6f;
            public float fovLerpSpeed = 5f;
            public float shakePerDamage = 0.03f;
            public float maxShake = 1.6f;
            public float landingShakePerSecondAir = 0.35f;
            public float takedownShake = 1.2f;
            [Tooltip("Сколько держать фокус на разбитой машине после слоу-мо, сек реального времени.")]
            public float takedownFocusHold = 0.35f;
        }

        [Serializable]
        public class AudioTuning
        {
            [Range(0f, 1f)] public float musicVolume = 0.55f;
            [Range(0f, 1f)] public float engineVolume = 0.6f;
            [Range(0f, 1f)] public float botEngineVolume = 0.45f;
            [Tooltip("Питч музыки во время слоу-мо.")]
            [Range(0.3f, 1f)] public float slowMoMusicPitch = 0.72f;
            public float slowMoMusicCutoff = 900f;
            public float musicFadeSpeed = 1.5f;
        }

        [Serializable]
        public class EconomyTuning
        {
            public int startingCredits = 20000;
            public int creditsPerCheckpoint = 500;
            public int creditsPerTakedown = 3000;
            public int finishBonus = 8000;
            public int creditsPerSecondLeft = 100;
            public int consolationCredits = 1000;
            [Tooltip("Кнопка «+бабки» в гараже (отключи перед релизом).")]
            public bool showMoneyCheat = true;
            public int moneyCheatAmount = 1000000;
        }

        public NitroTuning nitro = new NitroTuning();
        public AudioTuning audio = new AudioTuning();
        public EconomyTuning economy = new EconomyTuning();
        public CombatTuning combat = new CombatTuning();
        public TimeAttackTuning timeAttack = new TimeAttackTuning();
        public AITuning ai = new AITuning();
        public CameraTuning cameraFx = new CameraTuning();
    }
}
