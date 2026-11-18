using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarMonster.EditorTools
{
    public enum BodyStyle
    {
        Coupe,
        Hyper,
        Sedan,
        Truck,
        Suv,
        Suv6x6
    }

    /// <summary>
    /// Генерирует весь контент: CarDataSO + префабы машин, локации, GameBalance и GameCatalog.
    /// Названия — кодовые имена шасси (реальные марки без лицензии в релиз тащить нельзя),
    /// прототип указан в поле inspiredBy. displayName можно переименовать в инспекторе.
    /// </summary>
    public static class CarRoster
    {
        public const string Root = "Assets/_CarMonster";
        public const string CarsFolder = Root + "/Data/Cars";
        public const string LocationsFolder = Root + "/Data/Locations";
        public const string PrefabsFolder = Root + "/Prefabs/Cars";
        public const string BalancePath = Root + "/Data/GameBalance.asset";
        public const string CatalogPath = Root + "/Data/GameCatalog.asset";

        public class Preset
        {
            public string code, name, inspiredBy, description;
            public CarClass carClass = CarClass.Track;
            public CarTier tier = CarTier.Race;
            public int price;
            public BodyStyle style = BodyStyle.Coupe;
            public Color color = Color.red;
            public Color accent = new Color(0.08f, 0.08f, 0.09f);
            public int spd = 3, acc = 3, hnd = 3, tgh = 3;
            public float maxKmh = 330f, accel = 25f, launch = 0.3f, steerLow = 120f, steerHigh = 62f, grip = 11f;
            public float driftPower = 85f, driftGrip = 2.2f, driftRetention = 0.86f;
            public float mass = 1600f, ram = 1f, durability = 100f, armor = 0.1f;
            public float nitroCap = 100f, nitroCharge = 1f, nitroForce = 1f;
            public float wheelRadius = 0.35f, suspension = 0.5f;
            public bool plow;
            public float plowDamage = 1.6f, plowArmor = 0.6f, plowShove = 7f;
            public EngineSoundProfile engine = new EngineSoundProfile();
        }

        private static EngineSoundProfile Eng(EngineType type, float redline, int gears, float bright, float rough,
            float turbo, float backfire = 0.5f, float pitch = 1f, float idle = 900f) =>
            new EngineSoundProfile
            {
                type = type, redlineRpm = redline, gears = gears, brightness = bright, roughness = rough,
                turbo = turbo, backfire = backfire, pitch = pitch, idleRpm = idle, volume = 1f
            };

        public static readonly Preset[] Presets =
        {
            // ---------------- Базовый ростер ----------------
            new Preset
            {
                code = "R35-GT", name = "R35-GT", inspiredBy = "Nissan GT-R (R35)", carClass = CarClass.Drift, tier = CarTier.Street, price = 0,
                description = "Полный привод и злой дрифт. Нитро копит быстрее всех — живёт в заносах.",
                color = new Color(0.32f, 0.36f, 0.42f), spd = 3, acc = 4, hnd = 4, tgh = 3,
                maxKmh = 325f, accel = 25f, launch = 0.45f, steerLow = 125f, steerHigh = 62f, grip = 11f,
                driftPower = 95f, driftGrip = 1.9f, driftRetention = 0.9f, mass = 1750f, ram = 1f, durability = 110f, armor = 0.15f,
                nitroCap = 90f, nitroCharge = 1.35f, engine = Eng(EngineType.V6, 7100f, 6, 0.5f, 0.2f, 0.8f, 0.6f)
            },
            new Preset
            {
                code = "SF90-V8", name = "SF90-V8", inspiredBy = "Ferrari SF90 Stradale", carClass = CarClass.Track, tier = CarTier.Hyper, price = 85000,
                style = BodyStyle.Hyper, description = "Среднемоторный гиперкар: лучшая максималка и управляемость на трассе. Хрупкий.",
                color = new Color(0.85f, 0.05f, 0.05f), spd = 5, acc = 4, hnd = 5, tgh = 2,
                maxKmh = 350f, accel = 26f, launch = 0.35f, steerLow = 120f, steerHigh = 72f, grip = 13f,
                driftPower = 80f, driftGrip = 2.6f, driftRetention = 0.85f, mass = 1570f, ram = 0.9f, durability = 90f, armor = 0.05f,
                nitroForce = 1.1f, engine = Eng(EngineType.V8, 8000f, 8, 0.7f, 0.25f, 0.5f)
            },
            new Preset
            {
                code = "PLAID-EV", name = "Plaid-EV", inspiredBy = "Tesla Model S Plaid", carClass = CarClass.Heavy, tier = CarTier.Race, price = 30000,
                style = BodyStyle.Sedan, description = "Тяжёлый электро-седан: мгновенный старт и самый страшный таран в классе.",
                color = new Color(0.92f, 0.92f, 0.95f), spd = 3, acc = 5, hnd = 3, tgh = 5,
                maxKmh = 322f, accel = 30f, launch = 1.6f, steerLow = 110f, steerHigh = 55f, grip = 10f,
                driftPower = 70f, driftGrip = 2.8f, driftRetention = 0.8f, mass = 2160f, ram = 1.5f, durability = 140f, armor = 0.25f,
                nitroCap = 110f, nitroCharge = 0.9f, engine = Eng(EngineType.Electric, 18000f, 1, 0.5f, 0f, 0f, 0f)
            },
            new Preset
            {
                code = "HRC-V10", name = "Toro HRC-V10", inspiredBy = "Lamborghini Huracán", carClass = CarClass.Drift, tier = CarTier.Race, price = 45000,
                style = BodyStyle.Hyper, description = "Лёгкий атмосферный V10, крутит дрифт как волчок. Не любит тараны.",
                color = new Color(0.4f, 0.85f, 0.1f), spd = 3, acc = 4, hnd = 5, tgh = 2,
                maxKmh = 325f, accel = 25f, launch = 0.3f, steerLow = 130f, steerHigh = 64f, grip = 11f,
                driftPower = 100f, driftGrip = 1.8f, driftRetention = 0.88f, mass = 1420f, ram = 0.85f, durability = 85f, armor = 0.05f,
                nitroCap = 95f, nitroCharge = 1.25f, engine = Eng(EngineType.V10, 8700f, 7, 0.85f, 0.15f, 0f, 0.8f)
            },
            new Preset
            {
                code = "RVT-V12", name = "Toro RVT-V12", inspiredBy = "Lamborghini Revuelto", carClass = CarClass.Hyper, tier = CarTier.Hyper, price = 95000,
                style = BodyStyle.Hyper, description = "Гибридный V12: тяга на всех скоростях и сильный буст. Универсал для прямых.",
                color = new Color(1f, 0.78f, 0f), spd = 5, acc = 5, hnd = 3, tgh = 3,
                maxKmh = 350f, accel = 27f, launch = 0.7f, steerLow = 115f, steerHigh = 66f, grip = 12f,
                driftPower = 85f, driftGrip = 2.3f, driftRetention = 0.86f, mass = 1770f, ram = 1.1f, durability = 105f, armor = 0.12f,
                nitroForce = 1.15f, engine = Eng(EngineType.V12, 9500f, 8, 0.9f, 0.1f, 0f, 0.6f)
            },
            new Preset
            {
                code = "Z34-RZ", name = "Ronin Z34", inspiredBy = "Nissan Z", carClass = CarClass.Drift, tier = CarTier.Street, price = 0,
                description = "Дешёвый и злой: медленный, но король заносов и набора нитро.",
                color = new Color(0.1f, 0.4f, 1f), spd = 2, acc = 3, hnd = 5, tgh = 2,
                maxKmh = 275f, accel = 21f, launch = 0.25f, steerLow = 140f, steerHigh = 70f, grip = 10f,
                driftPower = 105f, driftGrip = 1.6f, driftRetention = 0.92f, mass = 1570f, ram = 0.8f, durability = 80f, armor = 0.05f,
                nitroCap = 80f, nitroCharge = 1.6f, nitroForce = 0.95f, engine = Eng(EngineType.V6, 7000f, 6, 0.45f, 0.25f, 0.6f, 0.7f)
            },
            new Preset
            {
                code = "812-SF", name = "Stallion 812", inspiredBy = "Ferrari 812 Superfast", carClass = CarClass.Muscle, tier = CarTier.Race, price = 60000,
                description = "Переднемоторный V12 GT: длинные дуги, стабильный на скорости, крепкий.",
                color = new Color(0.55f, 0.02f, 0.06f), spd = 4, acc = 3, hnd = 4, tgh = 3,
                maxKmh = 340f, accel = 24f, launch = 0.2f, steerLow = 112f, steerHigh = 64f, grip = 11.5f,
                driftPower = 90f, driftGrip = 2.1f, driftRetention = 0.87f, mass = 1630f, ram = 1.05f, durability = 100f, armor = 0.1f,
                nitroCharge = 1.1f, nitroForce = 1.05f, engine = Eng(EngineType.V12, 8900f, 7, 0.85f, 0.1f, 0f, 0.5f)
            },
            new Preset
            {
                code = "CYB-TRK", name = "Cyber Brick", inspiredBy = "Tesla Cybertruck", carClass = CarClass.Heavy, tier = CarTier.Street, price = 0,
                style = BodyStyle.Truck, description = "Стальной утюг. Медленный, но сносит всё, во что въезжает.",
                color = new Color(0.7f, 0.72f, 0.74f), spd = 1, acc = 4, hnd = 1, tgh = 5,
                maxKmh = 215f, accel = 22f, launch = 1.4f, steerLow = 100f, steerHigh = 50f, grip = 9f,
                driftPower = 60f, driftGrip = 3f, driftRetention = 0.75f, mass = 3100f, ram = 2f, durability = 220f, armor = 0.45f,
                nitroCap = 120f, nitroCharge = 0.8f, nitroForce = 0.9f, wheelRadius = 0.42f, suspension = 0.55f,
                engine = Eng(EngineType.Electric, 15000f, 1, 0.3f, 0f, 0f, 0f, 0.75f)
            },
            new Preset
            {
                code = "RDS-2E", name = "Roadster 2E", inspiredBy = "Tesla Roadster (2nd gen)", carClass = CarClass.Hyper, tier = CarTier.Hyper, price = 80000,
                style = BodyStyle.Hyper, description = "Электро-ракета: самый быстрый разгон в игре, но в поворотах надо думать.",
                color = new Color(0.8f, 0.1f, 0.5f), spd = 5, acc = 5, hnd = 3, tgh = 3,
                maxKmh = 345f, accel = 32f, launch = 1.8f, steerLow = 118f, steerHigh = 62f, grip = 12f,
                driftPower = 78f, driftGrip = 2.5f, driftRetention = 0.84f, mass = 1900f, ram = 1.2f, durability = 95f, armor = 0.1f,
                nitroForce = 1.1f, engine = Eng(EngineType.Electric, 20000f, 1, 0.7f, 0f, 0f, 0f, 1.2f)
            },
            new Preset
            {
                code = "GT2-RS", name = "Neun GT2", inspiredBy = "Porsche 911 GT2 RS", carClass = CarClass.Track, tier = CarTier.Race, price = 55000,
                description = "Заднемоторный снайпер: точный руль и стабильность на скорости.",
                color = new Color(0.15f, 0.75f, 0.6f), spd = 4, acc = 4, hnd = 5, tgh = 2,
                maxKmh = 340f, accel = 25f, launch = 0.25f, steerLow = 128f, steerHigh = 72f, grip = 13.5f,
                driftPower = 82f, driftGrip = 2.4f, driftRetention = 0.85f, mass = 1470f, ram = 0.9f, durability = 90f, armor = 0.08f,
                nitroCap = 95f, nitroCharge = 1.1f, engine = Eng(EngineType.FlatSix, 7200f, 7, 0.6f, 0.4f, 0.9f, 0.6f)
            },
            new Preset
            {
                code = "CHR-W16", name = "Molsheim W16", inspiredBy = "Bugatti Chiron", carClass = CarClass.Hyper, tier = CarTier.Hyper, price = 150000,
                style = BodyStyle.Hyper, description = "Король максималки (380). Тяжёлый, в повороты заходит как танкер.",
                color = new Color(0.05f, 0.15f, 0.55f), spd = 5, acc = 4, hnd = 2, tgh = 4,
                maxKmh = 380f, accel = 26f, launch = 0.5f, steerLow = 100f, steerHigh = 50f, grip = 11f,
                driftPower = 70f, driftGrip = 2.7f, driftRetention = 0.85f, mass = 1995f, ram = 1.3f, durability = 125f, armor = 0.2f,
                nitroCap = 110f, nitroCharge = 0.9f, nitroForce = 1.2f, engine = Eng(EngineType.W16, 6800f, 7, 0.55f, 0.05f, 1f, 0.3f)
            },
            new Preset
            {
                code = "SNA-LM", name = "Senna LM", inspiredBy = "McLaren Senna", carClass = CarClass.Track, tier = CarTier.Hyper, price = 90000,
                style = BodyStyle.Hyper, description = "Максимум прижима и сцепления. Самая лёгкая и самая хрупкая.",
                color = new Color(1f, 0.5f, 0f), spd = 4, acc = 4, hnd = 5, tgh = 1,
                maxKmh = 335f, accel = 26f, launch = 0.3f, steerLow = 132f, steerHigh = 78f, grip = 15f,
                driftPower = 75f, driftGrip = 2.8f, driftRetention = 0.82f, mass = 1200f, ram = 0.75f, durability = 75f, armor = 0f,
                nitroCap = 90f, nitroCharge = 1.2f, nitroForce = 1.05f, engine = Eng(EngineType.V8, 8250f, 7, 0.75f, 0.2f, 0.7f, 0.7f)
            },

            // ---------------- Новые гоночные ----------------
            new Preset
            {
                code = "JSK-AB", name = "Valkyr Jesko", inspiredBy = "Koenigsegg Jesko Absolut", carClass = CarClass.Hyper, tier = CarTier.Hyper, price = 140000,
                style = BodyStyle.Hyper, description = "Шведская ракета: 370 км/ч и 9-ступенчатая коробка, которая переключается мгновенно.",
                color = new Color(0.95f, 0.95f, 0.98f), accent = new Color(0.9f, 0.1f, 0.1f), spd = 5, acc = 5, hnd = 3, tgh = 2,
                maxKmh = 370f, accel = 28f, launch = 0.4f, steerLow = 115f, steerHigh = 62f, grip = 12.5f,
                driftPower = 80f, driftGrip = 2.5f, mass = 1390f, ram = 0.9f, durability = 85f, armor = 0.05f,
                nitroForce = 1.15f, engine = Eng(EngineType.V8, 8500f, 9, 0.7f, 0.2f, 1f, 0.6f)
            },
            new Preset
            {
                code = "HYR-R", name = "Vento HR", inspiredBy = "Pagani Huayra R", carClass = CarClass.Track, tier = CarTier.Hyper, price = 120000,
                style = BodyStyle.Hyper, description = "Трековый V12 весом с перышко: лучший руль в игре, но любой таран для него — боль.",
                color = new Color(0.12f, 0.12f, 0.14f), accent = new Color(0.85f, 0.65f, 0.2f), spd = 4, acc = 4, hnd = 5, tgh = 1,
                maxKmh = 355f, accel = 27f, launch = 0.3f, steerLow = 135f, steerHigh = 80f, grip = 14f,
                driftPower = 85f, driftGrip = 2.3f, mass = 1050f, ram = 0.7f, durability = 70f, armor = 0f,
                nitroCharge = 1.2f, engine = Eng(EngineType.V12, 9000f, 6, 0.95f, 0.1f, 0f, 0.7f)
            },
            new Preset
            {
                code = "VLK-AM", name = "Aero Valk", inspiredBy = "Aston Martin Valkyrie", carClass = CarClass.Track, tier = CarTier.Hyper, price = 130000,
                style = BodyStyle.Hyper, description = "Болид для дорог: V12 визжит до 11 000 оборотов, прижим как у Формулы.",
                color = new Color(0.1f, 0.35f, 0.25f), accent = new Color(0.75f, 0.85f, 0.2f), spd = 5, acc = 4, hnd = 5, tgh = 1,
                maxKmh = 350f, accel = 28f, launch = 0.35f, steerLow = 130f, steerHigh = 78f, grip = 15f,
                driftPower = 75f, driftGrip = 2.9f, driftRetention = 0.82f, mass = 1270f, ram = 0.8f, durability = 70f, armor = 0f,
                engine = Eng(EngineType.V12, 11100f, 7, 1f, 0.05f, 0f, 0.5f, 1.05f)
            },
            new Preset
            {
                code = "NVR-EV", name = "Volt Nevera", inspiredBy = "Rimac Nevera", carClass = CarClass.Hyper, tier = CarTier.Hyper, price = 125000,
                style = BodyStyle.Hyper, description = "Четыре электромотора: старт как из пушки и тяжёлый таран.",
                color = new Color(0.2f, 0.25f, 0.35f), accent = new Color(0.1f, 0.8f, 1f), spd = 5, acc = 5, hnd = 3, tgh = 4,
                maxKmh = 355f, accel = 34f, launch = 2f, steerLow = 115f, steerHigh = 60f, grip = 12.5f,
                driftPower = 75f, driftGrip = 2.6f, mass = 2300f, ram = 1.4f, durability = 115f, armor = 0.15f,
                nitroCharge = 0.95f, engine = Eng(EngineType.Electric, 21000f, 1, 0.8f, 0f, 0f, 0f, 1.1f)
            },
            new Preset
            {
                code = "AMG-1", name = "Silberpfeil ONE", inspiredBy = "Mercedes-AMG ONE", carClass = CarClass.Track, tier = CarTier.Hyper, price = 135000,
                style = BodyStyle.Hyper, description = "Двигатель от болида Ф1: истошный визг и сумасшедшее сцепление.",
                color = new Color(0.72f, 0.74f, 0.78f), accent = new Color(0.1f, 0.85f, 0.75f), spd = 5, acc = 4, hnd = 5, tgh = 2,
                maxKmh = 352f, accel = 27f, launch = 0.6f, steerLow = 125f, steerHigh = 76f, grip = 14.5f,
                driftPower = 78f, driftGrip = 2.7f, mass = 1695f, ram = 1f, durability = 90f, armor = 0.08f,
                nitroForce = 1.1f, engine = Eng(EngineType.V6, 11000f, 7, 0.95f, 0.1f, 1f, 0.4f, 1.1f, 1200f)
            },
            new Preset
            {
                code = "FGT-40", name = "Le Mans GT", inspiredBy = "Ford GT", carClass = CarClass.Track, tier = CarTier.Race, price = 50000,
                description = "Наследник гонщика из Ле-Мана: битурбо V6, ровный и цепкий.",
                color = new Color(0.1f, 0.3f, 0.75f), accent = Color.white, spd = 4, acc = 4, hnd = 4, tgh = 3,
                maxKmh = 348f, accel = 25f, launch = 0.3f, steerLow = 122f, steerHigh = 70f, grip = 13f,
                driftPower = 82f, driftGrip = 2.4f, mass = 1385f, ram = 0.95f, durability = 95f, armor = 0.08f,
                engine = Eng(EngineType.V6, 7000f, 7, 0.55f, 0.3f, 1f, 0.7f)
            },
            new Preset
            {
                code = "C8-Z06", name = "Stingray Z", inspiredBy = "Chevrolet Corvette C8 Z06", carClass = CarClass.Muscle, tier = CarTier.Race, price = 40000,
                description = "Американский V8 с плоским валом: громкий, злой и недорогой.",
                color = new Color(1f, 0.85f, 0.1f), spd = 3, acc = 4, hnd = 4, tgh = 3,
                maxKmh = 315f, accel = 25f, launch = 0.35f, steerLow = 122f, steerHigh = 64f, grip = 12f,
                driftPower = 92f, driftGrip = 2f, mass = 1560f, ram = 1.05f, durability = 100f, armor = 0.1f,
                nitroCharge = 1.15f, engine = Eng(EngineType.V8, 8600f, 8, 0.75f, 0.35f, 0f, 0.9f)
            },
            new Preset
            {
                code = "R34-GT", name = "Skyline R34", inspiredBy = "Nissan Skyline GT-R (R34)", carClass = CarClass.Drift, tier = CarTier.Street, price = 15000,
                description = "Легенда дрифта: рядная шестёрка с турбиной, самый длинный занос в игре.",
                color = new Color(0.15f, 0.3f, 0.85f), spd = 2, acc = 3, hnd = 5, tgh = 3,
                maxKmh = 290f, accel = 22f, launch = 0.3f, steerLow = 135f, steerHigh = 68f, grip = 10f,
                driftPower = 110f, driftGrip = 1.5f, driftRetention = 0.93f, mass = 1560f, ram = 0.95f, durability = 95f, armor = 0.1f,
                nitroCap = 85f, nitroCharge = 1.5f, engine = Eng(EngineType.Inline6, 8000f, 6, 0.55f, 0.15f, 1f, 0.8f)
            },
            new Preset
            {
                code = "SVJ-V12", name = "Toro SVJ", inspiredBy = "Lamborghini Aventador SVJ", carClass = CarClass.Hyper, tier = CarTier.Hyper, price = 110000,
                style = BodyStyle.Hyper, description = "Атмосферный V12 и активная аэродинамика: красиво ходит боком на 300.",
                color = new Color(0.55f, 0.15f, 0.75f), spd = 5, acc = 4, hnd = 4, tgh = 3,
                maxKmh = 350f, accel = 27f, launch = 0.4f, steerLow = 120f, steerHigh = 66f, grip = 12f,
                driftPower = 95f, driftGrip = 2f, driftRetention = 0.88f, mass = 1525f, ram = 1f, durability = 100f, armor = 0.1f,
                nitroCharge = 1.1f, engine = Eng(EngineType.V12, 8700f, 7, 0.9f, 0.15f, 0f, 0.8f)
            },
            new Preset
            {
                code = "LFR-HY", name = "Cavallo LF", inspiredBy = "LaFerrari", carClass = CarClass.Hyper, tier = CarTier.Hyper, price = 115000,
                style = BodyStyle.Hyper, description = "Гибрид V12: электрический подхват снизу и вой атмосферника сверху.",
                color = new Color(0.9f, 0.08f, 0.08f), accent = new Color(0.08f, 0.08f, 0.08f), spd = 5, acc = 5, hnd = 4, tgh = 2,
                maxKmh = 350f, accel = 28f, launch = 0.6f, steerLow = 122f, steerHigh = 70f, grip = 13f,
                driftPower = 85f, driftGrip = 2.3f, mass = 1585f, ram = 1f, durability = 95f, armor = 0.08f,
                nitroForce = 1.1f, engine = Eng(EngineType.V12, 9250f, 7, 0.9f, 0.1f, 0f, 0.6f)
            },

            // ---------------- Бронированные спец-машины с ковшом ----------------
            new Preset
            {
                code = "BLW-CUL", name = "Sovereign Bulwark", inspiredBy = "Rolls-Royce Cullinan (бронированный)", carClass = CarClass.Armored,
                tier = CarTier.ArmoredSpecial, price = 250000, style = BodyStyle.Suv,
                description = "Бронированный люкс-внедорожник с золочёным ковшом. Тихий V12 и абсолютная уверенность в себе.",
                color = new Color(0.12f, 0.08f, 0.18f), accent = new Color(0.85f, 0.68f, 0.25f), spd = 2, acc = 2, hnd = 2, tgh = 5,
                maxKmh = 260f, accel = 20f, launch = 0.4f, steerLow = 95f, steerHigh = 46f, grip = 9.5f,
                driftPower = 60f, driftGrip = 3f, driftRetention = 0.78f, mass = 3300f, ram = 1.8f, durability = 260f, armor = 0.5f,
                nitroCap = 120f, nitroCharge = 0.85f, nitroForce = 0.95f, wheelRadius = 0.45f, suspension = 0.55f,
                plow = true, plowDamage = 1.7f, plowArmor = 0.6f, plowShove = 7f,
                engine = Eng(EngineType.V12, 6000f, 8, 0.4f, 0.05f, 0.3f, 0.1f, 0.85f, 650f)
            },
            new Preset
            {
                code = "G66-ARM", name = "Mammoth 6x6", inspiredBy = "Mercedes-AMG G63 6x6", carClass = CarClass.Armored,
                tier = CarTier.ArmoredSpecial, price = 220000, style = BodyStyle.Suv6x6,
                description = "Шесть колёс, четыре тонны и ковш на лбу. Едет туда, где была чужая машина.",
                color = new Color(0.62f, 0.53f, 0.37f), accent = new Color(0.1f, 0.1f, 0.1f), spd = 1, acc = 2, hnd = 2, tgh = 5,
                maxKmh = 230f, accel = 19f, launch = 0.5f, steerLow = 92f, steerHigh = 44f, grip = 10f,
                driftPower = 58f, driftGrip = 3.1f, driftRetention = 0.76f, mass = 4000f, ram = 2.2f, durability = 300f, armor = 0.55f,
                nitroCap = 120f, nitroCharge = 0.8f, nitroForce = 0.95f, wheelRadius = 0.48f, suspension = 0.6f,
                plow = true, plowDamage = 1.8f, plowArmor = 0.65f, plowShove = 9f,
                engine = Eng(EngineType.V8, 6500f, 9, 0.45f, 0.4f, 0.8f, 0.6f, 0.85f, 750f)
            },
            new Preset
            {
                code = "URS-AX", name = "Toro Urus Aegis", inspiredBy = "Lamborghini Urus (бронированный)", carClass = CarClass.Armored,
                tier = CarTier.ArmoredSpecial, price = 180000, style = BodyStyle.Suv,
                description = "Самый быстрый из бронированных: суперкар-SUV с ковшом и битурбо V8.",
                color = new Color(1f, 0.42f, 0f), accent = new Color(0.1f, 0.1f, 0.1f), spd = 3, acc = 3, hnd = 3, tgh = 4,
                maxKmh = 290f, accel = 23f, launch = 0.5f, steerLow = 105f, steerHigh = 52f, grip = 10.5f,
                driftPower = 70f, driftGrip = 2.6f, driftRetention = 0.8f, mass = 2900f, ram = 1.6f, durability = 200f, armor = 0.4f,
                nitroCap = 110f, nitroCharge = 0.9f, wheelRadius = 0.44f, suspension = 0.55f,
                plow = true, plowDamage = 1.5f, plowArmor = 0.5f, plowShove = 6f,
                engine = Eng(EngineType.V8, 6800f, 8, 0.6f, 0.3f, 0.9f, 0.7f, 0.95f)
            },
            new Preset
            {
                code = "KNT-XV", name = "Knight Fortress", inspiredBy = "Conquest Knight XV", carClass = CarClass.Armored,
                tier = CarTier.ArmoredSpecial, price = 300000, style = BodyStyle.Suv,
                description = "Самая дорогая машина в игре: пять тонн брони и ковш, который сносит всё. Медленная, но неубиваемая.",
                color = new Color(0.07f, 0.07f, 0.08f), accent = new Color(0.85f, 0.68f, 0.25f), spd = 1, acc = 1, hnd = 1, tgh = 5,
                maxKmh = 205f, accel = 17f, launch = 0.6f, steerLow = 88f, steerHigh = 42f, grip = 10f,
                driftPower = 55f, driftGrip = 3.2f, driftRetention = 0.75f, mass = 5200f, ram = 2.6f, durability = 380f, armor = 0.65f,
                nitroCap = 130f, nitroCharge = 0.75f, nitroForce = 1f, wheelRadius = 0.5f, suspension = 0.6f,
                plow = true, plowDamage = 2f, plowArmor = 0.75f, plowShove = 12f,
                engine = Eng(EngineType.V10, 5500f, 6, 0.3f, 0.45f, 0f, 0.4f, 0.8f, 700f)
            },
            new Preset
            {
                code = "HMR-EV", name = "Rhino EV", inspiredBy = "GMC Hummer EV (бронированный)", carClass = CarClass.Armored,
                tier = CarTier.ArmoredSpecial, price = 200000, style = BodyStyle.Suv,
                description = "Электро-бронепикап: тихий, тяжёлый и мгновенно разгоняется прямо в борт сопернику.",
                color = new Color(0.86f, 0.86f, 0.82f), accent = new Color(0.15f, 0.15f, 0.15f), spd = 2, acc = 4, hnd = 2, tgh = 5,
                maxKmh = 250f, accel = 26f, launch = 1.8f, steerLow = 95f, steerHigh = 46f, grip = 10f,
                driftPower = 60f, driftGrip = 3f, driftRetention = 0.78f, mass = 4100f, ram = 2.3f, durability = 280f, armor = 0.5f,
                nitroCap = 120f, nitroCharge = 0.85f, wheelRadius = 0.48f, suspension = 0.6f,
                plow = true, plowDamage = 1.7f, plowArmor = 0.6f, plowShove = 10f,
                engine = Eng(EngineType.Electric, 14000f, 1, 0.35f, 0f, 0f, 0f, 0.6f)
            },
        };

        private static readonly Dictionary<string, Preset> ByCode = new Dictionary<string, Preset>();

        public static Preset GetPreset(string code)
        {
            if (ByCode.Count == 0)
                foreach (Preset p in Presets) ByCode[p.code] = p;
            return ByCode.TryGetValue(code, out Preset preset) ? preset : null;
        }

        [MenuItem("Car Monster/Create Car Roster", priority = 1)]
        public static void CreateRosterMenu()
        {
            CreateOrUpdateAll();
            EditorUtility.DisplayDialog("Car Monster", $"Готово: {Presets.Length} машин + префабы, {LocationPresets.All.Length} локации.", "OK");
        }

        /// <summary>Создать/обновить все CarDataSO, префабы, локации, баланс и каталог.</summary>
        public static GameCatalogSO CreateOrUpdateAll()
        {
            EnsureFolder(CarsFolder);
            EnsureFolder(PrefabsFolder);
            GameBalanceSO balance = GetOrCreateBalance();

            var cars = new CarDataSO[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                Preset p = Presets[i];
                string path = $"{CarsFolder}/Car_{p.code}.asset";
                var data = AssetDatabase.LoadAssetAtPath<CarDataSO>(path);
                bool isNew = data == null;
                if (isNew) data = ScriptableObject.CreateInstance<CarDataSO>();
                Apply(data, p);
                if (isNew) AssetDatabase.CreateAsset(data, path);
                cars[i] = data;
            }

            for (int i = 0; i < cars.Length; i++)
            {
                cars[i].prefab = CarFactory.SaveCarPrefab(cars[i], Presets[i], balance, $"{PrefabsFolder}/Car_{Presets[i].code}.prefab");
                EditorUtility.SetDirty(cars[i]);
            }

            LocationSO[] locations = LocationPresets.CreateOrUpdateAll();

            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalogSO>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<GameCatalogSO>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.cars = cars;
            catalog.locations = locations;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        public static GameBalanceSO GetOrCreateBalance()
        {
            EnsureFolder(Root + "/Data");
            var balance = AssetDatabase.LoadAssetAtPath<GameBalanceSO>(BalancePath);
            if (balance != null) return balance;
            balance = ScriptableObject.CreateInstance<GameBalanceSO>();
            AssetDatabase.CreateAsset(balance, BalancePath);
            return balance;
        }

        public static GameCatalogSO LoadCatalog() => AssetDatabase.LoadAssetAtPath<GameCatalogSO>(CatalogPath);

        public static CarDataSO Load(string code) =>
            AssetDatabase.LoadAssetAtPath<CarDataSO>($"{CarsFolder}/Car_{code}.asset");

        private static void Apply(CarDataSO d, Preset p)
        {
            d.displayName = p.name;
            d.chassisCode = p.code;
            d.inspiredBy = p.inspiredBy;
            d.carClass = p.carClass;
            d.description = p.description;
            d.bodyColor = p.color;
            d.statSpeed = p.spd;
            d.statAcceleration = p.acc;
            d.statHandling = p.hnd;
            d.statToughness = p.tgh;
            d.tier = p.tier;
            d.price = p.price;
            d.maxSpeedKmh = p.maxKmh;
            d.acceleration = p.accel;
            d.launchBoost = p.launch;
            d.steerRateLowSpeed = p.steerLow;
            d.steerRateHighSpeed = p.steerHigh;
            d.grip = p.grip;
            d.driftPower = p.driftPower;
            d.driftGrip = p.driftGrip;
            d.driftSpeedRetention = p.driftRetention;
            d.mass = p.mass;
            d.ramPower = p.ram;
            d.durability = p.durability;
            d.armor = p.armor;
            d.nitroCapacityPerBar = p.nitroCap;
            d.nitroChargeMultiplier = p.nitroCharge;
            d.nitroForceMultiplier = p.nitroForce;
            d.wheelRadius = p.wheelRadius;
            d.suspensionLength = p.suspension;
            d.hasRamPlow = p.plow;
            d.plowDamageMultiplier = p.plowDamage;
            d.plowFrontalArmor = p.plowArmor;
            d.plowShoveSpeed = p.plowShove;
            d.engineSound = p.engine;
            EditorUtility.SetDirty(d);
        }

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [MenuItem("Car Monster/Debug/Add 100 000 Credits", priority = 50)]
        private static void AddCredits() => PlayerProfile.AddCredits(100000);

        [MenuItem("Car Monster/Debug/Reset Progress", priority = 51)]
        private static void ResetProgress()
        {
            PlayerProfile.ResetProgress();
            GameCatalogSO catalog = LoadCatalog();
            if (catalog != null) PlayerProfile.ResetOwnership(catalog);
        }
    }
}
