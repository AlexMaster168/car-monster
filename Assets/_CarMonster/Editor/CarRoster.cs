using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarMonster.EditorTools
{
    /// <summary>
    /// Генерирует ассеты CarDataSO для всего ростера + GameBalance.
    /// Названия — кодовые имена шасси (реальные марки без лицензии в релиз тащить нельзя),
    /// прототип указан в поле inspiredBy. displayName можно переименовать в инспекторе.
    /// </summary>
    public static class CarRoster
    {
        public const string Root = "Assets/_CarMonster";
        public const string CarsFolder = Root + "/Data/Cars";
        public const string BalancePath = Root + "/Data/GameBalance.asset";

        private struct Preset
        {
            public string code, name, inspiredBy, description;
            public CarClass carClass;
            public Color color;
            public int spd, acc, hnd, tgh;
            public float maxKmh, accel, launch, steerLow, steerHigh, grip;
            public float driftPower, driftGrip, driftRetention;
            public float mass, ram, durability, armor;
            public float nitroCap, nitroCharge, nitroForce;
        }

        private static readonly Preset[] Presets =
        {
            new Preset
            {
                code = "R35-GT", name = "R35-GT", inspiredBy = "Nissan GT-R (R35)", carClass = CarClass.Drift,
                description = "Полный привод и злой дрифт. Нитро копит быстрее всех — живёт в заносах.",
                color = new Color(0.32f, 0.36f, 0.42f), spd = 3, acc = 4, hnd = 4, tgh = 3,
                maxKmh = 325f, accel = 25f, launch = 0.45f, steerLow = 125f, steerHigh = 62f, grip = 11f,
                driftPower = 95f, driftGrip = 1.9f, driftRetention = 0.9f,
                mass = 1750f, ram = 1f, durability = 110f, armor = 0.15f,
                nitroCap = 90f, nitroCharge = 1.35f, nitroForce = 1f
            },
            new Preset
            {
                code = "SF90-V8", name = "SF90-V8", inspiredBy = "Ferrari SF90 Stradale", carClass = CarClass.Track,
                description = "Среднемоторный гиперкар: лучшая максималка и управляемость на трассе. Хрупкий.",
                color = new Color(0.85f, 0.05f, 0.05f), spd = 5, acc = 4, hnd = 5, tgh = 2,
                maxKmh = 350f, accel = 26f, launch = 0.35f, steerLow = 120f, steerHigh = 72f, grip = 13f,
                driftPower = 80f, driftGrip = 2.6f, driftRetention = 0.85f,
                mass = 1570f, ram = 0.9f, durability = 90f, armor = 0.05f,
                nitroCap = 100f, nitroCharge = 1f, nitroForce = 1.1f
            },
            new Preset
            {
                code = "PLAID-EV", name = "Plaid-EV", inspiredBy = "Tesla Model S Plaid", carClass = CarClass.Heavy,
                description = "Тяжёлый электро-седан: мгновенный старт и самый страшный таран в классе.",
                color = new Color(0.92f, 0.92f, 0.95f), spd = 3, acc = 5, hnd = 3, tgh = 5,
                maxKmh = 322f, accel = 30f, launch = 1.6f, steerLow = 110f, steerHigh = 55f, grip = 10f,
                driftPower = 70f, driftGrip = 2.8f, driftRetention = 0.8f,
                mass = 2160f, ram = 1.5f, durability = 140f, armor = 0.25f,
                nitroCap = 110f, nitroCharge = 0.9f, nitroForce = 1f
            },
            new Preset
            {
                code = "HRC-V10", name = "Toro HRC-V10", inspiredBy = "Lamborghini Huracán", carClass = CarClass.Drift,
                description = "Лёгкий атмосферный V10, крутит дрифт как волчок. Не любит тараны.",
                color = new Color(0.4f, 0.85f, 0.1f), spd = 3, acc = 4, hnd = 5, tgh = 2,
                maxKmh = 325f, accel = 25f, launch = 0.3f, steerLow = 130f, steerHigh = 64f, grip = 11f,
                driftPower = 100f, driftGrip = 1.8f, driftRetention = 0.88f,
                mass = 1420f, ram = 0.85f, durability = 85f, armor = 0.05f,
                nitroCap = 95f, nitroCharge = 1.25f, nitroForce = 1f
            },
            new Preset
            {
                code = "RVT-V12", name = "Toro RVT-V12", inspiredBy = "Lamborghini Revuelto", carClass = CarClass.Hyper,
                description = "Гибридный V12: тяга на всех скоростях и сильный буст. Универсал для прямых.",
                color = new Color(1f, 0.78f, 0f), spd = 5, acc = 5, hnd = 3, tgh = 3,
                maxKmh = 350f, accel = 27f, launch = 0.7f, steerLow = 115f, steerHigh = 66f, grip = 12f,
                driftPower = 85f, driftGrip = 2.3f, driftRetention = 0.86f,
                mass = 1770f, ram = 1.1f, durability = 105f, armor = 0.12f,
                nitroCap = 100f, nitroCharge = 1f, nitroForce = 1.15f
            },
            new Preset
            {
                code = "Z34-RZ", name = "Ronin Z34", inspiredBy = "Nissan Z", carClass = CarClass.Drift,
                description = "Дешёвый и злой: медленный, но король заносов и набора нитро.",
                color = new Color(0.1f, 0.4f, 1f), spd = 2, acc = 3, hnd = 5, tgh = 2,
                maxKmh = 275f, accel = 21f, launch = 0.25f, steerLow = 140f, steerHigh = 70f, grip = 10f,
                driftPower = 105f, driftGrip = 1.6f, driftRetention = 0.92f,
                mass = 1570f, ram = 0.8f, durability = 80f, armor = 0.05f,
                nitroCap = 80f, nitroCharge = 1.6f, nitroForce = 0.95f
            },
            new Preset
            {
                code = "812-SF", name = "Stallion 812", inspiredBy = "Ferrari 812 Superfast", carClass = CarClass.Muscle,
                description = "Переднемоторный V12 GT: длинные дуги, стабильный на скорости, крепкий.",
                color = new Color(0.55f, 0.02f, 0.06f), spd = 4, acc = 3, hnd = 4, tgh = 3,
                maxKmh = 340f, accel = 24f, launch = 0.2f, steerLow = 112f, steerHigh = 64f, grip = 11.5f,
                driftPower = 90f, driftGrip = 2.1f, driftRetention = 0.87f,
                mass = 1630f, ram = 1.05f, durability = 100f, armor = 0.1f,
                nitroCap = 100f, nitroCharge = 1.1f, nitroForce = 1.05f
            },
            new Preset
            {
                code = "CYB-TRK", name = "Cyber Brick", inspiredBy = "Tesla Cybertruck", carClass = CarClass.Heavy,
                description = "Стальной утюг. Медленный, но сносит всё, во что въезжает.",
                color = new Color(0.7f, 0.72f, 0.74f), spd = 1, acc = 4, hnd = 1, tgh = 5,
                maxKmh = 215f, accel = 22f, launch = 1.4f, steerLow = 100f, steerHigh = 50f, grip = 9f,
                driftPower = 60f, driftGrip = 3f, driftRetention = 0.75f,
                mass = 3100f, ram = 2f, durability = 220f, armor = 0.45f,
                nitroCap = 120f, nitroCharge = 0.8f, nitroForce = 0.9f
            },
            new Preset
            {
                code = "RDS-2E", name = "Roadster 2E", inspiredBy = "Tesla Roadster (2nd gen)", carClass = CarClass.Hyper,
                description = "Электро-ракета: самый быстрый разгон в игре, но в поворотах надо думать.",
                color = new Color(0.8f, 0.1f, 0.5f), spd = 5, acc = 5, hnd = 3, tgh = 3,
                maxKmh = 345f, accel = 32f, launch = 1.8f, steerLow = 118f, steerHigh = 62f, grip = 12f,
                driftPower = 78f, driftGrip = 2.5f, driftRetention = 0.84f,
                mass = 1900f, ram = 1.2f, durability = 95f, armor = 0.1f,
                nitroCap = 100f, nitroCharge = 1f, nitroForce = 1.1f
            },
            new Preset
            {
                code = "GT2-RS", name = "Neun GT2", inspiredBy = "Porsche 911 GT2 RS", carClass = CarClass.Track,
                description = "Заднемоторный снайпер: точный руль и стабильность на скорости.",
                color = new Color(0.15f, 0.75f, 0.6f), spd = 4, acc = 4, hnd = 5, tgh = 2,
                maxKmh = 340f, accel = 25f, launch = 0.25f, steerLow = 128f, steerHigh = 72f, grip = 13.5f,
                driftPower = 82f, driftGrip = 2.4f, driftRetention = 0.85f,
                mass = 1470f, ram = 0.9f, durability = 90f, armor = 0.08f,
                nitroCap = 95f, nitroCharge = 1.1f, nitroForce = 1f
            },
            new Preset
            {
                code = "CHR-W16", name = "Molsheim W16", inspiredBy = "Bugatti Chiron", carClass = CarClass.Hyper,
                description = "Король максималки (380). Тяжёлый, в повороты заходит как танкер.",
                color = new Color(0.05f, 0.15f, 0.55f), spd = 5, acc = 4, hnd = 2, tgh = 4,
                maxKmh = 380f, accel = 26f, launch = 0.5f, steerLow = 100f, steerHigh = 50f, grip = 11f,
                driftPower = 70f, driftGrip = 2.7f, driftRetention = 0.85f,
                mass = 1995f, ram = 1.3f, durability = 125f, armor = 0.2f,
                nitroCap = 110f, nitroCharge = 0.9f, nitroForce = 1.2f
            },
            new Preset
            {
                code = "SNA-LM", name = "Senna LM", inspiredBy = "McLaren Senna", carClass = CarClass.Track,
                description = "Максимум прижима и сцепления. Самая лёгкая и самая хрупкая.",
                color = new Color(1f, 0.5f, 0f), spd = 4, acc = 4, hnd = 5, tgh = 1,
                maxKmh = 335f, accel = 26f, launch = 0.3f, steerLow = 132f, steerHigh = 78f, grip = 15f,
                driftPower = 75f, driftGrip = 2.8f, driftRetention = 0.82f,
                mass = 1200f, ram = 0.75f, durability = 75f, armor = 0f,
                nitroCap = 90f, nitroCharge = 1.2f, nitroForce = 1.05f
            },
        };

        [MenuItem("Car Monster/Create Car Roster", priority = 1)]
        public static void CreateRosterMenu()
        {
            CreateOrUpdateAll();
            EditorUtility.DisplayDialog("Car Monster", $"Ростер готов: {Presets.Length} машин в {CarsFolder}", "OK");
        }

        public static CarDataSO[] CreateOrUpdateAll()
        {
            EnsureFolder(CarsFolder);
            var result = new CarDataSO[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                Preset p = Presets[i];
                string path = $"{CarsFolder}/Car_{p.code}.asset";
                var data = AssetDatabase.LoadAssetAtPath<CarDataSO>(path);
                bool isNew = data == null;
                if (isNew) data = ScriptableObject.CreateInstance<CarDataSO>();

                Apply(data, p);
                if (isNew) AssetDatabase.CreateAsset(data, path);
                else EditorUtility.SetDirty(data);
                result[i] = data;
            }
            GetOrCreateBalance();
            AssetDatabase.SaveAssets();
            return result;
        }

        public static GameBalanceSO GetOrCreateBalance()
        {
            EnsureFolder(Path.GetDirectoryName(BalancePath)?.Replace('\\', '/'));
            var balance = AssetDatabase.LoadAssetAtPath<GameBalanceSO>(BalancePath);
            if (balance != null) return balance;
            balance = ScriptableObject.CreateInstance<GameBalanceSO>();
            AssetDatabase.CreateAsset(balance, BalancePath);
            return balance;
        }

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
            d.name = "Car_" + p.code;
        }

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
