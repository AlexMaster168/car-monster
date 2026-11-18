using UnityEditor;
using UnityEngine;

namespace CarMonster.EditorTools
{
    /// <summary>
    /// Собирает машины-заглушки из примитивов со всеми компонентами (подвеска, детали, FX, звук)
    /// и сохраняет префабы. Кузов зависит от BodyStyle; бронемашины получают ковш и броню.
    /// Ввод (игрок/ИИ) не входит в префаб — его добавляет TimeAttackManager при спавне.
    /// </summary>
    public static class CarFactory
    {
        private struct Dims
        {
            public Vector3 body;
            public float bodyY;
            public Vector3 cabin;
            public float cabinY, cabinZ;
            public float anchorY, anchorX;
            public float[] wheelZ;
            public bool spoiler;
        }

        private static Dims DimsOf(BodyStyle style)
        {
            switch (style)
            {
                case BodyStyle.Hyper:
                    return new Dims
                    {
                        body = new Vector3(2f, 0.48f, 4.5f), bodyY = 0.62f, cabin = new Vector3(1.4f, 0.36f, 1.6f), cabinY = 1.04f, cabinZ = -0.15f,
                        anchorY = 0.55f, anchorX = 0.86f, wheelZ = new[] { 1.5f, -1.45f }, spoiler = true
                    };
                case BodyStyle.Sedan:
                    return new Dims
                    {
                        body = new Vector3(1.96f, 0.62f, 4.95f), bodyY = 0.78f, cabin = new Vector3(1.6f, 0.5f, 2.7f), cabinY = 1.33f, cabinZ = -0.15f,
                        anchorY = 0.62f, anchorX = 0.84f, wheelZ = new[] { 1.6f, -1.55f }, spoiler = false
                    };
                case BodyStyle.Truck:
                    return new Dims
                    {
                        body = new Vector3(2.1f, 0.85f, 5.3f), bodyY = 1f, cabin = new Vector3(1.9f, 0.55f, 2.4f), cabinY = 1.7f, cabinZ = 0.1f,
                        anchorY = 0.85f, anchorX = 0.92f, wheelZ = new[] { 1.75f, -1.75f }, spoiler = false
                    };
                case BodyStyle.Suv:
                    return new Dims
                    {
                        body = new Vector3(2.15f, 0.95f, 4.9f), bodyY = 1.08f, cabin = new Vector3(1.95f, 0.75f, 2.9f), cabinY = 1.9f, cabinZ = -0.35f,
                        anchorY = 0.88f, anchorX = 0.94f, wheelZ = new[] { 1.6f, -1.6f }, spoiler = false
                    };
                case BodyStyle.Suv6x6:
                    return new Dims
                    {
                        body = new Vector3(2.25f, 1f, 5.8f), bodyY = 1.12f, cabin = new Vector3(2f, 0.8f, 2.7f), cabinY = 2f, cabinZ = 0.3f,
                        anchorY = 0.9f, anchorX = 0.98f, wheelZ = new[] { 2f, -1f, -2.3f }, spoiler = false
                    };
                default:
                    return new Dims
                    {
                        body = new Vector3(1.9f, 0.6f, 4.3f), bodyY = 0.75f, cabin = new Vector3(1.5f, 0.45f, 2f), cabinY = 1.27f, cabinZ = -0.25f,
                        anchorY = 0.6f, anchorX = 0.82f, wheelZ = new[] { 1.35f, -1.35f }, spoiler = true
                    };
            }
        }

