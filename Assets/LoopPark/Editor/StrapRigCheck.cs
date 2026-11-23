using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    /// <summary>
    /// 绑带接管（美术弯头 + 程序化套管）的验证工具。美术换件后重跑一次即可。
    ///
    /// 用实测数字验四件事，不靠肉眼：
    ///   1) 弯头转到任意方向后竖脚仍咬在吸盘孔上 —— 弯头顶点到吸盘轴心（socket）的最近距离应恒定；
    ///   2) 直口确实指向搭档 —— 直口水平径向 与「本车吸盘→对方吸盘」方向的点积应 ≈ 1；
    ///   3) 两段套管的中点重合、共线 —— 各自末端顶点环中心与双方直口中点之差 ≈ 0、方向叉积 ≈ 0；
    ///   4) 口径一致 —— 套管半径与美术直口口径的比例符合 hoseRadiusScale。
    /// 再出三张离屏预览：整车、接缝特写、管口特写。
    /// </summary>
    public static class StrapRigCheck
    {
        const string PrefabPath = "Assets/LoopPark/Resources/Model/CarModel.prefab";
        const string TuningPath = "Assets/LoopPark/Resources/Model/StrapTuning.asset";
        const string SkinPath = "Assets/LoopPark/Resources/LoopParkSkin.asset";
        const string OutPath = "Logs/strap_rig.txt";
        const string ImagePath = "Logs/strap_rig.png";

        /// <summary>
        /// 把绑带调参写进预制体。为什么需要这一步：Unity 会在字段新增时把「当时的默认值」烘进已有预制体，
        /// 之后改代码里的默认值对这份预制体不再生效（实测 hoseRadiusScale 一直停在旧值 0.97，
        /// 连续改了五版参数都「没效果」）。改默认值时同步跑一次这个。
        /// </summary>
        [MenuItem("LoopPark/Straps/Write Params to Prefab")]
        public static void WritePrefabFields()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var strap = root.GetComponentInChildren<CarStrap>(true);
                if (strap == null) { Debug.LogError("[StrapRigCheck] No CarStrap in prefab"); return; }
                var so = new SerializedObject(strap);
                SetFloat(so, "depthBias", -0.06f);
                SetFloat(so, "elbowScale", 1f);
                SetFloat(so, "hoseRadiusScale", 1f);
                SetFloat(so, "socketInset", 0f);
                // 绑带色 = 车漆色（0 = 不提亮）：车身 car_base 用的是同一个 palette 色写属性块，
                // 只要 lift=0 两者就逐值相同。旧版这里残留 0.2，导致绑带整体比车身亮 20%（用户实测“颜色还是不一样”）。
                SetFloat(so, "hoseLift", 0f);
                // 调色资产：存在就顺手挂上（运行时调色窗口改的就是它）
                var tuning = AssetDatabase.LoadAssetAtPath<StrapTuningConfig>(TuningPath);
                if (tuning != null)
                {
                    var property = so.FindProperty("tuning");
                    if (property != null) property.objectReferenceValue = tuning;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[StrapRigCheck] Strap params written to " + PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>创建调色资产（若不存在）并挂到预制体的 CarStrap.tuning —— 之后 LoopPark/绑带/实时调色 窗口即可用。</summary>
        [MenuItem("LoopPark/Straps/Create Tuning Asset and Attach to Prefab")]
        public static void EnsureTuningAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<StrapTuningConfig>(TuningPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<StrapTuningConfig>();
                AssetDatabase.CreateAsset(config, TuningPath);
                AssetDatabase.SaveAssets();
            }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var strap = root.GetComponentInChildren<CarStrap>(true);
                if (strap == null) { Debug.LogError("[StrapRigCheck] No CarStrap in prefab"); return; }
                var so = new SerializedObject(strap);
                var property = so.FindProperty("tuning");
                property.objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[StrapRigCheck] Tuning asset " + TuningPath + " created and attached to prefab");
        }

        /// <summary>
        /// 清理所有「本工具生成、但没有回收」的对象：验证用测试车（CarModel(Clone)）、套管（StrapSleeve）、
        /// 弯头轴心（ElbowSwivel）。
        ///
        /// 两个坑（都实测踩过）：
        /// ① 这些对象带 `HideFlags.HideAndDontSave`，`Scene.GetRootGameObjects()` 会跳过它们 —— 按场景根遍历永远清不到；
        ///    只有 `Resources.FindObjectsOfTypeAll` 看得见。
        /// ② 预览渲染（PreviewRenderUtility）里的车 `scene.IsValid()` 为 false，按「只清场景物体」的过滤也会漏掉。
        /// 所以这里**按名字 + DontSave** 精确匹配，绝不按「凡是 DontSave 就删」——
        /// 否则会误删 Scene 视图自己的光照场景（CustomLightsScene-SceneView*，删掉也能自愈，但没必要动它），
        /// 以及必须保留的预制体资产（用 PrefabUtility.IsPartOfPrefabAsset 兜底排除）。
        /// </summary>
        [MenuItem("LoopPark/Straps/Clean Scene Leftovers (Tool-Generated)")]
        public static void CleanupLeftovers()
        {
            var sb = new StringBuilder();
            int removed = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.transform.parent != null) continue;      // 只处理根节点
                if ((go.hideFlags & HideFlags.DontSave) == 0) continue;        // 工具/运行时生成物才带这个
                if (PrefabUtility.IsPartOfPrefabAsset(go)) continue;           // 别碰预制体资产
                string n = go.name;
                if (n != "StrapSleeve" && n != "ElbowSwivel" && n != "CarView(Test)" && !n.StartsWith("CarModel(Clone)")) continue;
                sb.AppendLine(n + " hideFlags=" + go.hideFlags
                    + " scene=" + (go.scene.IsValid() ? go.scene.name : "<preview/asset>")
                    + " children=" + CountChildren(go));
                Object.DestroyImmediate(go);
                removed++;
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/strap_leftovers.txt", "removed=" + removed + System.Environment.NewLine + sb);
            Debug.Log("[StrapRigCheck] Cleaned up " + removed + " leftovers, list in Logs/strap_leftovers.txt");
        }

        static int CountChildren(GameObject root)
        {
            int count = 0;
            foreach (var _ in root.GetComponentsInChildren<Transform>(true)) count++;
            return count;
        }

        /// <summary>诊断：把内存里所有「车 / 绑带相关」对象列出来（含 HideAndDontSave、含子节点、含预览场景），
        /// 并把每根套管的 shader / 属性块 _Color / _CarDepth / renderer.enabled 打出来，
        /// 写到 Logs/strap_objects.txt（Play 模式下跑一次就是游戏内真值）。</summary>
        [MenuItem("LoopPark/Straps/List Cars and Straps (Diagnostics)")]
        public static void DumpCarObjects()
        {
            var sb = new StringBuilder();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                string n = go.name;
                bool interesting = n.StartsWith("CarModel") || n.StartsWith("Strap") || n.StartsWith("Elbow")
                                   || n.StartsWith("Car") || n.StartsWith("SceneLight")
                                   || go.GetComponent<CarSkin3D>() != null || go.GetComponent<CarStrap>() != null;
                if (!interesting) continue;
                sb.AppendLine("name=" + n
                    + " hideFlags=" + go.hideFlags
                    + " scene=" + (go.scene.IsValid() ? go.scene.name : "<asset/preview>")
                    + " sceneIsDirty=" + (go.scene.IsValid() ? go.scene.isDirty.ToString() : "-")
                    + " parent=" + (go.transform.parent != null ? go.transform.parent.name : "<root>")
                    + " active=" + go.activeInHierarchy);
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/strap_objects.txt", sb.ToString());

            // 游戏内真值：每根套管的材质/shader/属性块，以及渲染器是否启用。
            // 用来判断「套管发暗」到底是 shader 不对、属性块没写进去，还是渲染器被关/被别的物体盖住。
            var hose = new StringBuilder();
            foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                if (renderer == null || !renderer.name.StartsWith("StrapSleeve")) continue;
                var material = renderer.sharedMaterial;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                hose.AppendLine("name=" + renderer.name
                    + " enabled=" + renderer.enabled
                    + " activeInHierarchy=" + renderer.gameObject.activeInHierarchy
                    + " shader=" + (material != null && material.shader != null ? material.shader.name : "<null>")
                    + " matColor=" + (material != null && material.HasProperty("_Color") ? material.GetColor("_Color").ToString() : "-")
                    + " mpbColor=" + block.GetColor("_Color")
                    + " mpbDepth=" + block.GetFloat("_CarDepth")
                    + " bounds=" + renderer.bounds);
            }
            if (hose.Length == 0) hose.AppendLine("<no StrapSleeve renderer found>");
            File.WriteAllText("Logs/strap_hose.txt", hose.ToString());
            Debug.Log("[StrapRigCheck] Sleeve renderer state written to Logs/strap_hose.txt");
            Debug.Log("[StrapRigCheck] Car/strap object list written to Logs/strap_objects.txt");
        }

        /// <summary>诊断：把当前场景所有根节点的名字 / hideFlags / 子节点数 / 是否预制体实例导到 Logs/scene_roots.txt。</summary>
        [MenuItem("LoopPark/Straps/List Scene Roots (Diagnostics)")]
        public static void DumpSceneRoots()
        {
            var sb = new StringBuilder();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            sb.AppendLine("activeScene=" + scene.name + " path=" + scene.path + " isDirty=" + scene.isDirty
                + " rootCount=" + scene.rootCount);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) { sb.AppendLine("  [null]"); continue; }
                sb.AppendLine("  name=" + root.name
                    + " hideFlags=" + root.hideFlags
                    + " children=" + CountChildren(root)
                    + " active=" + root.activeSelf
                    + " components=" + string.Join("/", System.Linq.Enumerable.Select(root.GetComponents<Component>(), c => c == null ? "null" : c.GetType().Name)));
            }
            File.WriteAllText("Logs/scene_roots.txt", sb.ToString());
            Debug.Log("[StrapRigCheck] Root node list written to Logs/scene_roots.txt");
        }

        [MenuItem("LoopPark/Straps/Verify Takeover (Values + Preview Image)")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(SkinPath);
            if (prefab == null || config == null) { Debug.LogError("[StrapRigCheck] Missing prefab/SkinConfig"); return; }

            CleanupLeftovers();   // 先自清：上次跑挂了留下的测试车/套管不要带进这次

            var log = new StringBuilder();
            var a = Spawn(prefab, config, new Vector3(-0.45f, 0f, 0f), 0);
            var b = Spawn(prefab, config, new Vector3(0.45f, 0f, 0f), 3);
            try
            {
                var strapA = a.GetComponentInChildren<CarStrap>(true);
                var strapB = b.GetComponentInChildren<CarStrap>(true);
                if (strapA == null || strapB == null) { Debug.LogError("[StrapRigCheck] No CarStrap on prefab"); return; }

                strapA.SetLinked(true, strapB);
                strapB.SetLinked(true, strapA);
                log.AppendLine("# both-side fields (prefab values, not code defaults)" + FieldDump(strapA));
                log.AppendLine("# shader actually used by sleeve = " + SleeveShader(strapA));
                log.AppendLine("# per heading (deg = partner direction relative to this car suction cup)");

                // 绕 A 车吸盘转一圈：既有正对车道的水平方向，也有斜向，覆盖转向的两个极端
                float[] degrees = { 0f, 45f, 90f, 135f, 180f, 250f };
                foreach (float degree in degrees)
                {
                    float rad = degree * Mathf.Deg2Rad;
                    Vector3 socketA = strapA.SocketWorldPosition;
                    b.transform.position = socketA + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * 0.9f;
                    strapA.SetLinked(true, strapB);
                    strapB.SetLinked(true, strapA);
                    log.AppendLine(Describe(strapA, strapB, degree));
                }

                // 正对（车道横向）摆一对用于出图
                b.transform.position = strapA.SocketWorldPosition + new Vector3(0.9f, 0f, 0f);
                strapA.SetLinked(true, strapB);
                strapB.SetLinked(true, strapA);

                // 颜色硬验证：
                //   同色配对 → 套管必须与两端弯头完全同色（这就是“颜色要一样”的验收口径）；
                //   异色配对 → 两端套管仍必须同色（整根管一个颜色，不会半蓝半灰）。
                var skinB = b.GetComponent<CarSkin3D>();
                skinB.SetCarColor(Palette.Get(0), false, 0);
                strapA.SetLinked(true, strapB);
                strapB.SetLinked(true, strapA);
                log.AppendLine("# colors (same-color pair 0/0) " + ColorReport(strapA, strapB));

                skinB.SetCarColor(Palette.Get(3), false, 3);
                strapA.SetLinked(true, strapB);
                strapB.SetLinked(true, strapA);
                log.AppendLine("# colors (different-color pair 0/3) " + ColorReport(strapA, strapB));

                Directory.CreateDirectory("Logs");
                File.WriteAllText(OutPath, log.ToString());
                Debug.Log("[StrapRigCheck] " + Path.GetFullPath(OutPath));

                Shoot(prefab, config, a.transform.position, b.transform.position, Mid(a, b), 1.6f, ImagePath);
                Shoot(prefab, config, a.transform.position, b.transform.position, Mid(a, b), 0.42f, "Logs/strap_rig_joint.png");
                const string mouthPath = "Logs/strap_rig_mouth.png";
                Shoot(prefab, config, a.transform.position, b.transform.position, strapA.MuzzleWorldPosition, 0.30f, mouthPath);
                Shoot(prefab, config, a.transform.position, b.transform.position, Mid(a, b), 0.42f, "Logs/strap_lighting.png", hideElbows: true);
                Debug.Log("[StrapRigCheck] Preview images: Logs/strap_rig.png / strap_rig_joint.png / strap_rig_mouth.png");
            }
            finally
            {
                KillTestCar(a);
                KillTestCar(b);
            }
        }

        static Vector3 Mid(GameObject a, GameObject b) =>
            (a.GetComponentInChildren<CarStrap>(true).MuzzleWorldPosition + b.GetComponentInChildren<CarStrap>(true).MuzzleWorldPosition) * 0.5f;

        static string FieldDump(CarStrap strap)
        {
            var so = new SerializedObject(strap);
            var sb = new StringBuilder(" ");
            foreach (var name in new[] { "depthBias", "elbowScale", "hoseRadiusScale", "socketInset", "hoseLift" })
            {
                var property = so.FindProperty(name);
                sb.Append(name + "=" + (property != null ? property.floatValue.ToString("F3", CultureInfo.InvariantCulture) : "?") + " ");
            }
            return sb.ToString();
        }

        /// <summary>把每根可见套管「当前实际颜色」写成调色资产的逐根默认值（Play 中跑一次即可）。</summary>
        [MenuItem("LoopPark/Straps/Write Each Sleeve Color to Asset (Generate Defaults)")]
        public static void CaptureCurrentHoseColors()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<StrapTuningConfig>(TuningPath);
            if (tuning == null) { Debug.LogError("[StrapRigCheck] Missing " + TuningPath); return; }
            var straps = Object.FindObjectsByType<CarStrap>(FindObjectsSortMode.None);
            var list = new List<CarStrap>(straps);
            var log = new StringBuilder();
            int count = CaptureHoseColors(list, tuning, log);
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssets();
            Debug.Log("[StrapRigCheck] Wrote " + count + " sleeves current colors written to " + TuningPath + "\n" + log);
        }

        /// <summary>逐根读取渲染器属性块里的 _Color，写进资产的 hoses 列表（key = 这一根的唯一编号）。</summary>
        static int CaptureHoseColors(List<CarStrap> straps, StrapTuningConfig tuning, StringBuilder log)
        {
            int count = 0;
            foreach (var strap in straps)
            {
                if (strap == null) continue;
                var hose = strap.Hose;
                if (hose == null) continue;
                var renderer = hose.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                Color current = block.GetColor("_Color");
                string key = strap.HoseKey;
                tuning.SetHoseColor(key, current);
                log?.AppendLine("  " + key + " = " + current.ToString("F3"));
                count++;
            }
            return count;
        }

        /// <summary>验证「每根生成的套管各自可调」：只给其中一根指定颜色，另一根必须保持车漆色。</summary>
        [MenuItem("LoopPark/Straps/Verify Single Sleeve Tint")]
        public static void VerifyPerHose()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(SkinPath);
            var tuning = AssetDatabase.LoadAssetAtPath<StrapTuningConfig>(TuningPath);
            if (prefab == null || config == null || tuning == null)
            {
                Debug.LogError("[StrapRigCheck] Missing prefab / SkinConfig / StrapTuning.asset");
                return;
            }

            CleanupLeftovers();
            var log = new StringBuilder();
            GameObject a = null, b = null;
            try
            {
                a = Spawn(prefab, config, new Vector3(-0.45f, 0f, 0f), 0, 101);
                b = Spawn(prefab, config, new Vector3(0.45f, 0f, 0f), 3, 101);
                var sa = a.GetComponentInChildren<CarStrap>(true);
                var sb = b.GetComponentInChildren<CarStrap>(true);
                // 两根各自一个编号（真实流程里由 GameController 按「车道_序号」写）
                sa.SetHoseId("L0_0");
                sb.SetHoseId("L1_0");

                // ① 只给 L1_0 那根指定洋红：A 必须保持车漆色
                tuning.ClearHoseColor("L0_0");
                tuning.SetHoseColor("L1_0", Color.magenta);
                sa.SetLinked(true, sb);
                sb.SetLinked(true, sa);
                log.AppendLine("1) Only L1_0 set to magenta -> " + ColorReport(sa, sb));

                // 关键口径：调色只改「代码生成的套管」，美术吸盘/弯头必须保持车漆色
                Color elbowB = ElbowColor(sb), sleeveB = SleeveColor(sb, true), sleeveA = SleeveColor(sa, true);
                log.AppendLine("   SleeveB=magenta:" + (SameColor(sleeveB, Color.magenta) ? "PASS" : "FAIL")
                               + " ElbowB=car paint (not tinted):" + (SameColor(elbowB, Palette.Get(3)) ? "PASS" : "FAIL")
                               + " SleeveA=car paint (not tinted):" + (SameColor(sleeveA, Palette.Get(0)) ? "PASS" : "FAIL"));

                // ② 把「当前颜色」写成逐根默认值：两根都应各自记下现在的颜色
                var list = new List<CarStrap> { sa, sb };
                tuning.ClearHoseColor("L0_0");
                tuning.ClearHoseColor("L1_0");
                int captured = CaptureHoseColors(list, tuning, log);
                log.AppendLine("2) Default entries written = " + captured
                               + "(L0_0 exists=" + tuning.TryGetHoseColor("L0_0", out Color c0) + "=" + c0.ToString("F3")
                               + ", L1_0 exists=" + tuning.TryGetHoseColor("L1_0", out Color c1) + "=" + c1.ToString("F3") + "）");
            }
            finally
            {
                tuning.ClearHoseColor("L0_0");   // 测试数据不留在资产里
                tuning.ClearHoseColor("L1_0");
                EditorUtility.SetDirty(tuning);
                KillTestCar(a);
                KillTestCar(b);
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/strap_hose_color.txt", log.ToString());
            Debug.Log("[StrapRigCheck] Single-sleeve tint verification written to Logs/strap_hose_color.txt\n" + log);
        }

        /// <summary>生成一辆车（姿态与预览一致：同款缩放、同款俯仰）。</summary>
        static GameObject Spawn(GameObject prefab, SkinConfig config, Vector3 position, int color)
            => Spawn(prefab, config, position, color, 0);

        /// <summary>生成一辆车，并在上色之前写好 link（管子标识）。
        /// 层级必须模拟真实结构 CarView → 预制体：CarStrap 是向上找 CarView 取 link 当「管子钥匙」的，
        /// 直接把预制体放在空场景根上会让钥匙退化成 "unlinked"，单根调色就测不出来。</summary>
        static GameObject Spawn(GameObject prefab, SkinConfig config, Vector3 position, int color, int link)
        {
            var holder = new GameObject("CarView(Test)");
            holder.hideFlags = HideFlags.HideAndDontSave;
            var view = holder.AddComponent<CarView>();
            view.link = link;
            view.enabled = false;   // 编辑模式下不需要它的 OnEnable 逻辑（订阅外观变更）
            var go = (GameObject)Object.Instantiate(prefab, holder.transform);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = position;
            go.transform.rotation = Quaternion.AngleAxis(config.carViewTilt, Vector3.right);
            go.transform.localScale = config.carModelScale * 0.65f;
            go.GetComponent<CarSkin3D>().SetCarColor(Palette.Get(color), false, color);
            return go;
        }

        /// <summary>销毁一辆测试车（连同临时的 CarView 根）。</summary>
        static void KillTestCar(GameObject car)
        {
            if (car == null) return;
            Object.DestroyImmediate(car.transform.root.gameObject);
        }

        static string Describe(CarStrap a, CarStrap b, float degree)
        {
            Vector3 socketA = a.SocketWorldPosition;
            Vector3 socketB = b.SocketWorldPosition;
            Vector3 muzzleA = a.MuzzleWorldPosition;
            Vector3 muzzleB = b.MuzzleWorldPosition;
            Vector3 mid = (muzzleA + muzzleB) * 0.5f;

            // 1) 竖脚是否还咬在吸盘孔上
            float legGap = ElbowMinDistance(a, socketA);
            // 2) 直口是否指向对方
            Vector3 radial = Flat(muzzleA - socketA);
            Vector3 want = Flat(socketB - socketA);
            float aim = want.sqrMagnitude > 1e-8f ? Vector3.Dot(radial.normalized, want.normalized) : float.NaN;
            // 3) 两半是否中点重合、共线
            Vector3 startA, endA, startB, endB;
            ReadSleeve(a, out startA, out endA);
            ReadSleeve(b, out startB, out endB);
            float gapA = Vector3.Distance(endA, mid);
            float gapB = Vector3.Distance(endB, mid);
            float cross = Vector3.Cross(Flat(endA - startA).normalized, Flat(endB - startB).normalized).magnitude;
            // 4) 口径：套管半径 / 直口口径
            float radius = Radius(a);
            float ratio = a.MouthRadiusWorld > 1e-5f ? radius / a.MouthRadiusWorld : float.NaN;

            return string.Format(CultureInfo.InvariantCulture,
                "deg={0,5:F0} aimDot={1,7:F4} legGap={2,7:F4} midGapA={3,7:F5} midGapB={4,7:F5} halvesCross={5,7:F5} radius={6:F4} radiusRatio={7:F3} sleeveLen={8:F4}",
                degree, aim, legGap, gapA, gapB, cross, radius, ratio, Vector3.Distance(startA, endA));
        }

        static float Radius(CarStrap strap)
        {
            var mesh = SleeveMesh(strap);
            if (mesh == null) return float.NaN;
            var verts = mesh.vertices;
            int sides = verts.Length / 2;
            Vector3 c0 = Vector3.zero, c1 = Vector3.zero;
            for (int i = 0; i < sides; i++) { c0 += verts[i]; c1 += verts[sides + i]; }
            c0 /= sides; c1 /= sides;
            Vector3 axis = (c1 - c0).normalized;
            return Vector3.ProjectOnPlane(verts[0] - c0, axis).magnitude;
        }

        static void ReadSleeve(CarStrap strap, out Vector3 start, out Vector3 end)
        {
            start = end = Vector3.zero;
            var mesh = SleeveMesh(strap);
            if (mesh == null) return;
            var verts = mesh.vertices;
            int sides = verts.Length / 2;
            for (int i = 0; i < sides; i++) { start += verts[i]; end += verts[sides + i]; }
            start /= sides;
            end /= sides;
        }

        static Mesh SleeveMesh(CarStrap strap)
        {
            var hose = strap.Hose;
            if (hose == null) return null;
            var filter = hose.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>套管实际绑的 shader + 关键参数：确认它是从 Strap.mat 派生的（参数一致）、且走双面套管 shader。</summary>
        static string SleeveShader(CarStrap strap)
        {
            var hose = strap.Hose;
            if (hose == null) return "<no sleeve>";
            var renderer = hose.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) return "<no material>";
            var m = renderer.sharedMaterial;
            if (m.shader == null) return "<shader is null>";
            return m.shader.name
                   + " gloss=" + m.GetFloat("_Glossiness").ToString("F2")
                   + " spec=" + m.GetColor("_SpecColor")
                   + " mainTex=" + (m.GetTexture("_MainTex") != null ? "Yes" : "Empty")
                   + " bump=" + (m.GetTexture("_BumpMap") != null ? "Yes" : "Empty");
        }

        /// <summary>套管渲染器属性块里的 _Color（start=true）或 _ColorEnd（start=false）。</summary>
        static Color SleeveColor(CarStrap strap, bool start)
        {
            var hose = strap.Hose;
            if (hose == null) return new Color(-1f, -1f, -1f, -1f);
            var renderer = hose.GetComponent<MeshRenderer>();
            if (renderer == null) return new Color(-1f, -1f, -1f, -1f);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);
            return block.GetColor(start ? "_Color" : "_ColorEnd");
        }

        /// <summary>弯头（美术件）渲染器的 _Color：与车漆同色时说明绑带和车身一致。</summary>
        static Color ElbowColor(CarStrap strap)
        {
            foreach (var renderer in strap.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null || !renderer.name.Contains("shengzi")) continue;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                return block.GetColor("_Color");
            }
            return new Color(-1f, -1f, -1f, -1f);
        }

        /// <summary>颜色近似相等（属性块存的是 float，直接 == 不可靠）。</summary>
        static bool SameColor(Color x, Color y) =>
            Mathf.Abs(x.r - y.r) < 1e-3f && Mathf.Abs(x.g - y.g) < 1e-3f && Mathf.Abs(x.b - y.b) < 1e-3f;

        static string ColorReport(CarStrap a, CarStrap b)
        {
            Color elbowA = ElbowColor(a), elbowB = ElbowColor(b);
            Color aStart = SleeveColor(a, true), aEnd = SleeveColor(a, false);
            Color bStart = SleeveColor(b, true), bEnd = SleeveColor(b, false);
            bool Same(Color x, Color y) =>
                Mathf.Abs(x.r - y.r) < 1e-3f && Mathf.Abs(x.g - y.g) < 1e-3f && Mathf.Abs(x.b - y.b) < 1e-3f;
            return "ElbowA=" + elbowA.ToString("F3") + " ElbowB=" + elbowB.ToString("F3")
                   + " | SleeveA=" + aStart.ToString("F3") + " SleeveB=" + bStart.ToString("F3")
                   + " | Hose single-color (start=end):" + (Same(aStart, aEnd) && Same(bStart, bEnd) ? "PASS" : "FAIL")
                   + " each sleeve independent:" + (!Same(aStart, bStart) ? "PASS (two sleeves differ)" : "Same color");
        }

        /// <summary>弯头顶点到吸盘轴心的最近距离：等于脚半径说明竖脚仍插在吸盘孔里。</summary>
        static float ElbowMinDistance(CarStrap strap, Vector3 point)
        {
            float best = float.MaxValue;
            foreach (var renderer in strap.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.name.ToLowerInvariant().Contains("shengzi")) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                foreach (var v in filter.sharedMesh.vertices)
                    best = Mathf.Min(best, Vector3.Distance(renderer.transform.TransformPoint(v), point));
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, v.y, 0f);

        /// <summary>离屏预览。每张图都从预制体新建实例：量件必须在静息姿态下做，且预览只接受场景根物体。</summary>
        static void Shoot(GameObject prefab, SkinConfig config, Vector3 posA, Vector3 posB, Vector3 focus, float size, string path, bool hideElbows = false)
        {
            var preview = new PreviewRenderUtility();
            Texture2D image = null;
            GameObject ga = null, gb = null;
            try
            {
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = size;
                preview.camera.transform.position = new Vector3(focus.x, focus.y, -10f);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.36f, 0.37f, 0.52f);
                preview.lights[0].intensity = config.carKeyLightIntensity;
                var direction = config.carKeyLightDirection;
                preview.lights[0].transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward);
                preview.lights[1].intensity = 0f;
                preview.ambientColor = config.carAmbientLight;

                ga = Spawn(prefab, config, posA, 0);
                gb = Spawn(prefab, config, posB, 3);
                var sa = ga.GetComponentInChildren<CarStrap>(true);
                var sb = gb.GetComponentInChildren<CarStrap>(true);
                sa.SetLinked(true, sb);
                sb.SetLinked(true, sa);
                if (hideElbows)
                {
                    // 只留套管（车体、吸盘、弯头全关）：这样测到的平均亮度就是套管本身的受光情况，
                    // 不会被车身的高光/白漆稀释。
                    foreach (var r in ga.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
                    foreach (var r in gb.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;
                }
                preview.AddSingleGO(ga);
                preview.AddSingleGO(gb);
                if (sa.Hose != null) preview.AddSingleGO(sa.Hose.gameObject);
                if (sb.Hose != null) preview.AddSingleGO(sb.Hose.gameObject);

                preview.BeginStaticPreview(new Rect(0, 0, 1000, 700));
                preview.Render();
                image = preview.EndStaticPreview();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                KillTestCar(ga);
                KillTestCar(gb);
                preview.Cleanup();
            }
        }

        static void SetFloat(SerializedObject so, string name, float value)
        {
            var property = so.FindProperty(name);
            if (property != null) property.floatValue = value;
        }
    }
}
