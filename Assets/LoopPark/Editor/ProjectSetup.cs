using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LoopPark.EditorTools
{
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/LoopPark/Scenes/Main.unity";
        internal static string[] AppScenePaths() => new[]
        {
            "Assets/LoopPark/Scenes/Loading.unity",
            "Assets/LoopPark/Scenes/Home.unity",
            "Assets/LoopPark/Scenes/Game.unity"
        };
        const string LevelDir = "Assets/LoopPark/Resources/Levels";
        const string SkinPath = "Assets/LoopPark/Resources/LoopParkSkin.asset";
        public const int LevelCount = 60;

        [MenuItem("LoopPark/1. 配置工程（场景、构建设置、皮肤）")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/LoopPark/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            var c = cam.GetComponent<Camera>();
            c.orthographic = true;
            c.orthographicSize = 9.6f;
            c.transform.position = new Vector3(0, 0, -10);
            c.clearFlags = CameraClearFlags.SolidColor;
            new GameObject("Bootstrap").AddComponent<Bootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var appScenes = AppScenePaths();
            if (System.Array.TrueForAll(appScenes, File.Exists))
                EditorBuildSettings.scenes = System.Array.ConvertAll(appScenes, path => new EditorBuildSettingsScene(path, true));
            else
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            if (AssetDatabase.LoadAssetAtPath<SkinConfig>(SkinPath) == null)
            {
                var skin = ScriptableObject.CreateInstance<SkinConfig>();
                skin.carColors = (Color[])Palette.Default.Clone();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }

            PlayerSettings.companyName = "LoopParkStudio";
            PlayerSettings.productName = "环线停车场";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.yocean.carloop");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[LoopPark] Setup done");
        }

        [MenuItem("LoopPark/2. 生成关卡（60 关，覆盖）")]
        public static void GenerateLevels() => GenerateRange(1, LevelCount);

        public const string CurvePath = "Assets/LoopPark/Editor/Data/difficulty_curve.csv";

        public static void GenerateRange(int from, int to)
        {
            if (from < 1 || to > LevelCount || from > to)
                throw new System.ArgumentOutOfRangeException(nameof(to), $"关卡生成范围必须在 1~{LevelCount} 之间");
            if (File.Exists(CurvePath)) LevelGenerator.LoadCurve(File.ReadAllText(CurvePath));
            Directory.CreateDirectory(LevelDir);
            var csv = new StringBuilder("level,difficulty,columns,capacity,lanes,cars,fences,hidden,winRate,seed\n");
            int total = to - from + 1;
            var results = new LevelData[total];
            var times = new double[total];
            int done = 0;
            var task = System.Threading.Tasks.Task.Run(() =>
                System.Threading.Tasks.Parallel.For(0, total, i =>
                {
                    var t0 = System.DateTime.Now;
                    results[i] = LevelGenerator.Generate(from + i);
                    times[i] = (System.DateTime.Now - t0).TotalSeconds;
                    System.Threading.Interlocked.Increment(ref done);
                }));
            while (!task.IsCompleted)
            {
                EditorUtility.DisplayProgressBar("生成关卡", $"{done}/{total}", done / (float)total);
                System.Threading.Thread.Sleep(200);
            }
            if (task.Exception != null) Debug.LogException(task.Exception);
            for (int i = 0; i < total; i++)
            {
                int n = from + i;
                var d = results[i];
                if (d == null) { Debug.LogError($"[LoopPark] Level {n}: 生成失败"); csv.Append($"{n},FAILED\n"); continue; }
                Debug.Log($"[LoopPark] level {n} rate {d.winRate:0.00} {times[i]:0.0}s");
                var err = d.Validate();
                if (err != null) Debug.LogError($"Level {n}: {err}");
                File.WriteAllText(Path.Combine(LevelDir, $"level_{n:D4}.json"), JsonUtility.ToJson(d, true));
                int fences = 0, hidden = 0;
                foreach (var c in d.columns) { if (c.fence) fences++; foreach (var s in c.cars) if (s.h) hidden++; }
                foreach (var l in d.lanes) foreach (var s in l.cars) if (s.h) hidden++;
                csv.Append($"{n},{d.difficulty},{d.columns.Count},{d.capacity},{d.lanes.Count},{d.TotalCars()},{fences},{hidden},{d.winRate:0.00},{d.seed}\n");
            }
            EditorUtility.ClearProgressBar();
            Directory.CreateDirectory("Docs");
            File.WriteAllText("Docs/levels_summary.csv", csv.ToString(), new UTF8Encoding(true));
            AssetDatabase.Refresh();
            Debug.Log($"[LoopPark] Generated levels {from}-{to}");
        }

        [MenuItem("LoopPark/3. 构建 Windows 测试包")]
        public static void BuildWindows()
        {
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Win/LoopPark.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[LoopPark] Windows build: " + report.summary.result + " " + report.summary.totalErrors + " errors");
        }

        [MenuItem("LoopPark/4. 构建 Android APK")]
        public static void BuildAndroid()
        {
            var scenes = AppScenePaths();
            foreach (var path in scenes)
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
                    throw new UnityEditor.Build.BuildFailedException($"Missing required Android scene: {path}");

            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/Android/LoopPark.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[LoopPark] Android build: " + report.summary.result + " " + report.summary.totalErrors + " errors");
        }

        public static void GenerateFromArgs()
        {
            var args = System.Environment.GetCommandLineArgs();
            int from = 1, to = LevelCount;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-lp_from") from = int.Parse(args[i + 1]);
                if (args[i] == "-lp_to") to = int.Parse(args[i + 1]);
            }
            GenerateRange(from, to);
        }

        /// <summary>命令行：-executeMethod LoopPark.EditorTools.ProjectSetup.BatchAll</summary>
        public static void BatchAll()
        {
            Setup();
            GenerateLevels();
        }
    }
}
