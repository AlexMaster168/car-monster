using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarMonster.EditorTools
{
    /// <summary>
    /// Точки входа для командной строки (-executeMethod) — их вызывают батники из корня репозитория.
    /// </summary>
    public static class CarMonsterBatch
    {
        private const string BuildPath = "Builds/Windows/CarMonster.exe";
        private const string ScreenshotFolder = "Docs/screenshots";

        /// <summary>Собрать ростер, префабы и сцены всех локаций.</summary>
        public static void BuildDemoScene() => DemoSceneBuilder.Build();

        [MenuItem("Car Monster/Build Windows Player", priority = 20)]
        public static void BuildWindowsMenu() => BuildWindows();

        /// <summary>Сборка Windows x64 в Builds/Windows. При ошибке процесс Unity выходит с кодом 1.</summary>
        public static void BuildWindows()
        {
            if (!File.Exists(DemoSceneBuilder.ScenePath)) DemoSceneBuilder.Build();
            ApplyPlayerSettings();

            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path)) scenes.Add(s.path);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[CarMonster] Build {summary.result}: {scenes.Count} scenes, {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>Полноэкранный режим по умолчанию, ресайз окна, Alt+Enter.</summary>
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = "Car Monster";
            PlayerSettings.companyName = "CarMonster";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.visibleInBackground = false;
            PlayerSettings.runInBackground = false;
        }

        [MenuItem("Car Monster/Capture README Screenshots", priority = 21)]
        public static void CaptureScreenshots()
        {
            Directory.CreateDirectory(ScreenshotFolder);
            if (!File.Exists(DemoSceneBuilder.ScenePath)) DemoSceneBuilder.Build();
            GameCatalogSO catalog = CarRoster.LoadCatalog();

            string[] lineup = { "R35-GT", "KNT-XV", "SF90-V8", "NVR-EV", "G66-ARM", "SVJ-V12" };
            for (int i = 0; i < catalog.locations.Length; i++)
            {
                LocationSO loc = catalog.locations[i];
                EditorSceneManager.OpenScene(LocationPresets.ScenePath(LocationPresets.All[i]), OpenSceneMode.Single);
                Transform[] grid = GridSlots();
                var spawned = new List<GameObject>();
                for (int s = 0; s < grid.Length && s < lineup.Length; s++)
                {
                    int pick = (s + i) % lineup.Length;
                    spawned.Add(Spawn(CarRoster.Load(lineup[pick]), grid[s].position, grid[s].rotation));
                }

                Camera cam = NewCamera();
                if (Camera.main != null)
                {
                    cam.clearFlags = Camera.main.clearFlags;
                    cam.backgroundColor = Camera.main.backgroundColor;
                }
                Transform p = grid[0];
                cam.fieldOfView = 55f;
                cam.transform.position = p.position - p.forward * 12f + p.right * 3.5f + Vector3.up * 4f;
                cam.transform.LookAt(p.position + p.forward * 24f + Vector3.up * 0.5f);
                Render(cam, $"loc_{loc.sceneName.Replace("Loc_", "").ToLowerInvariant()}.png");

                string shortId = loc.sceneName.Replace("Loc_", "").ToLowerInvariant();
                float extX = loc.cornerRadius + loc.sBendAmplitude;
                float extZ = loc.straightLength * 0.5f + loc.cornerRadius;
                bool fogWas = RenderSettings.fog;
                RenderSettings.fog = false;
                cam.fieldOfView = 50f;
                cam.transform.position = new Vector3(extX * 1.9f, Mathf.Max(extX, extZ) * 0.75f, -extZ * 1.15f);
                cam.transform.LookAt(new Vector3(0f, 0f, extZ * 0.05f));
                Render(cam, $"overview_{shortId}.png");
                RenderSettings.fog = fogWas;

                var circuit = Object.FindFirstObjectByType<WaypointCircuit>();
                circuit.Rebuild();
                float backStraight = loc.straightLength + Mathf.PI * loc.cornerRadius;
                if (loc.tunnel)
                {
                    float d = backStraight + loc.straightLength * 0.2f - 35f;
                    Vector3 eye = circuit.GetPoint(d) + Vector3.up * 4.5f;
                    cam.fieldOfView = 60f;
                    cam.transform.position = eye;
                    cam.transform.LookAt(circuit.GetPoint(d + 45f) + Vector3.up * 2f);
                    Render(cam, "tunnel.png");
                }
                if (loc.chasm)
                {
                    float g0 = loc.straightLength * loc.chasmAt;
                    float zc = -loc.straightLength * 0.5f + g0 + loc.chasmLength * 0.5f;
                    cam.fieldOfView = 55f;
                    cam.transform.position = new Vector3(loc.cornerRadius + 38f, 14f, zc - 55f);
                    cam.transform.LookAt(new Vector3(loc.cornerRadius, -6f, zc + 5f));
                    Render(cam, "chasm.png");
                }
                {
                    // Эстакада с виражом и отбойниками.
                    float d = loc.straightLength + Mathf.PI * loc.cornerRadius * 0.55f;
                    Vector3 op = circuit.GetPoint(d);
                    Vector3 dir = circuit.GetDirection(d);
                    Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
                    cam.fieldOfView = 55f;
                    cam.transform.position = op - dir * 45f - right * 35f + Vector3.up * 14f;
                    cam.transform.LookAt(op + Vector3.up * 2f);
                    Render(cam, $"overpass_{shortId}.png");
                }

                if (i == 0)
                {
                    GameObject hero = spawned[0];
                    foreach (string fx in new[] { "PerfectFire_0", "PerfectFire_1", "NitroFlame_0", "NitroFlame_1" })
                    {
                        Transform t = hero.transform.Find(fx);
                        if (t != null) t.GetComponent<ParticleSystem>().Simulate(0.7f, true, true);
                    }
                    Transform h = hero.transform;
                    cam.fieldOfView = 40f;
                    cam.transform.position = h.position - h.forward * 7.5f - h.right * 4f + Vector3.up * 1.6f;
                    cam.transform.LookAt(h.position - h.forward * 2.5f + Vector3.up * 0.6f);
                    Render(cam, "nitro_fx.png");
                }
                Object.DestroyImmediate(cam.gameObject);
            }

            var street = new List<CarDataSO>();
            var hyper = new List<CarDataSO>();
            var armored = new List<CarDataSO>();
            foreach (CarDataSO car in catalog.cars)
            {
                if (car.tier == CarTier.ArmoredSpecial) armored.Add(car);
                else if (car.tier == CarTier.Hyper) hyper.Add(car);
                else street.Add(car);
            }
            RosterShot(street, 5, "roster_street_race.png", 4.6f, 11f);
            RosterShot(hyper, 6, "roster_hyper.png", 4.6f, 11f);
            RosterShot(armored, 5, "armored.png", 6.2f, 12f, true);

            EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);
            Debug.Log($"[CarMonster] Screenshots saved to {Path.GetFullPath(ScreenshotFolder)}");
        }

        private static Transform[] GridSlots()
        {
            GameObject grid = GameObject.Find("StartGrid");
            var result = new Transform[grid.transform.childCount];
            for (int i = 0; i < result.Length; i++) result[i] = grid.transform.GetChild(i);
            return result;
        }

        private static GameObject Spawn(CarDataSO data, Vector3 position, Quaternion rotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(data.prefab);
            go.transform.SetPositionAndRotation(position - Vector3.up * 0.25f, rotation);
            return go;
        }

        private static Camera NewCamera()
        {
            var cam = new GameObject("ScreenshotCam").AddComponent<Camera>();
            cam.farClipPlane = 3000f;
            return cam;
        }

        private static void RosterShot(List<CarDataSO> cars, int perRow, string file, float spacing, float rowSpacing, bool armored = false)
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Light sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(40f, -150f, 0f);
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.4f;
            }

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(200f, 1f, 200f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.GetComponent<Renderer>().sharedMaterial = DemoSceneBuilder.Mat("Showroom", new Color(0.1f, 0.1f, 0.12f), 0.3f, 0.75f);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            int rows = Mathf.CeilToInt(cars.Count / (float)perRow);
            for (int i = 0; i < cars.Count; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                int inRow = Mathf.Min(perRow, cars.Count - row * perRow);
                var pos = new Vector3((col - (inRow - 1) * 0.5f) * spacing, 0f, row * -rowSpacing);
                Spawn(cars[i], pos + Vector3.up * 0.25f, Quaternion.Euler(0f, armored ? 30f : 25f, 0f));

                var label = new GameObject("Label").AddComponent<TextMesh>();
                label.font = font;
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                string price = cars[i].price > 0 ? $"{cars[i].price:N0} CR" : "FREE";
                label.text = $"{cars[i].displayName}\n<size=34>{cars[i].inspiredBy}</size>\n<size=34><color=#FFC93C>{price}</color></size>";
                label.richText = true;
                label.fontSize = 64;
                label.characterSize = armored ? 0.05f : 0.042f;
                label.anchor = TextAnchor.UpperCenter;
                label.alignment = TextAlignment.Center;
                label.color = Color.white;
                label.transform.position = pos + new Vector3(0f, 0.05f, armored ? 4.6f : 3.4f);
                label.transform.rotation = Quaternion.Euler(70f, 180f, 0f);
            }

            Camera cam = NewCamera();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            cam.fieldOfView = 40f;
            float depth = (rows - 1) * rowSpacing * 0.5f;
            float width = perRow * spacing;
            cam.transform.position = new Vector3(0f, 9f + width * 0.32f, 12f + width * 0.32f - depth);
            cam.transform.LookAt(new Vector3(0f, 0f, -depth));
            Render(cam, file);
            Render(cam, file);
        }

        private static void Render(Camera cam, string fileName, int width = 1600, int height = 900)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 8 };
            cam.targetTexture = rt;
            // Прогрев: первый рендер после открытия сцены в Unity 6 идёт с неготовыми данными инстансов.
            cam.Render();
            cam.Render();
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(Path.Combine(ScreenshotFolder, fileName), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
