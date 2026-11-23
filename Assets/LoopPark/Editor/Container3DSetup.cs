#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    /// <summary>
    /// 集装箱 3D 外壳的组装菜单（与「LoopPark/配置 3D 车模型」同风格，可重复执行）。
    /// 把 jizhuangxiang(1).fbx 整理成 Container3D.prefab 并挂到 LoopParkSkin.containerPrefab：
    ///   Container3D(ContainerView) → Fit(只缩放/居中) → VisualAxis(只旋转 -90,0,0)
    ///     → RoofPivot(polySurface40/18) / FrameRoot(其余 4 个)
    ///   + TopClipAnchor(提供裁切世界 Y) + IconAnchor/Lock(解锁提示物)
    /// 注意：已存在的材质球一律不覆盖 —— 颜色/参数由设计、美术在 Inspector 调，重跑本菜单只重建结构。
    /// </summary>
    public static class Container3DSetup
    {
        const string FbxPath = "Assets/LoopPark/Resources/Model/jizhuangxiang(1).fbx";
        const string MatDir = "Assets/LoopPark/Resources/Model/Materials/Container";
        const string PrefabPath = "Assets/LoopPark/Resources/Model/Container3D.prefab";
        const string SkinPath = "Assets/LoopPark/Resources/LoopParkSkin.asset";
        const string ShaderName = "LoopPark/Container Shell";

        // 第一版占位配色，仅在材质球缺失时写入
        static readonly Color RoofColor = new Color(0.44f, 0.50f, 0.96f);
        static readonly Color WallColor = new Color(0.28f, 0.33f, 0.70f);
        static readonly Color PillarColor = new Color(0.20f, 0.23f, 0.52f);
        static readonly Color RingColor = new Color(0.70f, 0.75f, 1f);

        [MenuItem("LoopPark/配置 3D 集装箱 (jizhuangxiang → Container3D.prefab)")]
        public static void Run()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Container3DSetup] 找不到 {ShaderName}，先让 Unity 导入 ContainerShell.shader");
                return;
            }

            EnsureFolder("Assets/LoopPark/Resources/Model", "Materials");
            EnsureFolder("Assets/LoopPark/Resources/Model/Materials", "Container");

            var roofMat = EnsureMaterial("Mat_Container_Roof", RoofColor, shader, 0.30f);
            var wallMat = EnsureMaterial("Mat_Container_Wall", WallColor, shader, 0.20f);
            var pillarMat = EnsureMaterial("Mat_Container_Pillar", PillarColor, shader, 0.22f);
            var ringMat = EnsureMaterial("Mat_Container_Ring", RingColor, shader, 0.35f);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (source == null)
            {
                Debug.LogError("[Container3DSetup] 找不到 FBX: " + FbxPath);
                return;
            }

            var root = (GameObject)Object.Instantiate(source);
            root.name = "Container3D";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // 先取出原 FBX 的子网格，再建结构（父子关系一变，childCount 就不可靠了）
            var meshes = new List<Transform>();
            for (int i = 0; i < root.transform.childCount; i++)
                meshes.Add(root.transform.GetChild(i));

            var fit = NewChild("Fit", root.transform);
            var axis = NewChild("VisualAxis", fit);
            axis.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 长轴 Z→世界Y，顶盖 +Y→相机 -Z
            var roofPivot = NewChild("RoofPivot", axis);
            var frameRoot = NewChild("FrameRoot", axis);

            int roofCount = 0, frameCount = 0;
            Transform topCap = null, bottomCap = null;
            foreach (var mesh in meshes)
            {
                bool isRoof = mesh.name == "polySurface40" || mesh.name == "polySurface18";
                mesh.SetParent(isRoof ? roofPivot : frameRoot, false);
                if (isRoof) roofCount++; else frameCount++;

                // 两端盖：实测 polySurface48 在局部 +Z（世界 +Y 顶端），polySurface43 在局部 −Z（底端）
                if (mesh.name == "polySurface48") topCap = mesh;
                else if (mesh.name == "polySurface43") bottomCap = mesh;

                var renderer = mesh.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                var src = renderer.sharedMaterials;
                var mapped = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                    mapped[i] = MapMaterial(src[i], roofMat, wallMat, pillarMat, ringMat);
                renderer.sharedMaterials = mapped;
            }

            var clipAnchor = NewChild("TopClipAnchor", root.transform);
            var iconAnchor = NewChild("IconAnchor", root.transform);
            var lockTf = NewChild("Lock", iconAnchor);
            var lockSprite = lockTf.gameObject.AddComponent<SpriteRenderer>();
            lockSprite.enabled = true;

            var animator = root.AddComponent<ContainerOpenAnimator>();
            var icon = iconAnchor.gameObject.AddComponent<ContainerColorIcon>();
            var view = root.AddComponent<ContainerView>();

            SetRef(animator, "_roofPivot", roofPivot);
            SetRef(animator, "_frameRoot", frameRoot);
            SetRef(animator, "_iconRoot", iconAnchor);
            SetRef(icon, "_lockSprite", lockSprite);
            SetRef(view, "_fitRoot", fit);
            SetRef(view, "_visualAxis", axis);
            SetRef(view, "_roofPivot", roofPivot);
            SetRef(view, "_topClipAnchor", clipAnchor);
            SetRef(view, "_topCap", topCap);
            SetRef(view, "_bottomCap", bottomCap);
            SetRef(view, "_colorIcon", icon);
            SetRef(view, "_animator", animator);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            var skin = AssetDatabase.LoadAssetAtPath<SkinConfig>(SkinPath);
            if (skin != null)
            {
                skin.containerPrefab = prefab;
                EditorUtility.SetDirty(skin);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Container3DSetup] 完成：{prefab.name}，顶盖 {roofCount} 个网格 / 外框 {frameCount} 个，" +
                      $"SkinConfig.containerPrefab = {(skin != null && skin.containerPrefab != null ? "已赋值" : "未赋值")}");
        }

        static Transform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Material MapMaterial(Material src, Material roof, Material wall, Material pillar, Material ring)
        {
            string n = src != null ? src.name.ToLowerInvariant() : "";
            if (n.Contains("ding") || n.Contains("aistandardsurface16")) return roof;
            if (n.Contains("qiangti") || n.Contains("aistandardsurface17")) return wall;
            if (n.Contains("zhuzi")) return pillar;
            if (n.Contains("huan")) return ring;
            return wall;
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[Container3DSetup] 字段 {field} 不存在于 {target.GetType().Name}");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>
        /// 只在材质球不存在时创建并写入第一版颜色；已存在的材质一律不动（颜色归设计/美术）。
        /// </summary>
        static Material EnsureMaterial(string name, Color color, Shader shader, float smoothness)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(shader) { name = name };
            mat.SetColor("_Color", color);
            mat.SetFloat("_Glossiness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_ClipEnabled", 0f);
            mat.SetFloat("_ClipTopY", 1000f);
            AssetDatabase.CreateAsset(mat, path);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
#endif
