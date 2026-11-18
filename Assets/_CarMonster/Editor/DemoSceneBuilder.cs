using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if CINEMACHINE_3
using Unity.Cinemachine;
#endif

namespace CarMonster.EditorTools
{
    /// <summary>
    /// Собирает играбельную демо-сцену из примитивов: трасса-стадион с S-образной связкой,
    /// трамплины, чекпоинты, игрок + 4 бота, Cinemachine-камеры, HUD. Car Monster → Build Demo Scene.
    /// </summary>
    public static class DemoSceneBuilder
    {
        public const string ScenePath = CarRoster.Root + "/Scenes/Demo_TimeAttack.unity";
        private const string MaterialsFolder = CarRoster.Root + "/Materials";

        private const float StraightLength = 520f;
        private const float CornerRadius = 140f;
        private const float RoadWidth = 22f;

        private static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        [MenuItem("Car Monster/Build Demo Scene", priority = 2)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static Scene Build()
        {
            MatCache.Clear();
            CarRoster.CreateOrUpdateAll();
            GameBalanceSO balance = CarRoster.GetOrCreateBalance();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 250f;
            RenderSettings.fogEndDistance = 900f;
            RenderSettings.fogColor = new Color(0.62f, 0.7f, 0.8f);

            Light sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
            }

            var track = new GameObject("Track");
            WaypointCircuit circuit = BuildCircuit(track.transform);
            BuildEnvironment(track.transform, circuit);
            Checkpoint[] gates = BuildCheckpoints(track.transform, circuit);

            var racersRoot = new GameObject("Racers");
            var racers = new List<ArcadeCarController>();

            ArcadeCarController player = CarFactory.CreateCar(CarRoster.Load("R35-GT"), balance, true,
                PoseOn(circuit, 22f, 0f, out Quaternion r0), r0, racersRoot.transform);
            player.gameObject.tag = "Player";
            racers.Add(player);

            (string code, float dist, float lane, float skill, float aggro)[] bots =
            {
                ("PLAID-EV", 36f, -5f, 0.65f, 0.9f),
                ("SF90-V8", 36f, 5f, 0.85f, 0.3f),
                ("HRC-V10", 50f, -5f, 0.75f, 0.5f),
                ("CYB-TRK", 50f, 5f, 0.6f, 1f),
            };
            foreach (var b in bots)
            {
                ArcadeCarController bot = CarFactory.CreateCar(CarRoster.Load(b.code), balance, false,
                    PoseOn(circuit, b.dist, b.lane, out Quaternion rot), rot, racersRoot.transform);
                bot.GetComponent<RacerAI>().EditorSetup(balance, circuit, b.skill, b.aggro, b.lane * 0.6f);
                racers.Add(bot);
            }

            var managerGo = new GameObject("TimeAttackManager");
            managerGo.AddComponent<TimeAttackManager>().EditorSetup(balance, player, circuit, gates, racers);
            managerGo.AddComponent<RaceDebugHUD>();

            BuildCameras(balance, player);

            CarRoster.EnsureFolder(CarRoster.Root + "/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CarMonster] Demo scene built: {ScenePath}");
            return scene;
        }

        // ---------- Track ----------

        private static WaypointCircuit BuildCircuit(Transform parent)
        {
            var go = new GameObject("Circuit");
            go.transform.SetParent(parent, false);
            var points = new List<Vector3>();
            float half = StraightLength * 0.5f;
            const float step = 35f;

            // Прямая A (старт/финиш): x = +R, едем по +Z.
            for (float z = -half; z < half; z += step) points.Add(new Vector3(CornerRadius, 0f, z));
            // Верхний разворот.
            for (float a = 0f; a < 180f; a += 15f)
            {
                float rad = a * Mathf.Deg2Rad;
                points.Add(new Vector3(Mathf.Cos(rad) * CornerRadius, 0f, half + Mathf.Sin(rad) * CornerRadius));
            }
            // Прямая B с S-образной связкой под дрифт.
            for (float z = half; z > -half; z -= step)
            {
                float t = Mathf.InverseLerp(half, -half, z);
                float x = -CornerRadius + Mathf.Sin(t * Mathf.PI * 2f) * 38f;
                points.Add(new Vector3(x, 0f, z));
            }
            // Нижний разворот.
            for (float a = 180f; a < 360f; a += 15f)
            {
                float rad = a * Mathf.Deg2Rad;
                points.Add(new Vector3(Mathf.Cos(rad) * CornerRadius, 0f, -half + Mathf.Sin(rad) * CornerRadius));
            }

            var transforms = new Transform[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                var wp = new GameObject($"WP_{i:00}");
                wp.transform.SetParent(go.transform, false);
                wp.transform.position = points[i];
                transforms[i] = wp.transform;
            }

            var circuit = go.AddComponent<WaypointCircuit>();
            circuit.EditorSetWaypoints(transforms, true);
            return circuit;
        }

