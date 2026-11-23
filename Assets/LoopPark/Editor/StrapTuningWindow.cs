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

        [MenuItem("LoopPark/绑带/实时调色 (Play 中可调 + 保存)")]
        public static void Open()
        {
            var window = GetWindow<StrapTuningWindow>("绑带调色");
            window.minSize = new Vector2(340f, 320f);
            window.Reload();
            window.Show();
        }

        void OnEnable() => Reload();

        void Reload()
        {
            config = AssetDatabase.LoadAssetAtPath<StrapTuningConfig>(AssetPath);
            serialized = config != null ? new SerializedObject(config) : null;
            status = config != null ? "" : "还没有调色资产，点下面的按钮创建。";
        }

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "Play 中：改任何一项，场景里的绑带下一帧就变。调满意后点「保存到磁盘」。"
                    : "未在 Play：改完点「保存到磁盘」，下次运行就用这些值。",
                MessageType.Info);

            if (config == null)
            {
                if (GUILayout.Button("创建调色资产 + 挂到预制体", GUILayout.Height(30)))
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
            if (GUILayout.Button("保存到磁盘（下次启动 / 别人读取都拿到这些值）", GUILayout.Height(30)))
                SaveToDisk();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("恢复跟随车漆"))
                {
                    serialized.Update();
                    serialized.FindProperty("useCarPaint").boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    status = "已切回跟随车漆（记得保存）";
                }
                if (GUILayout.Button("挂到预制体"))
                {
                    BindToPrefab();
                }
            }
            if (GUILayout.Button("打印场景里套管当前颜色（诊断）"))
                DumpSceneColors();

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.LabelField("资产：" + AssetPath, EditorStyles.miniLabel);
        }

        void DrawFields()
        {
            var useCarPaint = serialized.FindProperty("useCarPaint");
            var hoseColor = serialized.FindProperty("hoseColor");
            var affectsArt = serialized.FindProperty("affectsArt");
            var lift = serialized.FindProperty("lift");
            var smoothness = serialized.FindProperty("smoothness");
            var specular = serialized.FindProperty("specular");

            EditorGUILayout.LabelField("颜色来源", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useCarPaint, new GUIContent("跟随本车车漆色"));
            using (new EditorGUI.DisabledScope(useCarPaint.boolValue))
            {
                EditorGUILayout.PropertyField(hoseColor, new GUIContent("统一软管色"));
            }
            EditorGUILayout.PropertyField(affectsArt, new GUIContent("连美术吸盘/弯头一起染色"));
            if (!affectsArt.boolValue)
                EditorGUILayout.LabelField("　→ 只改代码生成的套管，美术件保持车漆色", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("材质观感", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!useCarPaint.boolValue))
            {
                EditorGUILayout.PropertyField(lift, new GUIContent("车漆色提亮量"));
            }
            EditorGUILayout.PropertyField(smoothness, new GUIContent("光滑度（高光宽度）"));
            EditorGUILayout.PropertyField(specular, new GUIContent("高光强度（偏亮就调小）"));

            EditorGUILayout.Space(4);
            var preview = useCarPaint.boolValue
                ? "当前：跟随车漆"
                : "当前：统一色 " + ColorUtility.ToHtmlStringRGB(hoseColor.colorValue);
            EditorGUILayout.HelpBox(preview, MessageType.None);
        }

        /// <summary>每根管子单独调色：列出场景里当前可见的套管（按配对编号分组），各自给一个颜色框。
        /// 改色立即生效（CarStrap 每帧读资产）；点「清除」回到全局规则。</summary>
        void DrawHoseSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("每根管子单独调色", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "下面列出场景里当前显示的每一根管（link 编号）。改哪个颜色，那根管下一帧就变。"
                    : "进 Play 配对后这里才会列出管子；改完记得保存。",
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

            if (GUILayout.Button("把当前每根的颜色写成默认值并保存（先进 Play 配对，再点这里）"))
            {
                EditorTools.StrapRigCheck.CaptureCurrentHoseColors();
                status = "已把当前颜色写入资产并保存";
            }

            if (keys.Count == 0)
            {
                EditorGUILayout.LabelField("（当前没有可见的管子）", EditorStyles.miniLabel);
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
                        status = "已改 " + key + " = " + ColorUtility.ToHtmlStringRGB(picked) + "（记得保存）";
                    }
                    if (hasOverride)
                    {
                        if (GUILayout.Button("清除", GUILayout.Width(48f)))
                        {
                            config.ClearHoseColor(key);
                            EditorUtility.SetDirty(config);
                            status = key + " 已回到全局规则（记得保存）";
                        }
                    }
                    else
                    {
                        GUILayout.Label("跟随默认", EditorStyles.miniLabel, GUILayout.Width(48f));
                    }
                }
            }

            if (GUILayout.Button("清除所有单根设置（全部回到全局规则）"))
            {
                config.hoses.Clear();
                EditorUtility.SetDirty(config);
                status = "已清除所有单根颜色（记得保存）";
            }
        }

        void SaveToDisk()
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            status = "已保存：" + AssetPath + "（" + System.DateTime.Now.ToString("HH:mm:ss") + "）";
            Debug.Log("[StrapTuning] 已保存 " + AssetPath
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
            status = "已创建并挂到预制体：" + AssetPath;
            Debug.Log("[StrapTuning] 创建 " + AssetPath);
        }

        /// <summary>把资产写进 CarModel.prefab 的 CarStrap.tuning 字段（预制体里不挂就等于没生效）。</summary>
        void BindToPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var strap = root.GetComponentInChildren<CarStrap>(true);
                if (strap == null) { status = "预制体里没找到 CarStrap"; return; }
                var so = new SerializedObject(strap);
                var property = so.FindProperty("tuning");
                property.objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                status = "已把调色资产挂到 " + PrefabPath;
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
                Debug.Log("[StrapTuning] 套管色=" + block.GetColor("_Color")
                          + " 弯头/美术件=" + strap.name
                          + " 资产=" + (config != null ? config.hoseColor.ToString() : "无"));
                shown++;
            }
            status = shown > 0 ? "已打印 " + shown + " 根套管颜色（看 Console）" : "场景里没有可见套管（先让车配对）";
        }
    }
}
#endif
