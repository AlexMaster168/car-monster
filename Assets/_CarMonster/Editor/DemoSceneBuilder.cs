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
    /// Собирает играбельные сцены всех локаций из примитивов: трасса со стартовой решёткой, трамплины,
    /// чекпоинты, декор, гараж, музыка, Cinemachine-камеры, HUD. Car Monster → Build Location Scenes.
    /// </summary>
    public static class DemoSceneBuilder
    {
        private const string MaterialsFolder = CarRoster.Root + "/Materials";
        private const float RoadWidth = 22f;

        /// <summary>Основная сцена (первая в билде).</summary>
        public static string ScenePath => LocationPresets.ScenePath(LocationPresets.All[0]);

        private static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        [MenuItem("Car Monster/Build Location Scenes", priority = 2)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Build()
        {
            MatCache.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameCatalogSO catalog = CarRoster.CreateOrUpdateAll();
            GameBalanceSO balance = CarRoster.GetOrCreateBalance();

            var paths = new List<string>();
            for (int i = 0; i < catalog.locations.Length; i++)
            {
                string path = LocationPresets.ScenePath(LocationPresets.All[i]);
                BuildLocation(catalog.locations[i], catalog, balance, path);
                paths.Add(path);
            }

            EditorBuildSettings.scenes = paths.ConvertAll(p => new EditorBuildSettingsScene(p, true)).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[CarMonster] Built {paths.Count} location scenes.");
        }

        private static void BuildLocation(LocationSO loc, GameCatalogSO catalog, GameBalanceSO balance, string path)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetupLighting(loc);

            var track = new GameObject("Track");
            WaypointCircuit circuit = BuildCircuit(track.transform, loc);
            BuildEnvironment(track.transform, circuit, loc);
            Checkpoint[] gates = BuildCheckpoints(track.transform, circuit, loc);
            Transform[] grid = BuildGrid(circuit);

            CameraSpeedController cam = BuildCameras(balance);
            if (loc.solidSky && Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = loc.solidSkyColor;
            }

            var managerGo = new GameObject("TimeAttackManager");
            var manager = managerGo.AddComponent<TimeAttackManager>();
            manager.EditorSetup(balance, catalog, loc, circuit, gates, grid, cam);
            managerGo.AddComponent<RaceDebugHUD>();
            managerGo.AddComponent<RaceSetupMenu>().EditorSetup(manager);

            var music = new GameObject("Music");
            music.AddComponent<AudioSource>();
            music.AddComponent<AudioLowPassFilter>();
            music.AddComponent<MusicPlayer>().EditorSetup(loc, balance);

            CarRoster.EnsureFolder(CarRoster.Root + "/Scenes");
            EditorSceneManager.SaveScene(scene, path);
        }

        private static void SetupLighting(LocationSO loc)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = loc.fogStart;
            RenderSettings.fogEndDistance = loc.fogEnd;
            RenderSettings.fogColor = loc.fogColor;

            Material sky = SkyMat(loc);
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(loc.skyTint, Color.white, 0.3f) * 0.9f;
            RenderSettings.ambientEquatorColor = Color.Lerp(loc.fogColor, Color.gray, 0.4f) * 0.8f;
            RenderSettings.ambientGroundColor = loc.groundColor * 0.45f;

            Light sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(loc.sunEuler);
                sun.color = loc.sunColor;
                sun.intensity = loc.sunIntensity;
                sun.shadows = LightShadows.Soft;
                RenderSettings.sun = sun;
            }
        }

        // ---------- Track ----------

        private static WaypointCircuit BuildCircuit(Transform parent, LocationSO loc)
        {
            var go = new GameObject("Circuit");
            go.transform.SetParent(parent, false);
            var points = new List<Vector3>();
            float half = loc.straightLength * 0.5f;
            float r = loc.cornerRadius;
            const float step = 30f;

            for (float z = -half; z < half; z += step) points.Add(new Vector3(r, 0f, z));
            for (float a = 0f; a < 180f; a += 15f)
            {
                float rad = a * Mathf.Deg2Rad;
                points.Add(new Vector3(Mathf.Cos(rad) * r, 0f, half + Mathf.Sin(rad) * r));
            }
            for (float z = half; z > -half; z -= step)
            {
                float t = Mathf.InverseLerp(half, -half, z);
                points.Add(new Vector3(-r + Mathf.Sin(t * Mathf.PI * 2f * loc.sBendWaves) * loc.sBendAmplitude, 0f, z));
            }
            for (float a = 180f; a < 360f; a += 15f)
            {
                float rad = a * Mathf.Deg2Rad;
                points.Add(new Vector3(Mathf.Cos(rad) * r, 0f, -half + Mathf.Sin(rad) * r));
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

        private static void BuildEnvironment(Transform parent, WaypointCircuit circuit, LocationSO loc)
        {
            string id = loc.sceneName;
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(2000f, 1f, 2400f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.GetComponent<Renderer>().sharedMaterial = Mat("Ground_" + id, loc.groundColor, 0f, 0.15f);
            ground.isStatic = true;

            var road = new GameObject("Road");
            road.transform.SetParent(parent, false);
            Material roadMat = Mat("Road_" + id, loc.roadColor, 0f, 0.35f);
            Material lineMat = Mat("RoadLine", new Color(0.95f, 0.95f, 0.95f), 0f, 0.3f, Color.white * 0.3f);
            bool icy = loc.decor == DecorStyle.PortContainers;
            Material curbA = icy ? Mat("CurbBlue", new Color(0.1f, 0.35f, 0.9f), 0f, 0.3f) : Mat("CurbRed", new Color(0.85f, 0.1f, 0.1f), 0f, 0.3f);
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

            Material rampMat = Mat("Ramp", new Color(1f, 0.55f, 0.05f), 0.2f, 0.5f);
            float straight = loc.straightLength;
            float backStraight = straight + Mathf.PI * loc.cornerRadius;
            Ramp(parent, circuit, 150f, rampMat);
            Ramp(parent, circuit, straight - 120f, rampMat);
            Ramp(parent, circuit, backStraight + 60f, rampMat);

            Material block = Mat("Concrete", new Color(0.6f, 0.6f, 0.62f), 0f, 0.2f);
            Obstacle(parent, circuit, straight * 0.62f, 6f, block);
            Obstacle(parent, circuit, straight * 0.62f + 15f, -6f, block);
            Obstacle(parent, circuit, backStraight + straight * 0.62f, 0f, block);

            var deco = new GameObject("Decor");
            deco.transform.SetParent(parent, false);
            switch (loc.decor)
            {
                case DecorStyle.DesertMesas: BuildMesas(deco.transform, circuit, loc); break;
                case DecorStyle.PortContainers: BuildContainers(deco.transform, circuit, loc); break;
                default: BuildTowers(deco.transform, circuit, loc); break;
            }
        }

        private static void BuildTowers(Transform parent, WaypointCircuit circuit, LocationSO loc)
        {
            Material tower = Mat("Tower_" + loc.sceneName, loc.decorColor, 0.4f, 0.7f);
            Material glow = Mat("Glow_" + loc.sceneName, loc.glowColor, 0f, 0.5f, loc.glowColor * 2.5f);
            Material glow2 = Mat("Glow2_" + loc.sceneName, new Color(0.1f, 0.85f, 1f), 0f, 0.5f, new Color(0.1f, 0.85f, 1f) * 2.5f);
            var rng = new System.Random(35);
            for (float d = 0f; d < circuit.Length; d += 26f)
            {
                SideOf(circuit, d, (int)(d / 26f) % 2 == 0 ? 1f : -1f, 14f + (float)rng.NextDouble() * 22f, out Vector3 pos, out Quaternion rot);
                float h = 10f + (float)rng.NextDouble() * 55f;
                VisualBox(parent, "Tower", pos + Vector3.up * (h * 0.5f), rot, new Vector3(7f, h, 7f), tower).isStatic = true;
                // Неоновые полосы на фасадах.
                Material stripe = rng.NextDouble() > 0.5 ? glow : glow2;
                for (float y = 4f; y < h - 2f; y += 6f + (float)rng.NextDouble() * 6f)
                    VisualBox(parent, "Neon", pos + Vector3.up * y, rot, new Vector3(7.2f, 0.35f, 7.2f), stripe).isStatic = true;
            }
        }

        private static void BuildMesas(Transform parent, WaypointCircuit circuit, LocationSO loc)
        {
            Material rock = Mat("Mesa_" + loc.sceneName, loc.decorColor, 0f, 0.1f);
            Material rockDark = Mat("MesaDark_" + loc.sceneName, loc.decorColor * 0.75f, 0f, 0.1f);
            Material cactus = Mat("Cactus", new Color(0.25f, 0.45f, 0.2f), 0f, 0.2f);
            var rng = new System.Random(77);
            for (float d = 0f; d < circuit.Length; d += 45f)
            {
                float side = rng.NextDouble() > 0.5 ? 1f : -1f;
                SideOf(circuit, d, side, 40f + (float)rng.NextDouble() * 60f, out Vector3 pos, out _);
                float h = 12f + (float)rng.NextDouble() * 30f;
                float w = 25f + (float)rng.NextDouble() * 35f;
                Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f);
                VisualBox(parent, "Mesa", pos + Vector3.up * (h * 0.5f), yaw, new Vector3(w, h, w * 0.8f), rock).isStatic = true;
                VisualBox(parent, "MesaCap", pos + Vector3.up * (h + 1.5f), yaw, new Vector3(w * 0.85f, 3f, w * 0.7f), rockDark).isStatic = true;

                SideOf(circuit, d + 20f, -side, 16f + (float)rng.NextDouble() * 10f, out Vector3 cpos, out _);
                VisualBox(parent, "Cactus", cpos + Vector3.up * 2.5f, Quaternion.identity, new Vector3(0.8f, 5f, 0.8f), cactus).isStatic = true;
                VisualBox(parent, "CactusArm", cpos + new Vector3(0.9f, 3f, 0f), Quaternion.identity, new Vector3(1.4f, 0.6f, 0.6f), cactus).isStatic = true;
            }
        }

        private static void BuildContainers(Transform parent, WaypointCircuit circuit, LocationSO loc)
        {
            Color[] colors =
            {
                loc.decorColor, new Color(0.1f, 0.3f, 0.7f), new Color(0.95f, 0.5f, 0.1f),
                new Color(0.15f, 0.5f, 0.3f), new Color(0.5f, 0.52f, 0.55f)
            };
            var mats = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++) mats[i] = Mat($"Container_{i}", colors[i], 0.3f, 0.35f);
            Material crane = Mat("Crane", new Color(0.95f, 0.75f, 0.1f), 0.4f, 0.4f);
            Material glow = Mat("Glow_" + loc.sceneName, loc.glowColor, 0f, 0.5f, loc.glowColor * 2.5f);
            Material snow = Mat("Snow", new Color(0.95f, 0.97f, 1f), 0f, 0.3f);
            var rng = new System.Random(91);

            for (float d = 0f; d < circuit.Length; d += 14f)
            {
                float side = (int)(d / 14f) % 2 == 0 ? 1f : -1f;
                SideOf(circuit, d, side, 16f + (float)rng.NextDouble() * 6f, out Vector3 pos, out Quaternion rot);
                int stack = 1 + rng.Next(0, 3);
                for (int s = 0; s < stack; s++)
                {
                    Material m = mats[rng.Next(mats.Length)];
                    VisualBox(parent, "Container", pos + Vector3.up * (1.3f + s * 2.62f), rot, new Vector3(2.5f, 2.6f, 6f), m).isStatic = true;
                }
                VisualBox(parent, "SnowCap", pos + Vector3.up * (stack * 2.62f + 0.08f), rot, new Vector3(2.55f, 0.15f, 6.05f), snow).isStatic = true;

                if ((int)(d / 14f) % 9 == 0)
                {
                    SideOf(circuit, d, -side, 34f, out Vector3 cpos, out Quaternion crot);
                    VisualBox(parent, "CraneTower", cpos + Vector3.up * 18f, crot, new Vector3(2f, 36f, 2f), crane).isStatic = true;
                    VisualBox(parent, "CraneArm", cpos + Vector3.up * 35f + crot * Vector3.right * side * -10f, crot, new Vector3(28f, 1.5f, 1.5f), crane).isStatic = true;
                    VisualBox(parent, "CraneLight", cpos + Vector3.up * 36.5f, crot, new Vector3(1f, 1f, 1f), glow).isStatic = true;
                }
            }
        }

        private static void SideOf(WaypointCircuit circuit, float distance, float side, float offset, out Vector3 pos, out Quaternion rot)
        {
            Vector3 p = circuit.GetPoint(distance);
            Vector3 dir = circuit.GetDirection(distance);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            pos = p + right * side * (RoadWidth * 0.5f + offset);
            rot = Quaternion.LookRotation(dir);
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

        private static Checkpoint[] BuildCheckpoints(Transform parent, WaypointCircuit circuit, LocationSO loc)
        {
            var root = new GameObject("Checkpoints");
            root.transform.SetParent(parent, false);
            Material start = Mat("GateStart", Color.white, 0f, 0.4f, Color.white * 1.5f);
            Material gate = Mat("Gate_" + loc.sceneName, loc.glowColor, 0f, 0.4f, loc.glowColor * 2f);

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

        /// <summary>Стартовая решётка: [0] игрок сзади по центру, дальше 5 ботов «ёлочкой».</summary>
        private static Transform[] BuildGrid(WaypointCircuit circuit)
        {
            (float dist, float lane)[] slots = { (22f, 0f), (36f, -5f), (36f, 5f), (50f, -5f), (50f, 5f), (64f, 0f) };
            var root = new GameObject("StartGrid");
            var result = new Transform[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = new GameObject(i == 0 ? "Grid_Player" : $"Grid_Bot_{i}");
                slot.transform.SetParent(root.transform, false);
                Vector3 pos = PoseOn(circuit, slots[i].dist, slots[i].lane, out Quaternion rot);
                slot.transform.SetPositionAndRotation(pos, rot);
                result[i] = slot.transform;
            }
            return result;
        }

        private static CameraSpeedController BuildCameras(GameBalanceSO balance)
        {
            Camera main = Camera.main;
            if (main == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                main = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            main.farClipPlane = 2500f;
            main.fieldOfView = balance.cameraFx.baseFov;

            var rig = new GameObject("CameraRig");
            CameraSpeedController controller = rig.AddComponent<CameraSpeedController>();
#if CINEMACHINE_3
            CinemachineBrain brain = main.gameObject.AddComponent<CinemachineBrain>();
            brain.IgnoreTimeScale = true;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.35f);

            CinemachineCamera follow = NewCmCamera(rig.transform, "CM_Follow", balance.cameraFx.baseFov);
            var body = follow.gameObject.AddComponent<CinemachineFollow>();
            body.FollowOffset = new Vector3(0f, 2.8f, -7.6f);
            body.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LazyFollow;
            body.TrackerSettings.PositionDamping = new Vector3(0.15f, 0.25f, 0.35f);
            var aim = follow.gameObject.AddComponent<CinemachineRotationComposer>();
            aim.TargetOffset = new Vector3(0f, 1.2f, 2f);
            aim.Damping = new Vector2(0.15f, 0.15f);

            CinemachineCamera takedown = NewCmCamera(rig.transform, "CM_Takedown", 50f);
            var tdBody = takedown.gameObject.AddComponent<CinemachineFollow>();
            tdBody.FollowOffset = new Vector3(6f, 3f, -7f);
            tdBody.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            takedown.gameObject.AddComponent<CinemachineRotationComposer>();

            var impulse = rig.AddComponent<CinemachineImpulseSource>();
            controller.EditorSetup(balance, null, follow, takedown, impulse);
            takedown.gameObject.SetActive(false);
#else
            Debug.LogWarning("[CarMonster] Cinemachine 3 не найден — CameraSpeedController работает с обычной камерой.");
#endif
            return controller;
        }

#if CINEMACHINE_3
        private static CinemachineCamera NewCmCamera(Transform parent, string name, float fov)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Lens.FieldOfView = fov;
            cam.Lens.FarClipPlane = 2500f;
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

        private static Material SkyMat(LocationSO loc)
        {
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return null;
            CarRoster.EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/Sky_{loc.sceneName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_SkyTint", loc.skyTint);
            mat.SetColor("_GroundColor", loc.groundHorizon);
            mat.SetFloat("_AtmosphereThickness", loc.atmosphere);
            mat.SetFloat("_Exposure", loc.skyExposure);
            mat.SetFloat("_SunSize", 0.05f);
            EditorUtility.SetDirty(mat);
            return mat;
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
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
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
    }
}