        private static void BuildEnvironment(Transform parent, WaypointCircuit circuit)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(1600f, 1f, 1800f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.GetComponent<Renderer>().sharedMaterial = Mat("Ground", new Color(0.24f, 0.32f, 0.22f), 0f, 0.1f);
            ground.isStatic = true;

            var road = new GameObject("Road");
            road.transform.SetParent(parent, false);
            Material roadMat = Mat("Road", new Color(0.13f, 0.13f, 0.15f), 0f, 0.35f);
            Material lineMat = Mat("RoadLine", new Color(0.95f, 0.95f, 0.95f), 0f, 0.3f, Color.white * 0.3f);
            Material curbA = Mat("CurbRed", new Color(0.85f, 0.1f, 0.1f), 0f, 0.3f);
            Material curbB = Mat("CurbWhite", new Color(0.92f, 0.92f, 0.92f), 0f, 0.3f);

            const float seg = 6f;
            int index = 0;
            for (float d = 0f; d < circuit.Length; d += seg, index++)
            {
                Vector3 a = circuit.GetPoint(d);
                Vector3 b = circuit.GetPoint(d + seg);
                Vector3 mid = (a + b) * 0.5f;
                Quaternion rot = Quaternion.LookRotation((b - a).normalized, Vector3.up);
                Vector3 right = rot * Vector3.right;

                VisualBox(road.transform, "Asphalt", mid + Vector3.up * 0.01f, rot, new Vector3(RoadWidth, 0.04f, seg + 0.8f), roadMat);
                Material curb = index % 2 == 0 ? curbA : curbB;
                VisualBox(road.transform, "CurbL", mid - right * (RoadWidth * 0.5f + 0.6f) + Vector3.up * 0.02f, rot, new Vector3(1.2f, 0.06f, seg + 0.1f), curb);
                VisualBox(road.transform, "CurbR", mid + right * (RoadWidth * 0.5f + 0.6f) + Vector3.up * 0.02f, rot, new Vector3(1.2f, 0.06f, seg + 0.1f), curb);
                if (index % 3 == 0)
                    VisualBox(road.transform, "Line", mid + Vector3.up * 0.035f, rot, new Vector3(0.35f, 0.02f, seg * 0.6f), lineMat);
            }

            // Трамплины на обеих прямых.
            Material rampMat = Mat("Ramp", new Color(1f, 0.55f, 0.05f), 0.2f, 0.5f);
            Ramp(parent, circuit, 150f, rampMat);
            float straightB = StraightLength + Mathf.PI * CornerRadius;
            Ramp(parent, circuit, straightB + 60f, rampMat);
            Ramp(parent, circuit, StraightLength - 120f, rampMat);

            // Бетонные блоки — повод для объезда у ИИ.
            Material block = Mat("Concrete", new Color(0.6f, 0.6f, 0.62f), 0f, 0.2f);
            Obstacle(parent, circuit, 330f, 6f, block);
            Obstacle(parent, circuit, 345f, -6f, block);
            Obstacle(parent, circuit, straightB + 330f, 0f, block);

            // Декор — колонны-«небоскрёбы» по периметру для ощущения скорости.
            var deco = new GameObject("Decor");
            deco.transform.SetParent(parent, false);
            Material tower = Mat("Tower", new Color(0.25f, 0.28f, 0.36f), 0.4f, 0.7f);
            Material towerGlow = Mat("TowerGlow", new Color(0.1f, 0.8f, 1f), 0f, 0.5f, new Color(0.1f, 0.8f, 1f) * 2f);
            var rng = new System.Random(35);
            for (float d = 0f; d < circuit.Length; d += 28f)
            {
                Vector3 p = circuit.GetPoint(d);
                Vector3 dir = circuit.GetDirection(d);
                Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                float side = (int)(d / 28f) % 2 == 0 ? 1f : -1f;
                float h = 8f + (float)rng.NextDouble() * 40f;
                Vector3 pos = p + right * side * (RoadWidth * 0.5f + 14f + (float)rng.NextDouble() * 20f) + Vector3.up * (h * 0.5f);
                GameObject t = VisualBox(deco.transform, "Tower", pos, Quaternion.LookRotation(dir), new Vector3(6f, h, 6f),
                    rng.NextDouble() > 0.75 ? towerGlow : tower);
                t.isStatic = true;
            }
        }

