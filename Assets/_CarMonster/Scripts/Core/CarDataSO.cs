using UnityEngine;

namespace CarMonster
{
    public enum CarClass
    {
        Drift,
        Track,
        Heavy,
        Muscle,
        Hyper
    }

    /// <summary>
    /// Все характеристики шасси. Готовый ростер генерится меню Car Monster → Create Car Roster.
    /// Имя/марку можно менять прямо в инспекторе — код ни к каким названиям не привязан.
    /// </summary>
    [CreateAssetMenu(fileName = "Car_New", menuName = "Car Monster/Car Data", order = 0)]
    public class CarDataSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Имя, которое видит игрок.")]
        public string displayName = "New Chassis";
        [Tooltip("Короткий код шасси для HUD.")]
        public string chassisCode = "NEW-01";
        [Tooltip("На какую реальную машину ориентировались при балансе (только для дизайнеров).")]
        public string inspiredBy = "";
        public CarClass carClass = CarClass.Track;
        [TextArea(2, 4)] public string description = "";
        public Color bodyColor = new Color(0.85f, 0.1f, 0.1f);
        [Range(1, 5)] public int statSpeed = 3;
        [Range(1, 5)] public int statAcceleration = 3;
        [Range(1, 5)] public int statHandling = 3;
        [Range(1, 5)] public int statToughness = 3;

        [Header("Engine")]
        [Tooltip("Максимальная скорость без нитро, км/ч.")]
        public float maxSpeedKmh = 330f;
        [Tooltip("Базовое ускорение на старте, м/с².")]
        public float acceleration = 24f;
        [Tooltip("Множитель ускорения по нормализованной скорости (0 = стоит, 1 = макс.).")]
        public AnimationCurve accelerationCurve = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.6f, 0.7f), new Keyframe(1f, 0.2f));
        [Tooltip("Доп. тяга на низких скоростях (электромоторы = много). 0..2.")]
        [Range(0f, 2f)] public float launchBoost = 0.3f;
        [Tooltip("Замедление при торможении, м/с².")]
        public float brakeDeceleration = 38f;
        public float reverseMaxSpeedKmh = 60f;
        [Tooltip("Сопротивление накату без газа (1/с).")]
        public float coastDrag = 0.25f;

        [Header("Steering")]
        [Tooltip("Скорость поворота на малой скорости, град/с.")]
        public float steerRateLowSpeed = 120f;
        [Tooltip("Скорость поворота на максималке, град/с. Отвечает за отзывчивость на 350+ км/ч.")]
        public float steerRateHighSpeed = 60f;
        [Tooltip("Насколько быстро угловая скорость доходит до цели, рад/с².")]
        public float steerResponse = 14f;
        [Tooltip("Сцепление шин: как быстро гасится боковое скольжение (1/с).")]
        public float grip = 10f;
        [Tooltip("Прижимная сила на максималке, м/с².")]
        public float downforce = 12f;

        [Header("Drift")]
        [Tooltip("Мощность дрифта: базовая скорость вращения в заносе, град/с.")]
        public float driftPower = 85f;
        [Tooltip("Сколько руль может сузить/расширить дугу дрифта (0..1).")]
        [Range(0f, 1f)] public float driftSteerRange = 0.55f;
        [Tooltip("Сцепление в заносе (меньше = больше угол).")]
        public float driftGrip = 2.2f;
        [Tooltip("Насколько дрифт сохраняет скорость (0..1).")]
        [Range(0f, 1f)] public float driftSpeedRetention = 0.85f;
        public float driftMinSpeedKmh = 55f;

        [Header("Suspension")]
        public float suspensionLength = 0.5f;
        public float wheelRadius = 0.35f;
        [Tooltip("Жёсткость пружины (м/с² на единицу сжатия, нормировано на массу).")]
        public float springStrength = 38f;
        public float springDamper = 2.6f;

        [Header("Air")]
        [Tooltip("Доп. гравитация в воздухе, чтобы прыжки не были «лунными».")]
        public float extraAirGravity = 12f;
        [Tooltip("Сила выравнивания корпуса в воздухе.")]
        public float airStabilization = 14f;
        public float airAngularDamping = 4f;
        [Tooltip("Управление рысканием в воздухе, рад/с².")]
        public float airYawControl = 3f;
        [Tooltip("Скорость доворота к нормали дороги после приземления.")]
        public float landingAlignSpeed = 12f;
        public float landingAlignTime = 0.35f;

        [Header("Combat")]
        [Tooltip("Масса, кг. Тяжелее = сильнее таранит и меньше страдает.")]
        public float mass = 1700f;
        [Tooltip("Множитель урона при таране.")]
        public float ramPower = 1f;
        [Tooltip("Прочность (HP).")]
        public float durability = 100f;
        [Tooltip("Снижение входящего урона, 0..0.8.")]
        [Range(0f, 0.8f)] public float armor = 0.1f;

        [Header("Nitro")]
        [Tooltip("Ёмкость нитро на ОДИН уровень шкалы (всего уровней 3).")]
        public float nitroCapacityPerBar = 100f;
        [Tooltip("Множитель набора нитро (дрифт/воздух/впритирку/тейкдауны).")]
        public float nitroChargeMultiplier = 1f;
        [Tooltip("Множитель силы ускорения нитро для этого шасси.")]
        public float nitroForceMultiplier = 1f;

        public float MaxSpeedMs => maxSpeedKmh / 3.6f;
    }
}
