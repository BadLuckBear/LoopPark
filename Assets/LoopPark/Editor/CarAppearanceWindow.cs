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
            if (hidden) Field("hiddenCarColor", "问号车车身颜色");
            else EditorGUILayout.PropertyField(settings.FindProperty("carColors").GetArrayElementAtIndex(selectedColor), new GUIContent("车身颜色", "所选编号的车漆颜色，修改后立即刷新。"));
            ProfileField(profile, "carSmoothness", "光滑度（低 = 哑光）");
            ProfileField(profile, "carSpecular", "高光强度（低 = 柔和）");
            ProfileField(profile, "carWindowColor", "车窗");
            ProfileField(profile, "carTrimColor", "窗框 / 饰条 / 前铲内部");
            ProfileField(profile, "carHeadlightColor", "前灯 / 灯条");
            ProfileField(profile, "carTaillightColor", "尾灯");
            ProfileField(profile, "carWheelColor", "轮毂");
            ProfileField(profile, "carTireColor", "轮胎");
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
            }
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
            foreach (var profile in config.carAppearanceByColor)
            {
                profile.carSmoothness = 0.18f;
                profile.carSpecular = 0.08f;
            }
            config.RefreshCarAppearance();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
            Debug.Log("[CarAppearance] 已应用 15 色、柔和材质与 X1.4/Y1.2 缩放");
        }
    }
}