        private static void Ramp(Transform parent, WaypointCircuit circuit, float distance, Material mat)
        {
            const float length = 22f;
            const float angle = 12f;
            const float thickness = 1f;
            Vector3 dir = circuit.GetDirection(distance);
            Vector3 p = circuit.GetPoint(distance);
            float cy = Mathf.Sin(angle * Mathf.Deg2Rad) * length * 0.5f - Mathf.Cos(angle * Mathf.Deg2Rad) * thickness * 0.5f;

            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Ramp";
            ramp.transform.SetParent(parent, false);
            ramp.transform.position = p + Vector3.up * cy;
            ramp.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(-angle, 0f, 0f);
            ramp.transform.localScale = new Vector3(RoadWidth * 0.55f, thickness, length);
            ramp.GetComponent<Renderer>().sharedMaterial = mat;
            ramp.isStatic = true;
        }

        private static void Obstacle(Transform parent, WaypointCircuit circuit, float distance, float lateral, Material mat)
        {
            Vector3 dir = circuit.GetDirection(distance);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "ConcreteBlock";
            block.transform.SetParent(parent, false);
            block.transform.position = circuit.GetPoint(distance) + right * lateral + Vector3.up * 1f;
            block.transform.rotation = Quaternion.LookRotation(dir);
            block.transform.localScale = new Vector3(3f, 2f, 3f);
            block.GetComponent<Renderer>().sharedMaterial = mat;
            block.isStatic = true;
        }

        private static Checkpoint[] BuildCheckpoints(Transform parent, WaypointCircuit circuit)
        {
            var root = new GameObject("Checkpoints");
            root.transform.SetParent(parent, false);
            Material start = Mat("GateStart", Color.white, 0f, 0.4f, Color.white * 1.5f);
            Material gate = Mat("Gate", new Color(0.1f, 1f, 0.4f), 0f, 0.4f, new Color(0.1f, 1f, 0.4f) * 2f);

            const int count = 5;
            var result = new Checkpoint[count];
            for (int i = 0; i < count; i++)
            {
                float d = circuit.Length * i / count;
                Vector3 p = circuit.GetPoint(d);
                Quaternion rot = Quaternion.LookRotation(circuit.GetDirection(d), Vector3.up);

                var go = new GameObject(i == 0 ? "CP_00_StartFinish" : $"CP_{i:00}");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(p + Vector3.up * 5f, rot);
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(RoadWidth + 16f, 10f, 3f);
                result[i] = go.AddComponent<Checkpoint>();

                Material m = i == 0 ? start : gate;
                float halfW = RoadWidth * 0.5f + 3f;
                VisualBox(go.transform, "PillarL", p - rot * Vector3.right * halfW + Vector3.up * 4f, rot, new Vector3(1f, 8f, 1f), m);
                VisualBox(go.transform, "PillarR", p + rot * Vector3.right * halfW + Vector3.up * 4f, rot, new Vector3(1f, 8f, 1f), m);
                VisualBox(go.transform, "Beam", p + Vector3.up * 8.2f, rot, new Vector3(halfW * 2f + 1f, 0.8f, 1f), m);
            }
            return result;
        }