        public static GameObject SaveCarPrefab(CarDataSO data, CarRoster.Preset preset, GameBalanceSO balance, string path)
        {
            GameObject root = BuildCar(data, preset, balance);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static GameObject BuildCar(CarDataSO data, CarRoster.Preset preset, GameBalanceSO balance)
        {
            Dims d = DimsOf(preset.style);
            var root = new GameObject($"Car_{data.chassisCode}");

            Material body = DemoSceneBuilder.Mat("Body_" + data.chassisCode, data.bodyColor, 0.6f, 0.82f);
            Material accent = DemoSceneBuilder.Mat("Accent_" + data.chassisCode, preset.accent, 0.85f, 0.85f);
            Material glass = DemoSceneBuilder.Mat("CarGlass", new Color(0.05f, 0.07f, 0.1f), 0.8f, 0.95f);
            Material tyre = DemoSceneBuilder.Mat("Tyre", new Color(0.06f, 0.06f, 0.06f), 0f, 0.2f);
            Material rim = DemoSceneBuilder.Mat("Rim", new Color(0.55f, 0.56f, 0.6f), 0.9f, 0.8f);
            Material lights = DemoSceneBuilder.Mat("TailLight", new Color(1f, 0.05f, 0.05f), 0f, 0.5f, new Color(1f, 0.05f, 0.05f) * 3f);
            Material head = DemoSceneBuilder.Mat("HeadLight", Color.white, 0f, 0.8f, Color.white * 2f);
            Material steel = DemoSceneBuilder.Mat("ArmorSteel", new Color(0.22f, 0.23f, 0.25f), 0.85f, 0.45f);

            float halfL = d.body.z * 0.5f;
            float halfW = d.body.x * 0.5f;
            float bottom = d.bodyY - d.body.y * 0.5f;
            float top = d.bodyY + d.body.y * 0.5f;

            // Корпус и кабина — основа составного коллайдера.
            Part(root.transform, "Body", new Vector3(0f, d.bodyY, 0f), d.body, body, true);
            Part(root.transform, "Cabin", new Vector3(0f, d.cabinY, d.cabinZ), d.cabin, glass, true);
            Part(root.transform, "TailLights", new Vector3(0f, top - 0.12f, -halfL - 0.01f), new Vector3(d.body.x * 0.85f, 0.1f, 0.05f), lights, false);
            Part(root.transform, "HeadLights", new Vector3(0f, top - 0.14f, halfL + 0.01f), new Vector3(d.body.x * 0.8f, 0.08f, 0.05f), head, false);
            if (preset.accent != new Color(0.08f, 0.08f, 0.09f) && !preset.plow)
                Part(root.transform, "Stripe", new Vector3(0f, top + 0.005f, 0.4f), new Vector3(0.45f, 0.01f, d.body.z * 0.8f), accent, false);

            // Отстреливаемые детали.
            Detachable(root.transform, "FrontBumper", new Vector3(0f, bottom + 0.18f, halfL + 0.12f), new Vector3(d.body.x + 0.05f, 0.32f, 0.3f), preset.plow ? steel : body, 30f);
            Detachable(root.transform, "RearBumper", new Vector3(0f, bottom + 0.2f, -halfL - 0.12f), new Vector3(d.body.x + 0.05f, 0.32f, 0.3f), preset.plow ? steel : body, 30f);
            Detachable(root.transform, "Hood", new Vector3(0f, top + 0.03f, halfL - 0.8f), new Vector3(d.body.x * 0.88f, 0.06f, 1.2f), body, 15f);
            Detachable(root.transform, "DoorL", new Vector3(-halfW - 0.04f, d.bodyY + 0.05f, d.cabinZ), new Vector3(0.08f, d.body.y * 0.75f, Mathf.Min(1.4f, d.cabin.z * 0.6f)), body, 25f);
            Detachable(root.transform, "DoorR", new Vector3(halfW + 0.04f, d.bodyY + 0.05f, d.cabinZ), new Vector3(0.08f, d.body.y * 0.75f, Mathf.Min(1.4f, d.cabin.z * 0.6f)), body, 25f);
            if (d.spoiler)
                Detachable(root.transform, "Spoiler", new Vector3(0f, top + 0.42f, -halfL + 0.3f), new Vector3(d.body.x * 0.92f, 0.08f, 0.45f), preset.accent == new Color(0.08f, 0.08f, 0.09f) ? glass : accent, 12f);

            if (preset.plow) BuildArmor(root.transform, d, preset.style, body, accent, steel, head);

            // Колёса.
            int axles = d.wheelZ.Length;
            var wheels = new ArcadeCarController.Wheel[axles * 2];
            float wheelDiameter = data.wheelRadius * 2f;
            for (int a = 0; a < axles; a++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int i = a * 2 + side;
                    float x = side == 0 ? -d.anchorX : d.anchorX;
                    var anchorPos = new Vector3(x, d.anchorY, d.wheelZ[a]);

                    var anchor = new GameObject($"WheelAnchor_{i}");
                    anchor.transform.SetParent(root.transform, false);
                    anchor.transform.localPosition = anchorPos;

                    var pivot = new GameObject($"WheelVisual_{i}");
                    pivot.transform.SetParent(root.transform, false);
                    pivot.transform.localPosition = anchorPos - Vector3.up * (data.suspensionLength * 0.7f);

                    GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    t.name = "Tyre";
                    Object.DestroyImmediate(t.GetComponent<Collider>());
                    t.transform.SetParent(pivot.transform, false);
                    t.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    float width = preset.plow ? 0.24f : 0.16f;
                    t.transform.localScale = new Vector3(wheelDiameter, width, wheelDiameter);
                    t.GetComponent<Renderer>().sharedMaterial = tyre;

                    GameObject r = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    r.name = "Rim";
                    Object.DestroyImmediate(r.GetComponent<Collider>());
                    r.transform.SetParent(pivot.transform, false);
                    r.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    r.transform.localPosition = new Vector3(side == 0 ? -0.02f : 0.02f, 0f, 0f);
                    r.transform.localScale = new Vector3(wheelDiameter * 0.6f, width + 0.01f, wheelDiameter * 0.6f);
                    r.GetComponent<Renderer>().sharedMaterial = rim;

                    wheels[i] = new ArcadeCarController.Wheel { anchor = anchor.transform, visual = pivot.transform, isFront = a == 0 };
                }
            }

            // FX.
            Material sparkMat = DemoSceneBuilder.ParticleMat("Spark", new Color(1f, 0.75f, 0.2f));
            Material boostMat = DemoSceneBuilder.ParticleMat("Boost", new Color(0.3f, 0.6f, 1f));
            Material fireMat = DemoSceneBuilder.ParticleMat("Fire", new Color(1f, 0.4f, 0.05f));
            Material smokeMat = DemoSceneBuilder.ParticleMat("Smoke", new Color(0.35f, 0.35f, 0.35f, 0.6f));

            ParticleSystem sparks = Fx(root.transform, "ImpactSparks", Vector3.zero, Quaternion.identity, sparkMat, 0.5f, 18f, 0.12f, 0f, 30f, true, false);
            ParticleSystem explosion = Fx(root.transform, "WreckExplosion", new Vector3(0f, d.bodyY + 0.3f, 0f), Quaternion.Euler(-90f, 0f, 0f), fireMat, 0.8f, 14f, 1.4f, 0f, 70f, true, false, 80);
            ParticleSystem smoke = Fx(root.transform, "WreckSmoke", new Vector3(0f, top + 0.2f, halfL * 0.4f), Quaternion.Euler(-90f, 0f, 0f), smokeMat, 2.5f, 3f, 1.6f, 18f, 25f, true, true);

            var boost = new ParticleSystem[2];
            var fire = new ParticleSystem[2];
            var trails = new TrailRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var exhaust = new Vector3(i == 0 ? -0.45f : 0.45f, bottom + 0.15f, -halfL - 0.1f);
                Quaternion back = Quaternion.Euler(0f, 180f, 0f);
                boost[i] = Fx(root.transform, $"NitroFlame_{i}", exhaust, back, boostMat, 0.25f, 9f, 0.45f, 160f, 8f, false, true);
                fire[i] = Fx(root.transform, $"PerfectFire_{i}", exhaust, back, fireMat, 0.45f, 12f, 0.8f, 220f, 12f, true, true);

                var trailGo = new GameObject($"FireTrail_{i}");
                trailGo.transform.SetParent(root.transform, false);
                trailGo.transform.localPosition = exhaust;
                TrailRenderer trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = 0.35f;
                trail.widthMultiplier = 0.4f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.sharedMaterial = fireMat;
                trail.emitting = false;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trails[i] = trail;
            }

