#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 绑带调色窗口：**Play 模式下直接改、立即生效，点保存落盘**。
    ///
    /// 数据流（单向）：
    ///   本窗口改 StrapTuningConfig（ScriptableObject）
    ///     → CarStrap.ApplyColors 每帧读取 → StrapPaint 写 MaterialPropertyBlock
    ///     → 屏幕上立刻变化（不用重启、不用重生成预制体）
    ///   点「保存到磁盘」→ EditorUtility.SetDirty + AssetDatabase.SaveAssets
    ///     → .asset 文件真正写入 → 退出 Play / 下次启动 / 别人读文件都拿到这些值
    ///
    /// 为什么用资产而不是静态字段：符合工程规范（共享配置归 SO、运行时可热调、不依赖场景），
    /// 而且退出 Play 后值不会丢。
    /// </summary>
    public sealed class StrapTuningWindow : EditorWindow
    {
        const string AssetFolder = "Assets/LoopPark/Resources/Model";
        const string AssetPath = AssetFolder + "/StrapTuning.asset";
        const string PrefabPath = "Assets/LoopPark/Resources/Model/CarModel.prefab";

        StrapTuningConfig config;
        SerializedObject serialized;
        Vector2 scroll;
        string status = "";

        [MenuItem("LoopPark/Straps/Live Tuning (Editable in Play Mode + Save)")]
        public static void Open()
        {
            var window = GetWindow<StrapTuningWindow>("Strap Tuning");
            window.minSize = new Vector2(340f, 320f);
            window.Reload();
            window.Show();
        }

        void OnEnable() => Reload();

        void Reload()
        {
            config = AssetDatabase.LoadAssetAtPath<StrapTuningConfig>(AssetPath);
            serialized = config != null ? new SerializedObject(config) : null;
            status = config != null ? "" : "No tuning asset yet; click the button below to create one.";
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "In Play mode: any change updates the straps next frame. Click Save to Disk when happy."
                    : "Not in Play mode: click Save to Disk and the next run uses these values.",
                MessageType.Info);

            if (config == null)
            {
                if (GUILayout.Button("Create Tuning Asset + Attach to Prefab", GUILayout.Height(30)))
                    CreateAndBind();
                return;
            }

            serialized.Update();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawFields();
            DrawHoseSection();
            EditorGUILayout.EndScrollView();
            if (serialized.ApplyModifiedProperties())
            {
                // 运行中改完立刻可见：CarStrap 每帧读这个资产；这里标脏让编辑器知道值变了
                EditorUtility.SetDirty(config);
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Save to Disk (next launch and other readers get these values)", GUILayout.Height(30)))
                SaveToDisk();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Follow car paint again"))
                {
                    serialized.Update();
                    serialized.FindProperty("useCarPaint").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    status = "Back to following car paint (remember to save)";
                }
                if (GUILayout.Button("Attach to Prefab"))
                {
                    BindToPrefab();
                }
            }
            if (GUILayout.Button("Print current sleeve colors in scene (diagnostics)"))
                DumpSceneColors();

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.LabelField("Asset: " + AssetPath, EditorStyles.miniLabel);
        }

        void DrawFields()
        {
            var useCarPaint = serialized.FindProperty("useCarPaint");
            var hoseColor = serialized.FindProperty("hoseColor");
            var affectsArt = serialized.FindProperty("affectsArt");
            var lift = serialized.FindProperty("lift");
            var smoothness = serialized.FindProperty("smoothness");
            var specular = serialized.FindProperty("specular");

            EditorGUILayout.LabelField("Color source", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useCarPaint, new GUIContent("Follow this car paint"));
            using (new EditorGUI.DisabledScope(useCarPaint.boolValue))
            {
                EditorGUILayout.PropertyField(hoseColor, new GUIContent("Single hose color"));
            }
            EditorGUILayout.PropertyField(affectsArt, new GUIContent("Tint art suction cups / elbows too"));
            if (!affectsArt.boolValue)
                EditorGUILayout.LabelField("  -> only code-generated sleeves change; art pieces keep car paint", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Material look", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!useCarPaint.boolValue))
            {
                EditorGUILayout.PropertyField(lift, new GUIContent("Paint brightness boost"));
            }
            EditorGUILayout.PropertyField(smoothness, new GUIContent("Smoothness (highlight width)"));
            EditorGUILayout.PropertyField(specular, new GUIContent("Specular intensity (lower if too bright)"));

            EditorGUILayout.Space(4);
            var preview = useCarPaint.boolValue
                ? "Current: following car paint"
                : "Current: single color " + ColorUtility.ToHtmlStringRGB(hoseColor.colorValue);
            EditorGUILayout.HelpBox(preview, MessageType.None);
        }

        /// <summary>每根管子单独调色：列出场景里当前可见的套管（按配对编号分组），各自给一个颜色框。
        /// 改色立即生效（CarStrap 每帧读资产）；点「清除」回到全局规则。</summary>
        void DrawHoseSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Per-hose color", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "Lists every visible hose in the scene (link id). Changing a color updates that hose next frame."
                    : "Hoses appear here after pairing in Play mode; remember to save.",
                MessageType.None);

            var straps = Object.FindObjectsByType<CarStrap>(FindObjectsSortMode.None);
            var keys = new List<string>();
            foreach (var strap in straps)
            {
                if (strap == null) continue;
                if (strap.Hose == null) continue;          // 只列真正画出来的套管
                string key = strap.HoseKey;
                if (!keys.Contains(key)) keys.Add(key);
            }
            keys.Sort();

            if (GUILayout.Button("Write each current hose color as defaults and save (enter Play, let cars pair, then click here)"))
            {
                EditorTools.StrapRigCheck.CaptureCurrentHoseColors();
                status = "Current colors written to asset and saved";
            }

            if (keys.Count == 0)
            {
                EditorGUILayout.LabelField("(no visible hose right now)", EditorStyles.miniLabel);
                return;
            }

            int index = 0;
            foreach (string key in keys)
            {
                bool hasOverride = config.TryGetHoseColor(key, out Color current);
                Color shown = hasOverride
                    ? current
                    : (config.useCarPaint ? Color.white : config.hoseColor);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("#" + (++index) + "  " + key, GUILayout.Width(96f));
                    EditorGUI.BeginChangeCheck();
                    Color picked = EditorGUILayout.ColorField(GUIContent.none, shown, false, false, false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        config.SetHoseColor(key, picked);
                        EditorUtility.SetDirty(config);
                        status = "Changed " + key + " = " + ColorUtility.ToHtmlStringRGB(picked) + "(remember to save)";
                    }
                    if (hasOverride)
                    {
                        if (GUILayout.Button("Clear", GUILayout.Width(48f)))
                        {
                            config.ClearHoseColor(key);
                            EditorUtility.SetDirty(config);
                            status = key + " reverted to global rule (remember to save)";
                        }
                    }
                    else
                    {
                        GUILayout.Label("Follow default", EditorStyles.miniLabel, GUILayout.Width(48f));
                    }
                }
            }

            if (GUILayout.Button("Clear all per-hose settings (everything back to the global rule)"))
            {
                config.hoses.Clear();
                EditorUtility.SetDirty(config);
                status = "All per-hose colors cleared (remember to save)";
            }
        }

        void SaveToDisk()
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            status = "Saved: " + AssetPath + "（" + System.DateTime.Now.ToString("HH:mm:ss") + "）";
            Debug.Log("[StrapTuning] Saved " + AssetPath
                      + " useCarPaint=" + config.useCarPaint
                      + " hoseColor=" + config.hoseColor
                      + " smoothness=" + config.smoothness.ToString("F2")
                      + " specular=" + config.specular.ToString("F2"));
        }

        void CreateAndBind()
        {
            if (!Directory.Exists(AssetFolder)) Directory.CreateDirectory(AssetFolder);
            config = AssetDatabase.LoadAssetAtPath<StrapTuningConfig>(AssetPath);
            if (config == null)
            {
                config = CreateInstance<StrapTuningConfig>();
                AssetDatabase.CreateAsset(config, AssetPath);
                AssetDatabase.SaveAssets();
            }
            serialized = new SerializedObject(config);
            BindToPrefab();
            status = "Created and attached to prefab: " + AssetPath;
            Debug.Log("[StrapTuning] Created " + AssetPath);
        }

        /// <summary>把资产写进 CarModel.prefab 的 CarStrap.tuning 字段（预制体里不挂就等于没生效）。</summary>
        void BindToPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var strap = root.GetComponentInChildren<CarStrap>(true);
                if (strap == null) { status = "No CarStrap found in prefab"; return; }
                var so = new SerializedObject(strap);
                var property = so.FindProperty("tuning");
                property.objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                status = "Tuning asset attached to " + PrefabPath;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>诊断：把场景里所有套管属性块的颜色打出来，确认窗口的改动是否真的落到渲染器上。</summary>
        void DumpSceneColors()
        {
            var straps = Object.FindObjectsByType<CarStrap>(FindObjectsSortMode.None);
            int shown = 0;
            foreach (var strap in straps)
            {
                var hose = strap.Hose;
                if (hose == null) continue;
                var renderer = hose.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                Debug.Log("[StrapTuning] Sleeve color=" + block.GetColor("_Color")
                          + " elbow/art=" + strap.name
                          + " asset=" + (config != null ? config.hoseColor.ToString() : "None"));
                shown++;
            }
            status = shown > 0 ? "Printed " + shown + " sleeve colors printed (see Console)" : "No visible sleeve in scene (pair the cars first)";
        }
    }
}
#endif
