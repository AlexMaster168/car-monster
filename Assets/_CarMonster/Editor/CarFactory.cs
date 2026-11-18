using UnityEditor;
using UnityEngine;

namespace CarMonster.EditorTools
{
    /// <summary>
    /// Собирает машину-заглушку из примитивов со всеми компонентами: подвеска, отстреливаемые детали,
    /// FX нитро/искр/взрыва. Подходит как шаблон: замените меши на свои — компоненты останутся.
    /// </summary>
    public static class CarFactory
    {
        private static readonly Vector3[] WheelPositions =
        {
            new Vector3(-0.82f, 0.6f, 1.35f),
            new Vector3(0.82f, 0.6f, 1.35f),
            new Vector3(-0.82f, 0.6f, -1.35f),
            new Vector3(0.82f, 0.6f, -1.35f),
        };

        public static ArcadeCarController CreateCar(CarDataSO data, GameBalanceSO balance, bool isPlayer,
            Vector3 position, Quaternion rotation, Transform parent)
        {
            var root = new GameObject($"{(isPlayer ? "Player" : "AI")}_{data.chassisCode}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);

            Material body = DemoSceneBuilder.Mat("Body_" + data.chassisCode, data.bodyColor, 0.6f, 0.82f);
            Material glass = DemoSceneBuilder.Mat("CarGlass", new Color(0.05f, 0.07f, 0.1f), 0.8f, 0.95f);
            Material tyre = DemoSceneBuilder.Mat("Tyre", new Color(0.06f, 0.06f, 0.06f), 0f, 0.2f);
            Material lights = DemoSceneBuilder.Mat("TailLight", new Color(1f, 0.05f, 0.05f), 0f, 0.5f, new Color(1f, 0.05f, 0.05f) * 3f);
            Material head = DemoSceneBuilder.Mat("HeadLight", Color.white, 0f, 0.8f, Color.white * 2f);

            // Корпус — его коллайдер = основная часть составного коллайдера машины.
            GameObject chassis = Part(root.transform, "Body", new Vector3(0f, 0.75f, 0f), new Vector3(1.9f, 0.6f, 4.3f), body, true);
            Part(root.transform, "Cabin", new Vector3(0f, 1.27f, -0.25f), new Vector3(1.5f, 0.45f, 2f), glass, true);
            Part(root.transform, "TailLights", new Vector3(0f, 0.85f, -2.16f), new Vector3(1.6f, 0.12f, 0.05f), lights, false);
            Part(root.transform, "HeadLights", new Vector3(0f, 0.8f, 2.16f), new Vector3(1.5f, 0.1f, 0.05f), head, false);

            // Отстреливаемые детали.
            Detachable(root.transform, "FrontBumper", new Vector3(0f, 0.55f, 2.27f), new Vector3(1.95f, 0.32f, 0.3f), body, 30f);
            Detachable(root.transform, "RearBumper", new Vector3(0f, 0.6f, -2.27f), new Vector3(1.95f, 0.32f, 0.3f), body, 30f);
            Detachable(root.transform, "Spoiler", new Vector3(0f, 1.3f, -1.95f), new Vector3(1.8f, 0.08f, 0.45f), glass, 12f);
            Detachable(root.transform, "DoorL", new Vector3(-0.99f, 0.82f, 0.15f), new Vector3(0.08f, 0.45f, 1.3f), body, 25f);
            Detachable(root.transform, "DoorR", new Vector3(0.99f, 0.82f, 0.15f), new Vector3(0.08f, 0.45f, 1.3f), body, 25f);
            Detachable(root.transform, "Hood", new Vector3(0f, 1.07f, 1.45f), new Vector3(1.7f, 0.06f, 1.2f), body, 15f);

            // Колёса.
            var wheels = new ArcadeCarController.Wheel[4];
            for (int i = 0; i < 4; i++)
            {
                var anchor = new GameObject($"WheelAnchor_{i}");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.localPosition = WheelPositions[i];

                var pivot = new GameObject($"WheelVisual_{i}");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = WheelPositions[i] - Vector3.up * 0.3f;

                GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mesh.name = "Tyre";
                Object.DestroyImmediate(mesh.GetComponent<Collider>());
                mesh.transform.SetParent(pivot.transform, false);
                mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                float d = data.wheelRadius * 2f;
                mesh.transform.localScale = new Vector3(d, 0.16f, d);
                mesh.GetComponent<Renderer>().sharedMaterial = tyre;

                wheels[i] = new ArcadeCarController.Wheel
                {
                    anchor = anchor.transform,
                    visual = pivot.transform,
                    isFront = i < 2
                };
            }

            // FX.
            Material sparkMat = DemoSceneBuilder.ParticleMat("Spark", new Color(1f, 0.75f, 0.2f));
            Material boostMat = DemoSceneBuilder.ParticleMat("Boost", new Color(0.3f, 0.6f, 1f));
            Material fireMat = DemoSceneBuilder.ParticleMat("Fire", new Color(1f, 0.4f, 0.05f));
            Material smokeMat = DemoSceneBuilder.ParticleMat("Smoke", new Color(0.35f, 0.35f, 0.35f, 0.6f));

            ParticleSystem sparks = Fx(root.transform, "ImpactSparks", Vector3.zero, Quaternion.identity, sparkMat,
                0.5f, 18f, 0.12f, 0f, 30f, true, false);
            ParticleSystem explosion = Fx(root.transform, "WreckExplosion", new Vector3(0f, 1f, 0f), Quaternion.Euler(-90f, 0f, 0f), fireMat,
                0.8f, 14f, 1.4f, 0f, 70f, true, false, 80);
            ParticleSystem smoke = Fx(root.transform, "WreckSmoke", new Vector3(0f, 1.2f, 0.8f), Quaternion.Euler(-90f, 0f, 0f), smokeMat,
                2.5f, 3f, 1.6f, 18f, 25f, true, true);

            var boost = new ParticleSystem[2];
            var fire = new ParticleSystem[2];
            var trails = new TrailRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.45f : 0.45f;
                var exhaustPos = new Vector3(x, 0.55f, -2.4f);
                Quaternion back = Quaternion.Euler(0f, 180f, 0f);
                boost[i] = Fx(root.transform, $"NitroFlame_{i}", exhaustPos, back, boostMat, 0.25f, 9f, 0.45f, 160f, 8f, false, true);
                fire[i] = Fx(root.transform, $"PerfectFire_{i}", exhaustPos, back, fireMat, 0.45f, 12f, 0.8f, 220f, 12f, true, true);

                var trailGo = new GameObject($"FireTrail_{i}");
                trailGo.transform.SetParent(root.transform, false);
                trailGo.transform.localPosition = exhaustPos;
                TrailRenderer trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = 0.35f;
                trail.widthMultiplier = 0.4f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.sharedMaterial = fireMat;
                trail.emitting = false;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trails[i] = trail;
            }

            // Компоненты. Источник ввода добавляем первым.
            CarInputSource input = isPlayer
                ? root.AddComponent<PlayerCarInput>()
                : (CarInputSource)root.AddComponent<RacerAI>();

            // RacerAI тянет ArcadeCarController через RequireComponent, PlayerCarInput — нет.
            if (!root.TryGetComponent(out ArcadeCarController controller))
                controller = root.AddComponent<ArcadeCarController>();
            controller.EditorSetup(data, input, wheels);
            root.GetComponent<Rigidbody>().mass = data.mass;

            root.AddComponent<NitroSystem>().EditorSetup(balance, boost, fire, trails);
            root.AddComponent<TakedownSystem>().EditorSetup(balance, sparks, explosion, smoke);

            EditorUtility.SetDirty(root);
            return controller;
        }

        private static GameObject Part(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void Detachable(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, float mass)
        {
            GameObject go = Part(parent, name, localPos, size, mat, true);
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