            // Компоненты.
            var controller = root.AddComponent<ArcadeCarController>();
            controller.EditorSetup(data, null, wheels);
            root.GetComponent<Rigidbody>().mass = data.mass;
            // Низкий центр масс — стабильность в поворотах и на трамплинах.
            SetCenterOfMass(controller, new Vector3(0f, bottom - 0.1f, 0f));

            root.AddComponent<NitroSystem>().EditorSetup(balance, boost, fire, trails);
            root.AddComponent<TakedownSystem>().EditorSetup(balance, sparks, explosion, smoke);

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = true;
            audio.spatialBlend = 1f;
            root.AddComponent<EngineAudio>().EditorSetup(balance);

            return root;
        }

        /// <summary>Ковш, броня, решётки и багажник бронемашины.</summary>
        private static void BuildArmor(Transform root, Dims d, BodyStyle style, Material body, Material accent, Material steel, Material lightMat)
        {
            float halfL = d.body.z * 0.5f;
            float halfW = d.body.x * 0.5f;
            float plowW = d.body.x + 0.5f;
            float z0 = halfL + 0.55f;

            // Ковш: задняя стенка с наклоном, нижняя кромка, щёки, зубья, рычаги. Не отлетает.
            var plow = new GameObject("RamPlow");
            plow.transform.SetParent(root, false);
            Part(plow.transform, "BackPlate", new Vector3(0f, 0.95f, z0), new Vector3(plowW, 0.95f, 0.14f), steel, true, Quaternion.Euler(-14f, 0f, 0f));
            Part(plow.transform, "Lip", new Vector3(0f, 0.52f, z0 + 0.38f), new Vector3(plowW, 0.1f, 0.8f), accent, true, Quaternion.Euler(10f, 0f, 0f));
            Part(plow.transform, "CheekL", new Vector3(-plowW * 0.5f, 0.9f, z0 + 0.25f), new Vector3(0.1f, 0.9f, 0.85f), accent, true);
            Part(plow.transform, "CheekR", new Vector3(plowW * 0.5f, 0.9f, z0 + 0.25f), new Vector3(0.1f, 0.9f, 0.85f), accent, true);
            Part(plow.transform, "TopEdge", new Vector3(0f, 1.44f, z0 - 0.12f), new Vector3(plowW + 0.05f, 0.08f, 0.2f), accent, false);
            for (int t = 0; t < 6; t++)
            {
                float x = Mathf.Lerp(-plowW * 0.42f, plowW * 0.42f, t / 5f);
                Part(plow.transform, $"Tooth_{t}", new Vector3(x, 0.5f, z0 + 0.86f), new Vector3(0.14f, 0.1f, 0.25f), steel, false);
            }
            Part(plow.transform, "ArmL", new Vector3(-0.6f, 0.9f, halfL + 0.2f), new Vector3(0.16f, 0.16f, 0.8f), steel, false);
            Part(plow.transform, "ArmR", new Vector3(0.6f, 0.9f, halfL + 0.2f), new Vector3(0.16f, 0.16f, 0.8f), steel, false);
            Part(plow.transform, "Hydraulic", new Vector3(0f, 1.25f, halfL + 0.1f), new Vector3(0.12f, 0.12f, 0.9f), accent, false, Quaternion.Euler(18f, 0f, 0f));

            // Бронепластины на бортах — отлетают при тейкдауне.
            Detachable(root, "ArmorL", new Vector3(-halfW - 0.1f, d.bodyY - 0.05f, -0.1f), new Vector3(0.1f, d.body.y * 0.7f, d.body.z * 0.72f), steel, 60f);
            Detachable(root, "ArmorR", new Vector3(halfW + 0.1f, d.bodyY - 0.05f, -0.1f), new Vector3(0.1f, d.body.y * 0.7f, d.body.z * 0.72f), steel, 60f);

            // Решётка лобового стекла и багажник со светом на крыше.
            float cabinTop = d.cabinY + d.cabin.y * 0.5f;
            float cabinFront = d.cabinZ + d.cabin.z * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float x = Mathf.Lerp(-d.cabin.x * 0.4f, d.cabin.x * 0.4f, i / 3f);
                Part(root, $"Grille_{i}", new Vector3(x, d.cabinY, cabinFront + 0.03f), new Vector3(0.05f, d.cabin.y * 0.9f, 0.05f), steel, false);
            }
            Detachable(root, "RoofRack", new Vector3(0f, cabinTop + 0.1f, d.cabinZ), new Vector3(d.cabin.x * 0.9f, 0.1f, d.cabin.z * 0.85f), steel, 20f);
            Part(root, "LightBar", new Vector3(0f, cabinTop + 0.2f, d.cabinZ + d.cabin.z * 0.4f), new Vector3(d.cabin.x * 0.8f, 0.1f, 0.12f), lightMat, false);

