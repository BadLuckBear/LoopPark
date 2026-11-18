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
        const string PrefabPath = "Assets/LoopPark/Resources/Model/CarModel.prefab";
        const string SkinPath = "Assets/LoopPark/Resources/LoopParkSkin.asset";

        /// <summary>车漆材质名关键字（名字包含即上色）。</summary>
        static readonly string[] PaintKeywords = { "base" };

        [MenuItem("LoopPark/配置 3D 车模型 (car.fbx → CarModel.prefab)")]
        public static void Setup()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null) { Debug.LogError("[LoopPark] 找不到 " + FbxPath); return; }
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

        static float SubmeshZ(Mesh mesh, Material[] mats, string key)
        {
            for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                if (mats[i] != null && mats[i].name.ToLower().Contains(key))
                    return mesh.GetSubMesh(i).bounds.center.z;
            return float.NaN;
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
                    go.transform.localScale = new Vector3(config.carModelScale.x, config.carModelScale.y, 1) * 0.65f;
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
