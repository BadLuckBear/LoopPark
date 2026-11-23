using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 入口：场景里只需要一个挂了 Bootstrap 的物体，其余全部运行时创建。
    /// 命令行（测试用）：
    ///   -lp_level N      直接进入第 N 关
    ///   -lp_autoplay     自动玩
    ///   -lp_shots DIR    每隔 -lp_shotEvery 秒截图到 DIR
    ///   -lp_quit SEC     SEC 秒后退出（或胜负后 3 秒退出）
    ///   -lp_reset        清空存档
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        // Designer control: 1.0 is the previous board size; 1.25 matches the larger reference framing.
        [SerializeField, Range(0.9f, 1.4f)] float boardScale = 1f;
        // Designer-assigned effect played when a matched car disappears.
        [SerializeField] GameObject carClearFx;

        GameController game;
        UIManager ui;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cgo.tag = "MainCamera";
                cam = cgo.GetComponent<Camera>();
            }
            cam.orthographic = true;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkinConfig.Instance.background;
            FitCamera(cam);

            // 3D 车模型用的灯光（对 2D 精灵无影响）
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = SkinConfig.Instance.carKeyLightIntensity;
            light.shadows = LightShadows.None;
            var direction = SkinConfig.Instance.carKeyLightDirection;
            light.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = SkinConfig.Instance.carAmbientLight;

            Sfx.Init(gameObject);
            SdkIntegration.Initialize();

            game = new GameObject("Game").AddComponent<GameController>();
            game.BoardScale = boardScale;
            game.CarClearFx = carClearFx;
            ui = new GameObject("UI").AddComponent<UIManager>();
            ui.Init(game);
        }

        public static void FitCamera(Camera cam)
        {
            // 设计宽度 10.8 单位、高度 19.2 单位（9:16）；更长的屏幕保持宽度
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            cam.orthographicSize = Mathf.Max(9.6f, 5.4f / aspect);
        }

        int lastW, lastH;

        void Update()
        {
            if (Screen.width != lastW || Screen.height != lastH)
            {
                lastW = Screen.width;
                lastH = Screen.height;
                FitCamera(Camera.main);
            }
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key)
            {
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
                return null;
            }
            bool Has(string key) => Array.IndexOf(args, key) >= 0;

            GameController.Verbose = Has("-lp_verbose");
            if (Arg("-lp_level") != null) Application.runInBackground = true; // 自动测试时窗口失焦也继续运行
            if (Has("-lp_reset")) SaveSystem.Reset();
            var shots = Arg("-lp_shots");
            float every = float.TryParse(Arg("-lp_shotEvery"), out var e) ? e : 2f;
            float quit = float.TryParse(Arg("-lp_quit"), out var q) ? q : -1f;
            if (shots != null) StartCoroutine(Shots(shots, every));
            if (quit > 0) StartCoroutine(QuitAfter(quit));

            yield return null;
            if (Has("-lp_gmtest")) { Application.runInBackground = true; StartCoroutine(ui.GMSelfTest()); }
            int lv = int.TryParse(Arg("-lp_level"), out var requested) ? requested :
                SceneFlow.LevelToPlay > 0 ? SceneFlow.LevelToPlay : SaveSystem.D.level;
            SceneFlow.LevelToPlay = 0;
            if (lv > 0)
            {
                if (Has("-lp_homeshot")) yield return new WaitForSeconds(1.5f);
                ui.StartLevel(lv);
                game.AutoPlay = Has("-lp_autoplay");
                game.Won += () => { Debug.Log($"[LoopPark] WIN level {lv}"); if (quit > 0) StartCoroutine(QuitAfter(3)); };
                game.Failed += () => { Debug.Log($"[LoopPark] FAIL level {lv}"); if (quit > 0) StartCoroutine(QuitAfter(3)); };
            }
        }

        IEnumerator Shots(string dir, float every)
        {
            Directory.CreateDirectory(dir);
            int i = 0;
            while (true)
            {
                yield return new WaitForSeconds(every);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"shot_{i++:D3}.png"));
            }
        }

        IEnumerator QuitAfter(float sec)
        {
            yield return new WaitForSeconds(sec);
            yield return new WaitForEndOfFrame();
            Application.Quit();
        }
    }
}