            // Запаска на корме.
            GameObject spare = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spare.name = "SpareWheel";
            spare.transform.SetParent(root, false);
            spare.transform.localPosition = new Vector3(0f, d.bodyY + 0.1f, -halfL - 0.18f);
            spare.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            spare.transform.localScale = new Vector3(0.8f, 0.12f, 0.8f);
            spare.GetComponent<Renderer>().sharedMaterial = DemoSceneBuilder.Mat("Tyre", new Color(0.06f, 0.06f, 0.06f), 0f, 0.2f);
            AddDetachable(spare, 20f);

            if (style == BodyStyle.Suv6x6)
            {
                // Кузов-платформа сзади.
                float bedZ = d.cabinZ - d.cabin.z * 0.5f - 1.2f;
                Part(root, "BedL", new Vector3(-halfW + 0.05f, d.bodyY + d.body.y * 0.5f + 0.25f, bedZ), new Vector3(0.08f, 0.5f, 2.2f), body, true);
                Part(root, "BedR", new Vector3(halfW - 0.05f, d.bodyY + d.body.y * 0.5f + 0.25f, bedZ), new Vector3(0.08f, 0.5f, 2.2f), body, true);
                Detachable(root, "Tailgate", new Vector3(0f, d.bodyY + d.body.y * 0.5f + 0.25f, -halfL + 0.05f), new Vector3(d.body.x - 0.1f, 0.5f, 0.08f), body, 25f);
            }
        }

        private static void SetCenterOfMass(ArcadeCarController controller, Vector3 offset)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("centerOfMassOffset").vector3Value = offset;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Part(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider,
            Quaternion? rotation = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void Detachable(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, float mass)
        {
            AddDetachable(Part(parent, name, localPos, size, mat, true), mass);
        }

        private static void AddDetachable(GameObject go, float mass)
        {
            var part = go.AddComponent<DetachablePart>();
            var so = new SerializedObject(part);
            so.FindProperty("partMass").floatValue = mass;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ParticleSystem Fx(Transform parent, string name, Vector3 localPos, Quaternion localRot, Material mat,
            float lifetime, float speed, float size, float rate, float coneAngle, bool worldSpace, bool loop, int burst = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = loop ? 1f : 0.5f;
            main.loop = loop;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.simulationSpace = worldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.gravityModifier = name.Contains("Spark") ? 1.5f : 0f;
            main.maxParticles = 400;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            if (burst > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.12f;

            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, name.Contains("Smoke") ? 2.5f : 0.1f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }
    }
}
