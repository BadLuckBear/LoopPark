using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    /// <summary>把美术的车模型 (Resources/Model/car.fbx) 包装成游戏可用的 prefab，并接入 SkinConfig。</summary>
    public static class CarModelSetup
    {
        const string FbxPath = "Assets/LoopPark/Resources/Model/car.fbx";
        // 整体车衣：平时静态遮盖问号车，只有 1 个网格（省 Draw Call）。
        const string CoverFbxPath = "Assets/LoopPark/Resources/Model/buliao.fbx";
        // 拆分衣片：只在爆衣动画期间启用，逐片炸开（buliao 是整体模型，做不了这个动画）。
        const string BurstFbxPath = "Assets/LoopPark/Resources/Model/buliaoposui.fbx";
        // 绑带车车顶附件：吸盘 + 软管。与 car.fbx 使用同一坐标系（美术在同一场景里摆的），
        // 因此挂到 Body 下只做与车身相同的中心对齐，不需要额外位置/缩放补偿。
        const string StrapFbxPath = "Assets/LoopPark/Resources/Model/shengzi.fbx";
        const string PrefabPath = "Assets/LoopPark/Resources/Model/CarModel.prefab";
        const string SkinPath = "Assets/LoopPark/Resources/LoopParkSkin.asset";
        const string CoverMaterialPath = "Assets/LoopPark/Resources/Model/Materials/Buliao.mat";
        const string StrapMaterialPath = "Assets/LoopPark/Resources/Model/Materials/Strap.mat";

        // 这是美术确认过的问号车覆盖层姿态；不要再用 FBX bounds 自动推导覆盖层位置。
        static readonly Vector3 CoverPosition = new Vector3(-0.006f, 1.481f, -1.064f);
        static readonly Vector3 CoverRotation = new Vector3(-14.1f, 0.37f, -2.221f);
        static readonly Vector3 CoverScale = new Vector3(1f, 0.9f, 1f);

        /// <summary>车漆材质名关键字（名字包含即上色）。</summary>
        static readonly string[] PaintKeywords = { "base" };

        [MenuItem("LoopPark/配置 3D 车模型 (car.fbx → CarModel.prefab)")]
        public static void Setup()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) { Debug.LogError("[LoopPark] 找不到 " + FbxPath); return; }
            var coverFbx = AssetDatabase.LoadAssetAtPath<GameObject>(CoverFbxPath);
            var burstFbx = AssetDatabase.LoadAssetAtPath<GameObject>(BurstFbxPath);
            var mr = fbx.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r =>
                r.sharedMaterials.Any(m => m != null && PaintKeywords.Any(k => m.name.ToLower().Contains(k))));
            var mf = mr != null ? mr.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null || mr == null)
            {
                Debug.LogError("[LoopPark] car.fbx 没有可用的 MeshFilter/MeshRenderer，保留旧预制体");
                return;
            }
            var mesh = mf.sharedMesh;
            var mats = mr.sharedMaterials;
            var bounds = mr.bounds;
            foreach (var renderer in fbx.GetComponentsInChildren<MeshRenderer>(true)) bounds.Encapsulate(renderer.bounds);

            // 判断车头方向：车灯(chedeng) 与 尾灯(weideng) 子网格的 z 位置
            float headZ = SubmeshZ(mesh, mats, "chedeng"), tailZ = SubmeshZ(mesh, mats, "weideng");
            bool frontPlusZ = float.IsNaN(headZ) || float.IsNaN(tailZ) || headZ > tailZ;
            Debug.Log($"[LoopPark] headZ={headZ} tailZ={tailZ} frontPlusZ={frontPlusZ}");

            var paint = Enumerable.Range(0, mats.Length)
                .Where(i => mats[i] != null && PaintKeywords.Any(k => mats[i].name.ToLower().Contains(k)))
                .ToArray();
            Debug.Log("[LoopPark] paint materials: " + string.Join(",", paint.Select(i => mats[i].name)));

            float length = bounds.size.z;
            var root = new GameObject("CarModel");
            var skin = root.AddComponent<CarSkin3D>();
            var orient = new GameObject("Orient").transform;
            orient.SetParent(root.transform, false);
            orient.localRotation = Quaternion.Euler(-90, 0, 0);   // 模型 +Z(车头) → 世界 +Y；模型 +Y(车顶) → 朝向相机
            orient.localScale = Vector3.one * (1.2f / Mathf.Max(0.01f, length)); // 车长 = 1.2 个车宽单位
            var body = new GameObject("Body").transform;
            body.SetParent(orient, false);
            body.localRotation = Quaternion.Euler(0, frontPlusZ ? 0 : 180, 0);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            inst.transform.SetParent(body, false);
            inst.transform.localPosition = new Vector3(-bounds.center.x, 0, -bounds.center.z);

            // 绑带车附件（吸盘 + 软管）：默认关闭，运行时由 CarView 配对成功后打开。
            var strapFbx = AssetDatabase.LoadAssetAtPath<GameObject>(StrapFbxPath);
            GameObject strapInst = null;
            CarStrap strap = null;
            if (strapFbx != null)
            {
                var strapRoot = new GameObject("Strap").transform;
                strapRoot.SetParent(body, false);
                strapInst = (GameObject)PrefabUtility.InstantiatePrefab(strapFbx);
                strapInst.transform.SetParent(strapRoot, false);
                ResetLocalTransform(strapInst.transform);
                // shengzi.fbx 与 car.fbx 同一坐标系，沿用车身实例的中心对齐即可贴到车顶。
                strapInst.transform.localPosition = new Vector3(-bounds.center.x, 0, -bounds.center.z);
                strap = strapRoot.gameObject.AddComponent<CarStrap>();
                // 预制体默认是“普通车”：只有绑带配对后才显示，避免每辆车都带吸盘。
                strapRoot.gameObject.SetActive(false);
            }
            else
            {
                Debug.LogWarning("[LoopPark] 缺少绑带附件 " + StrapFbxPath + "，绑带车只会用车辆颜色区分");
            }
            CarRevealCover revealCover = null;
            CoverBurst coverBurst = null;
            Transform coverRoot = null;
            if (coverFbx != null)
            {
                // Cover 根节点承担美术确认过的整体姿态；子节点只用 identity，
                // 这样爆衣衣片与静态车衣共用同一个坐标系，动画前后都不会错位。
                coverRoot = new GameObject("Cover").transform;
                coverRoot.SetParent(body, false);
                coverRoot.localPosition = CoverPosition;
                coverRoot.localRotation = Quaternion.Euler(CoverRotation);
                coverRoot.localScale = CoverScale;
                revealCover = coverRoot.gameObject.AddComponent<CarRevealCover>();
                coverBurst = coverRoot.gameObject.AddComponent<CoverBurst>();

                // 静态遮盖：整体车衣，正常游玩时每辆车只有 1 个 Draw Call。
                var staticInst = (GameObject)PrefabUtility.InstantiatePrefab(coverFbx);
                staticInst.name = CoverBurst.StaticCoverNodeName;
                staticInst.transform.SetParent(coverRoot, false);
                ResetLocalTransform(staticInst.transform);

                // 爆衣：拆分衣片，默认关闭，只在 PlayReveal 的那 0.42s 内启用。
                if (burstFbx != null)
                {
                    var shardInst = (GameObject)PrefabUtility.InstantiatePrefab(burstFbx);
                    shardInst.name = CoverBurst.ShardNodeName;
                    shardInst.transform.SetParent(coverRoot, false);
                    ResetLocalTransform(shardInst.transform);
                    shardInst.SetActive(false);
                    coverBurst.shardRoot = shardInst.transform;
                }
                else
                {
                    Debug.LogWarning("[LoopPark] 缺少拆分车衣 " + BurstFbxPath + "，爆衣会退回整块车衣动画");
                }
                coverBurst.staticCover = staticInst;
                revealCover.shardBurst = coverBurst;
                // 预制体默认就是“问号车被布料盖住”的状态。
                // 这里不要在编辑器里调用 SetCovered：AddComponent 后 Awake 尚未可靠执行，
                // baseScale 还是 Vector3.zero，可能把刚生成的布料缩成不可见。
                coverRoot.gameObject.SetActive(true);
            }
            var cfg = AssetDatabase.LoadAssetAtPath<SkinConfig>(SkinPath);
            skin.appearance = cfg;
            const string materialFolder = "Assets/LoopPark/Resources/Model/Materials";
            Directory.CreateDirectory(materialFolder);
            var shader = Shader.Find("LoopPark/Layered Car");
            // 使用工程材质替代 FBX 的 HDR 橙色材质，避免导入后重新泛橙。
            if (shader == null || cfg == null)
            {
                Object.DestroyImmediate(root);
                Debug.LogError("[LoopPark] 缺少车辆 Shader 或 SkinConfig，保留旧预制体");
                return;
            }
            if (coverRoot != null)
                ConfigureCoverMaterials(coverRoot.gameObject, shader);
            if (strapInst != null)
                ConfigureStrapMaterial(strapInst, shader);
            foreach (var renderer in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var imported = renderer.sharedMaterials;
                for (int i = 0; i < imported.Length; i++)
                {
                    if (imported[i] == null) continue;
                    var name = imported[i].name;
                    var path = materialFolder + "/" + name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(shader) { name = name };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.shader = shader;
                    material.color = CarMaterialColors.Resolve(name, Color.white, cfg.GetCarAppearance(-1));
                    material.SetColor("_SpecColor", Color.white * cfg.GetCarAppearance(-1).carSpecular);
                    material.SetFloat("_Glossiness", cfg.GetCarAppearance(-1).carSmoothness);
                    material.SetColor("_EmissionColor", Color.black);
                    material.DisableKeyword("_EMISSION");
                    material.SetFloat("_GlossyReflections", 0);
                    material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
                    EditorUtility.SetDirty(material);
                    imported[i] = material;
                }
                renderer.sharedMaterials = imported;
            }

            skin.body = body;
            skin.animator = inst.GetComponentInChildren<Animator>();
            skin.revealCover = revealCover;
            skin.strap = strap;
            Debug.Assert(paint.Length > 0 && skin.appearance != null, "车漆材质或外观配置绑定失败");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            if (cfg != null)
            {
                cfg.carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                EditorUtility.SetDirty(cfg);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LoopPark] CarModel.prefab 已生成并接入 SkinConfig");
        }

        /// <summary>
        /// 把 FBX 实例摆回父节点原点：整体车衣与拆分衣片必须共用 Cover 的坐标系，
        /// 否则爆衣瞬间换成衣片会整体偏移（两个 FBX 的根节点自带 -0.01/0.19/0.02 的偏移）。
        /// </summary>
        static void ResetLocalTransform(Transform tf)
        {
            tf.localPosition = Vector3.zero;
            tf.localRotation = Quaternion.identity;
            tf.localScale = Vector3.one;
        }

        static float SubmeshZ(Mesh mesh, Material[] mats, string key)
        {
            for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                if (mats[i] != null && mats[i].name.ToLower().Contains(key))
                    return mesh.GetSubMesh(i).bounds.center.z;
            return float.NaN;
        }

        /// <summary>让布料也使用整车深度，解锁中的车辆才能稳定压过问号车。</summary>
        static void ConfigureCoverMaterials(GameObject coverInst, Shader shader)
        {
            if (coverInst == null) return;
            foreach (var renderer in coverInst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var source = renderer.sharedMaterials.FirstOrDefault(m => m != null);
                var material = AssetDatabase.LoadAssetAtPath<Material>(CoverMaterialPath);
                if (material == null)
                {
                    material = new Material(shader) { name = "Buliao" };
                    AssetDatabase.CreateAsset(material, CoverMaterialPath);
                }
                material.shader = shader;
                if (source != null)
                {
                    if (source.HasProperty("_MainTex") && material.HasProperty("_MainTex")) material.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                    if (source.HasProperty("_Color") && material.HasProperty("_Color")) material.SetColor("_Color", source.GetColor("_Color"));
                    if (source.HasProperty("_BumpMap") && material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                }
                material.SetFloat("_CarDepth", -2f);
                EditorUtility.SetDirty(material);
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            }
        }

        /// <summary>
        /// 绑带附件与车身共用车辆 Shader；共享材质只保留默认白色，具体颜色由运行时
        /// MaterialPropertyBlock 按车漆覆盖，_CarDepth 由 CarLayerDepth 逐帧写入。
        /// </summary>
        static void ConfigureStrapMaterial(GameObject strapInst, Shader shader)
        {
            if (strapInst == null) return;
            var material = AssetDatabase.LoadAssetAtPath<Material>(StrapMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "Strap" };
                AssetDatabase.CreateAsset(material, StrapMaterialPath);
            }
            material.shader = shader;
            material.color = Color.white;
            material.SetFloat("_CarDepth", -2f);
            EditorUtility.SetDirty(material);
            foreach (var renderer in strapInst.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
        }

        /// <summary>渲染一辆绑带车 + 一辆普通车到 Logs/strap_preview.png，检查附着位置与跟随车漆。</summary>
        [MenuItem("LoopPark/渲染绑带车预览")]
        public static void RenderStrapPreview()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(SkinPath);
            if (prefab == null || config == null) return;
            var preview = new PreviewRenderUtility();
            Texture2D image = null;
            try
            {
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 1.5f;
                preview.camera.transform.position = new Vector3(0, 0, -10);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.36f, 0.37f, 0.52f);
                preview.lights[0].intensity = config.carKeyLightIntensity;
                var direction = config.carKeyLightDirection;
                preview.lights[0].transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward);
                preview.lights[1].intensity = 0;
                preview.ambientColor = config.carAmbientLight;
                for (int i = 0; i < 2; i++)
                {
                    var go = Object.Instantiate(prefab);
                    go.transform.position = new Vector3(-0.85f + i * 1.7f, 0, 0);
                    go.transform.rotation = Quaternion.AngleAxis(config.carViewTilt, Vector3.right) * Quaternion.Euler(0, 0, 180);
                    go.transform.localScale = config.carModelScale * 0.65f;
                    var skin = go.GetComponent<CarSkin3D>();
                    skin.SetCarColor(Palette.Get(i * 3), false, i * 3);
                    // 左边是绑带车（显示吸盘），右边是普通车，用来对比附件只在配对车上出现。
                    skin.SetLinked(i == 0, null);
                    preview.AddSingleGO(go);
                }
                preview.BeginStaticPreview(new Rect(0, 0, 1000, 700));
                preview.Render();
                image = preview.EndStaticPreview();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/strap_preview.png", image.EncodeToPNG());
                Debug.Log("[LoopPark] strap preview saved Logs/strap_preview.png");
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                preview.Cleanup();
            }
        }

        /// <summary>渲染预览图到 Logs/car_preview.png（需要图形设备，不能加 -nographics）。</summary>
        [MenuItem("LoopPark/渲染车模型预览")]
        public static void RenderPreview()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return;
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(SkinPath);
            // 独立预览环境，不修改当前游戏场景的灯光和环境色。
            var preview = new PreviewRenderUtility();
            Texture2D image = null;
            try
            {
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 2.3f;
                preview.camera.transform.position = new Vector3(0, 0, -10);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.36f, 0.37f, 0.52f);
                preview.lights[0].intensity = config.carKeyLightIntensity;
                var direction = config.carKeyLightDirection;
                preview.lights[0].transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward);
                preview.lights[1].intensity = 0;
                preview.ambientColor = config.carAmbientLight;
                for (int i = 0; i < Palette.Count; i++)
                {
                    var go = Object.Instantiate(prefab);
                    go.transform.position = new Vector3(-2.0f + i % 5, 1.45f - i / 5 * 1.45f, 0);
                    go.transform.rotation = Quaternion.AngleAxis(config.carViewTilt, Vector3.right) * Quaternion.Euler(0, 0, 180);
                    go.transform.localScale = config.carModelScale * 0.65f;
                    go.GetComponent<CarSkin3D>().SetCarColor(Palette.Get(i), false, i);
                    preview.AddSingleGO(go);
                }
                preview.BeginStaticPreview(new Rect(0, 0, 1000, 750));
                preview.Render();
                image = preview.EndStaticPreview();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/car_preview.png", image.EncodeToPNG());
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                preview.Cleanup();
            }
            Debug.Log("[LoopPark] preview saved");
        }

        /// <summary>命令行一次完成：车模型 → 预览 → 关卡 → Windows 包。</summary>
        public static void FullRebuild()
        {
            Setup();
            RenderPreview();
            ProjectSetup.GenerateFromArgs();
            ProjectSetup.BuildWindows();
        }

        public static void SetupAndPreview()
        {
            Setup();
            RenderPreview();
        }
    }

    public sealed class CarModelPostprocessor : AssetPostprocessor
    {
        static bool queued;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (queued || !imported.Contains("Assets/LoopPark/Resources/Model/car.fbx")) return;
            queued = true;
            EditorApplication.delayCall += () =>
            {
                queued = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.LogWarning("[LoopPark] 请退出播放模式后运行“配置 3D 车模型”以更新预制体");
                    return;
                }
                CarModelSetup.Setup();
            };
        }
    }
}
