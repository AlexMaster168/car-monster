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

        /// <summary>Собрать демо-сцену и ростер (CI / первый запуск).</summary>
        public static void BuildDemoScene()
        {
            DemoSceneBuilder.Build();
        }

        [MenuItem("Car Monster/Build Windows Player", priority = 20)]
        public static void BuildWindowsMenu() => BuildWindows();

        /// <summary>Сборка Windows x64 в Builds/Windows. При ошибке процесс Unity выходит с кодом 1.</summary>
        public static void BuildWindows()
        {
            if (!File.Exists(DemoSceneBuilder.ScenePath)) DemoSceneBuilder.Build();

            PlayerSettings.productName = "Car Monster";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DemoSceneBuilder.ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[CarMonster] Build {summary.result}: {summary.totalSize / (1024 * 1024)} MB, {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        [MenuItem("Car Monster/Capture README Screenshots", priority = 21)]
        public static void CaptureScreenshots()
        {
            Directory.CreateDirectory(ScreenshotFolder);

            if (!File.Exists(DemoSceneBuilder.ScenePath)) DemoSceneBuilder.Build();
            EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);

            var player = GameObject.FindWithTag("Player").transform;
            var cam = new GameObject("ScreenshotCam").AddComponent<Camera>();
            cam.farClipPlane = 3000f;

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.fieldOfView = 50f;
            cam.transform.position = new Vector3(380f, 300f, -470f);
            cam.transform.LookAt(new Vector3(0f, 0f, 30f));
            // Прогревочные кадры: первый рендер после открытия сцены в Unity 6 идёт с неготовыми данными инстансов.
            Render(cam, "track_overview.png");
            Render(cam, "track_overview.png");
            Render(cam, "track_overview.png");
            RenderSettings.fog = fog;

            cam.fieldOfView = 55f;
            cam.transform.position = player.position - player.forward * 11f + player.right * 3f + Vector3.up * 3.6f;
            cam.transform.LookAt(player.position + player.forward * 22f + Vector3.up * 0.5f);
            Render(cam, "start_grid.png");

            foreach (string fx in new[] { "PerfectFire_0", "PerfectFire_1", "NitroFlame_0", "NitroFlame_1" })
            {
                Transform t = player.Find(fx);
                if (t == null) continue;
                var ps = t.GetComponent<ParticleSystem>();
                ps.Simulate(0.7f, true, true);
            }
            cam.fieldOfView = 40f;
            cam.transform.position = player.position - player.forward * 7.5f - player.right * 4f + Vector3.up * 1.6f;
            cam.transform.LookAt(player.position - player.forward * 2.5f + Vector3.up * 0.6f);
            Render(cam, "nitro_fx.png");

            Object.DestroyImmediate(cam.gameObject);
            EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);

            CaptureRoster();
            EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);
            Debug.Log($"[CarMonster] Screenshots saved to {Path.GetFullPath(ScreenshotFolder)}");
        }

        private static void CaptureRoster()
        {
            CarDataSO[] cars = CarRoster.CreateOrUpdateAll();
            GameBalanceSO balance = CarRoster.GetOrCreateBalance();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Light sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(38f, -150f, 0f);
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.4f;
            }

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(200f, 1f, 200f);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.GetComponent<Renderer>().sharedMaterial =
                DemoSceneBuilder.Mat("Showroom", new Color(0.1f, 0.1f, 0.12f), 0.3f, 0.75f);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            const int perRow = 6;
            for (int i = 0; i < cars.Length; i++)
            {
                int row = i / perRow;
                int col = i % perRow;
                var pos = new Vector3((col - (perRow - 1) * 0.5f) * 4.6f, 0.1f, row * -11f);
                CarFactory.CreateCar(cars[i], balance, row == 0 && col == 0, pos, Quaternion.Euler(0f, 25f, 0f), null);

                var label = new GameObject("Label").AddComponent<TextMesh>();
                label.font = font;
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                label.text = $"{cars[i].displayName}\n<size=40>{cars[i].inspiredBy}</size>";
                label.richText = true;
                label.fontSize = 64;
                label.characterSize = 0.045f;
                label.anchor = TextAnchor.LowerCenter;
                label.alignment = TextAlignment.Center;
                label.color = Color.white;
                // Подпись лежит перед машиной, чтобы не перекрывать ряд позади.
                label.transform.position = pos + new Vector3(0f, 0.05f, 3.4f);
                label.transform.rotation = Quaternion.Euler(70f, 180f, 0f);
            }

            var cam = new GameObject("ScreenshotCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            cam.fieldOfView = 40f;
            cam.transform.position = new Vector3(0f, 17f, 21f);
            cam.transform.LookAt(new Vector3(0f, 0f, -4f));
            Render(cam, "roster.png");
        }

        private static void Render(Camera cam, string fileName, int width = 1600, int height = 900)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 8
            };
            cam.targetTexture = rt;
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
