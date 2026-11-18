using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    /// <summary>直接编辑共享皮肤资源，支持播放时预览并显式保存到工程。</summary>
    public sealed class CarAppearanceWindow : EditorWindow
    {
        const string ConfigPath = "Assets/LoopPark/Resources/LoopParkSkin.asset";
        SkinConfig config;
        SerializedObject settings;
        Vector2 scroll;
        [SerializeField, Tooltip("当前正在独立调节的车色编号；最后一项为问号车。切换不会修改其他颜色。") ]
        int selectedColor;
        [SerializeField] bool showFallback;

        [MenuItem("LoopPark/车辆外观调节（支持运行中）")]
        public static void Open() => GetWindow<CarAppearanceWindow>("车辆外观");

        /// <summary>窗口打开或脚本重载后重新获取资源，避免继续使用失效的序列化对象。</summary>
        void OnEnable()
        {
            config = AssetDatabase.LoadAssetAtPath<SkinConfig>(ConfigPath);
            if (config != null)
            {
                config.EnsureCarAppearances();
                settings = new SerializedObject(config);
            }
        }

        void OnGUI()
        {
            if (settings == null) { EditorGUILayout.HelpBox("未找到 LoopParkSkin 配置", MessageType.Error); return; }
            EditorGUILayout.HelpBox("播放时调整，当前车辆立即刷新。点击保存后，重新运行和打包都会使用该配置。", MessageType.Info);
            settings.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            Field("carModelScale", "车辆缩放 X / Y");
            Field("carViewTilt", "俯视倾角");
            EditorGUILayout.Space();
            var labels = new string[Palette.Count + 1];
            for (int i = 0; i < Palette.Count; i++) labels[i] = i + "  " + Palette.Names[i];
            labels[Palette.Count] = "问号车";
            selectedColor = EditorGUILayout.Popup(new GUIContent("独立调节的车色", "仅修改所选颜色的参数，其他车色保持各自设置。"), Mathf.Clamp(selectedColor, 0, Palette.Count), labels);
            bool hidden = selectedColor == Palette.Count;
            var profile = hidden ? settings.FindProperty("hiddenCarAppearance") : settings.FindProperty("carAppearanceByColor").GetArrayElementAtIndex(selectedColor);
            EditorGUILayout.HelpBox("独立材质优先。点击材质球在 Inspector 调颜色、贴图、高光和发光；更改共享材质会立即影响使用它的车辆。请使用 LoopPark/Layered Car Shader 以保留遮挡排序。", MessageType.Info);
            MaterialField(profile, "paintMaterial", "车漆 / 后视镜");
            MaterialField(profile, "windowMaterial", "车窗");
            MaterialField(profile, "windowFrameMaterial", "窗框 / 黑色结构");
            MaterialField(profile, "trimMaterial", "饰条");
            MaterialField(profile, "frontSplitterMaterial", "前铲 / 下摆");
            MaterialField(profile, "headlightMaterial", "前灯");
            MaterialField(profile, "lightStripMaterial", "灯条");
            MaterialField(profile, "taillightMaterial", "尾灯");
            MaterialField(profile, "wheelMaterial", "轮毂");
            MaterialField(profile, "tireMaterial", "轮胎");
            showFallback = EditorGUILayout.Foldout(showFallback, "旧调色回退（仅未指定材质的部件生效）", true);
            if (showFallback)
            {
            if (hidden) Field("hiddenCarColor", "问号车车身颜色");
            else EditorGUILayout.PropertyField(settings.FindProperty("carColors").GetArrayElementAtIndex(selectedColor), new GUIContent("车身颜色", "所选编号的车漆颜色，修改后立即刷新。"));
            ProfileField(profile, "carSmoothness", "光滑度（低 = 哑光）");
            ProfileField(profile, "carSpecular", "高光强度（低 = 柔和）");
            ProfileField(profile, "carWindowColor", "车窗");
            ProfileField(profile, "carWindowFrameColor", "车窗框 / 黑色结构");
            ProfileField(profile, "carTrimColor", "车身饰条");
            ProfileField(profile, "carFrontSplitterColor", "前铲内部 / 下摆");
            ProfileField(profile, "carHeadlightColor", "前灯");
            ProfileField(profile, "carLightStripColor", "灯条");
            ProfileField(profile, "carTaillightColor", "尾灯");
            ProfileField(profile, "carWheelColor", "轮毂");
            ProfileField(profile, "carTireColor", "轮胎");
            }
            if (EditorGUI.EndChangeCheck())
            {
                // SerializedObject 保留 Undo，并通过事件刷新当前车辆。
                settings.ApplyModifiedProperties();
                config.RefreshCarAppearance();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("保存当前外观配置"))
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(config);
                foreach (var material in GetProfileMaterials(config.GetCarAppearance(hidden ? -1 : selectedColor)))
                    if (material != null) AssetDatabase.SaveAssetIfDirty(material);
            }
        }

        static void MaterialField(SerializedProperty profile, string name, string label)
        {
            var property = profile.FindPropertyRelative(name);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(property, new GUIContent(label + "材质"));
            var material = property.objectReferenceValue as Material;
            using (new EditorGUI.DisabledScope(material == null))
                if (GUILayout.Button("编辑", GUILayout.Width(42))) Selection.activeObject = material;
            EditorGUILayout.EndHorizontal();
            if (material != null && material.shader.name != CarMaterialColors.ShaderName)
                EditorGUILayout.HelpBox(label + "材质的 Shader 不兼容，运行时会使用旧配置。请选择 LoopPark/Layered Car。", MessageType.Error);
        }

        static Material[] GetProfileMaterials(CarColorAppearance p) => new[]
        {
            p.paintMaterial, p.windowMaterial, p.windowFrameMaterial, p.trimMaterial, p.frontSplitterMaterial,
            p.headlightMaterial, p.lightStripMaterial, p.taillightMaterial, p.wheelMaterial, p.tireMaterial
        };

        [InitializeOnLoadMethod]
        static void ScheduleMaterialMigration() => EditorApplication.delayCall += MigrateMaterialsOnce;

        static void MigrateMaterialsOnce()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += MigrateMaterialsOnce;
                return;
            }
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(ConfigPath);
            if (config != null && !config.carMaterialsInitialized) EnsureMaterials();
        }

        /// <summary>首次升级创建独立材质；再次执行只补空引用，保留美术已调好的资源。</summary>
        [MenuItem("LoopPark/生成或补齐车辆独立材质")]
        public static void EnsureMaterials()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureMaterials;
                return;
            }
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(ConfigPath);
            var shader = Shader.Find(CarMaterialColors.ShaderName);
            if (config == null || shader == null) return;
            config.EnsureCarAppearances();
            const string folder = "Assets/LoopPark/Resources/Model/Materials/Appearances";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/LoopPark/Resources/Model/Materials", "Appearances");
            bool changed = false;
            for (int i = -1; i < Palette.Count; i++)
            {
                var p = config.GetCarAppearance(i);
                var paint = i < 0 ? config.hiddenCarColor : Palette.Get(i);
                string prefix = i < 0 ? "Hidden" : "Color" + i.ToString("00");
                changed |= EnsureMaterial(ref p.paintMaterial, folder, prefix, "Paint", paint, p.carSmoothness, p.carSpecular, shader);
                changed |= EnsureMaterial(ref p.windowMaterial, folder, prefix, "Windows", p.carWindowColor, 0.75f, 0.35f, shader);
                changed |= EnsureMaterial(ref p.windowFrameMaterial, folder, prefix, "Frames", p.carWindowFrameColor, 0.3f, 0.12f, shader);
                changed |= EnsureMaterial(ref p.trimMaterial, folder, prefix, "Trim", p.carTrimColor, 0.4f, 0.18f, shader);
                changed |= EnsureMaterial(ref p.frontSplitterMaterial, folder, prefix, "Splitter", p.carFrontSplitterColor, 0.25f, 0.1f, shader);
                changed |= EnsureMaterial(ref p.headlightMaterial, folder, prefix, "Headlights", p.carHeadlightColor, 0.8f, 0.4f, shader);
                changed |= EnsureMaterial(ref p.lightStripMaterial, folder, prefix, "LightStrip", p.carLightStripColor, 0.7f, 0.3f, shader);
                changed |= EnsureMaterial(ref p.taillightMaterial, folder, prefix, "Taillights", p.carTaillightColor, 0.65f, 0.3f, shader);
                changed |= EnsureMaterial(ref p.wheelMaterial, folder, prefix, "Wheels", p.carWheelColor, 0.5f, 0.35f, shader);
                changed |= EnsureMaterial(ref p.tireMaterial, folder, prefix, "Tires", p.carTireColor, 0.12f, 0.04f, shader);
            }
            if (!changed && config.carMaterialsInitialized) return;
            config.carMaterialsInitialized = true;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            config.RefreshCarAppearance();
            Debug.Log("[CarAppearance] 已补齐 15 色及问号车的独立材质，可在车辆外观窗口选择材质球调节。");
        }

        static bool EnsureMaterial(ref Material reference, string folder, string prefix, string part,
            Color color, float smoothness, float specular, Shader shader)
        {
            if (reference != null) return false;
            string path = folder + "/" + prefix + "_" + part + ".mat";
            reference = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (reference != null) return true;
            reference = new Material(shader) { name = prefix + "_" + part };
            color.a = 1;
            reference.color = color;
            reference.SetFloat("_Glossiness", smoothness);
            reference.SetColor("_SpecColor", Color.white * specular);
            AssetDatabase.CreateAsset(reference, path);
            return true;
        }

        // 自定义中文标签时仍保留配置字段的 Tooltip。
        void Field(string name, string label)
        {
            var property = settings.FindProperty(name);
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
        }

        // 读取对应车色的序列化子字段，沿用 Undo 与 Tooltip。
        static void ProfileField(SerializedProperty profile, string name, string label)
        {
            var property = profile.FindPropertyRelative(name);
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
        }

        [MenuItem("LoopPark/迁移独立车色外观配置")]
        public static void MigrateAppearance()
        {
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(ConfigPath);
            config.EnsureCarAppearances();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[CarAppearance] 已保存 15 组独立外观及问号车配置，沿用当前参数");
        }

        /// <summary>首次迁移时显式应用本次默认值；平时重建模型不覆盖设计师已调好的配置。</summary>
        [MenuItem("LoopPark/应用本次 15 色与柔和材质配置")]
        public static void InitializeAppearance()
        {
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(ConfigPath);
            Undo.RecordObject(config, "应用车辆外观");
            config.carColors = (Color[])Palette.Default.Clone();
            config.carModelScale = new Vector2(1.4f, 1.2f);
            config.EnsureCarAppearances();
            for (int i = 0; i < config.carAppearanceByColor.Length; i++)
            {
                var profile = config.carAppearanceByColor[i];
                profile.carSmoothness = 0.18f;
                profile.carSpecular = 0.08f;
                if (profile.paintMaterial == null) continue;
                Undo.RecordObject(profile.paintMaterial, "应用车漆外观");
                profile.paintMaterial.color = Palette.Get(i);
                profile.paintMaterial.SetFloat("_Glossiness", profile.carSmoothness);
                profile.paintMaterial.SetColor("_SpecColor", Color.white * profile.carSpecular);
                EditorUtility.SetDirty(profile.paintMaterial);
                AssetDatabase.SaveAssetIfDirty(profile.paintMaterial);
            }
            config.RefreshCarAppearance();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[CarAppearance] 已应用 15 色、柔和材质与 X1.4/Y1.2 缩放");
        }
    }
}
