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
        [SerializeField, Tooltip("Color id currently being tuned; the last entry is the mystery car. Switching does not modify other colors.") ]
        int selectedColor;
        [SerializeField] bool showFallback;

        [MenuItem("LoopPark/Car Appearance (Works While Playing)")]
        public static void Open() => GetWindow<CarAppearanceWindow>("Car Appearance");

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
            if (settings == null) { EditorGUILayout.HelpBox("LoopParkSkin config not found", MessageType.Error); return; }
            EditorGUILayout.HelpBox("Tune while playing; current cars refresh immediately. After saving, reruns and builds use this config.", MessageType.Info);
            settings.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            Field("carModelScale", "Car scale X / Y / Z");
            Field("carViewTilt", "Parking area tilt");
            Field("departureCarViewTilt", "Departure area tilt");
            Field("carViewTiltTransitionTime", "Area view transition time");
            EditorGUILayout.Space();
            var labels = new string[Palette.Count + 1];
            for (int i = 0; i < Palette.Count; i++) labels[i] = i + "  " + Palette.Names[i];
            labels[Palette.Count] = "Mystery car";
            selectedColor = EditorGUILayout.Popup(new GUIContent("Color being tuned", "Only the selected color changes; other colors keep their own settings."), Mathf.Clamp(selectedColor, 0, Palette.Count), labels);
            bool hidden = selectedColor == Palette.Count;
            var profile = hidden ? settings.FindProperty("hiddenCarAppearance") : settings.FindProperty("carAppearanceByColor").GetArrayElementAtIndex(selectedColor);
            EditorGUILayout.HelpBox("Independent materials take priority. Click a material to tune color, texture, specular and emission in the Inspector; changing a shared material affects every car using it. Use LoopPark/Layered Car Shader to keep occlusion sorting.", MessageType.Info);
            MaterialField(profile, "paintMaterial", "Car paint / mirrors");
            MaterialField(profile, "windowMaterial", "Windows");
            MaterialField(profile, "windowFrameMaterial", "Window frame / black structure");
            MaterialField(profile, "trimMaterial", "Trim");
            MaterialField(profile, "frontSplitterMaterial", "Front splitter / skirt");
            MaterialField(profile, "headlightMaterial", "Headlights");
            MaterialField(profile, "lightStripMaterial", "Light strip");
            MaterialField(profile, "taillightMaterial", "Taillights");
            MaterialField(profile, "wheelMaterial", "Wheels");
            MaterialField(profile, "tireMaterial", "Tires");
            showFallback = EditorGUILayout.Foldout(showFallback, "Legacy tinting fallback (only parts without a material)", true);
            if (showFallback)
            {
            if (hidden) Field("hiddenCarColor", "Mystery car body color");
            else EditorGUILayout.PropertyField(settings.FindProperty("carColors").GetArrayElementAtIndex(selectedColor), new GUIContent("Body color", "Paint color for the selected id; changes apply immediately."));
            ProfileField(profile, "carSmoothness", "Smoothness (low = matte)");
            ProfileField(profile, "carSpecular", "Specular intensity (low = soft)");
            ProfileField(profile, "carWindowColor", "Windows");
            ProfileField(profile, "carWindowFrameColor", "Window frame / black structure");
            ProfileField(profile, "carTrimColor", "Body trim");
            ProfileField(profile, "carFrontSplitterColor", "Front splitter inner / skirt");
            ProfileField(profile, "carHeadlightColor", "Headlights");
            ProfileField(profile, "carLightStripColor", "Light strip");
            ProfileField(profile, "carTaillightColor", "Taillights");
            ProfileField(profile, "carWheelColor", "Wheels");
            ProfileField(profile, "carTireColor", "Tires");
            }
            if (EditorGUI.EndChangeCheck())
            {
                // SerializedObject 保留 Undo，并通过事件刷新当前车辆。
                settings.ApplyModifiedProperties();
                config.RefreshCarAppearance();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Save Current Appearance Config"))
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
            EditorGUILayout.PropertyField(property, new GUIContent(label + "Material"));
            var material = property.objectReferenceValue as Material;
            using (new EditorGUI.DisabledScope(material == null))
                if (GUILayout.Button("Edit", GUILayout.Width(42))) Selection.activeObject = material;
            EditorGUILayout.EndHorizontal();
            if (material != null && material.shader.name != CarMaterialColors.ShaderName)
                EditorGUILayout.HelpBox(label + "Material shader is incompatible; the legacy config is used at runtime. Choose LoopPark/Layered Car.", MessageType.Error);
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
        [MenuItem("LoopPark/Create or Fill Car Materials")]
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
            Debug.Log("[CarAppearance] Materials for 15 colors and the mystery car are complete; pick the material in the Car Appearance window to tune.");
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

        [MenuItem("LoopPark/Migrate Per-Color Appearance Config")]
        public static void MigrateAppearance()
        {
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(ConfigPath);
            config.EnsureCarAppearances();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[CarAppearance] Saved 15 independent appearance sets plus mystery car config, keeping current values");
        }

        /// <summary>首次迁移时显式应用本次默认值；平时重建模型不覆盖设计师已调好的配置。</summary>
        [MenuItem("LoopPark/Apply Current 15 Colors and Soft Materials")]
        public static void InitializeAppearance()
        {
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>(ConfigPath);
            Undo.RecordObject(config, "Apply car appearance");
            config.carColors = (Color[])Palette.Default.Clone();
            config.carModelScale = new Vector3(1.4f, 1.2f, 1f);
            config.EnsureCarAppearances();
            for (int i = 0; i < config.carAppearanceByColor.Length; i++)
            {
                var profile = config.carAppearanceByColor[i];
                profile.carSmoothness = 0.18f;
                profile.carSpecular = 0.08f;
                if (profile.paintMaterial == null) continue;
                Undo.RecordObject(profile.paintMaterial, "Apply car paint appearance");
                profile.paintMaterial.color = Palette.Get(i);
                profile.paintMaterial.SetFloat("_Glossiness", profile.carSmoothness);
                profile.paintMaterial.SetColor("_SpecColor", Color.white * profile.carSpecular);
                EditorUtility.SetDirty(profile.paintMaterial);
                AssetDatabase.SaveAssetIfDirty(profile.paintMaterial);
            }
            config.RefreshCarAppearance();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[CarAppearance] Applied 15 colors, soft materials and X1.4/Y1.2 scale");
        }
    }
}