        private static void BuildCameras(GameBalanceSO balance, ArcadeCarController player)
        {
            Camera main = Camera.main;
            if (main == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                main = camGo.AddComponent<Camera>();
            }
            main.farClipPlane = 2000f;
            main.fieldOfView = balance.cameraFx.baseFov;
            Transform pt = player.transform;
            main.transform.SetPositionAndRotation(pt.position - pt.forward * 8f + Vector3.up * 3f,
                Quaternion.LookRotation(pt.forward * 10f - Vector3.up * 2f));

            var rig = new GameObject("CameraRig");
#if CINEMACHINE_3
            CinemachineBrain brain = main.gameObject.AddComponent<CinemachineBrain>();
            brain.IgnoreTimeScale = true;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.35f);

            CinemachineCamera follow = NewCmCamera(rig.transform, "CM_Follow", player.transform, balance.cameraFx.baseFov);
            var body = follow.gameObject.AddComponent<CinemachineFollow>();
            body.FollowOffset = new Vector3(0f, 2.6f, -7.2f);
            body.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LazyFollow;
            body.TrackerSettings.PositionDamping = new Vector3(0.15f, 0.25f, 0.35f);
            var aim = follow.gameObject.AddComponent<CinemachineRotationComposer>();
            aim.TargetOffset = new Vector3(0f, 1.1f, 2f);
            aim.Damping = new Vector2(0.15f, 0.15f);

            CinemachineCamera takedown = NewCmCamera(rig.transform, "CM_Takedown", player.transform, 50f);
            var tdBody = takedown.gameObject.AddComponent<CinemachineFollow>();
            tdBody.FollowOffset = new Vector3(6f, 3f, -7f);
            tdBody.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            takedown.gameObject.AddComponent<CinemachineRotationComposer>();

            var impulse = rig.AddComponent<CinemachineImpulseSource>();
            var controller = rig.AddComponent<CameraSpeedController>();
            controller.EditorSetup(balance, player, follow, takedown, impulse);
            takedown.gameObject.SetActive(false);
#else
            rig.AddComponent<CameraSpeedController>();
            Debug.LogWarning("[CarMonster] Cinemachine 3 не найден — CameraSpeedController работает в режиме обычной камеры. Настройте ссылки вручную.");
#endif
        }

#if CINEMACHINE_3
        private static CinemachineCamera NewCmCamera(Transform parent, string name, Transform target, float fov)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Follow = target;
            cam.LookAt = target;
            cam.Lens.FieldOfView = fov;
            cam.Lens.FarClipPlane = 2000f;
            go.AddComponent<CinemachineImpulseListener>();
            return cam;
        }
#endif

        // ---------- Helpers ----------

        public static Vector3 PoseOn(WaypointCircuit circuit, float distance, float lateral, out Quaternion rotation)
        {
            Vector3 dir = circuit.GetDirection(distance);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            rotation = Quaternion.LookRotation(dir, Vector3.up);
            return circuit.GetPoint(distance) + right * lateral + Vector3.up * 0.3f;
        }

        public static GameObject VisualBox(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static Material Mat(string name, Color color, float metallic, float smoothness, Color? emission = null)
        {
            if (MatCache.TryGetValue(name, out Material cached) && cached != null) return cached;

            CarRoster.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            MatCache[name] = mat;
            return mat;
        }

        public static Material ParticleMat(string name, Color color)
        {
            string key = "P_" + name;
            if (MatCache.TryGetValue(key, out Material cached) && cached != null) return cached;

            CarRoster.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/M_{key}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", SoftDotTexture());
            // Прозрачный аддитивный режим URP Particles.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            MatCache[key] = mat;
            return mat;
        }

        private static Texture2D SoftDotTexture()
        {
            string path = $"{MaterialsFolder}/T_SoftDot.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;

            const int size = 64;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void AddSceneToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path || s.path.EndsWith("SampleScene.unity"));
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
