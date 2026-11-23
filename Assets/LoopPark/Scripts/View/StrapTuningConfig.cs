using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 绑带上色的**唯一可调数据源**（ScriptableObject），支持「每根管子单独一个颜色」。
    ///
    /// 为什么要有这个资产：
    ///   车漆色是 CarSkin3D 从车色配置推给 CarStrap 的，代码里没有「给人调的地方」；
    ///   而美术要求「运行时看到不满意就改、每根管子还能单独改、改完存下来，下次启动继续用」。
    ///   所以颜色优先级变成：**单根覆盖 → 全局规则 → 本车车漆色**。
    ///
    /// 单根覆盖怎么认「同一根」：套管属于一对绑带（一个 link 编号），
    /// 所以 key = "link<配对编号>"。同一关重跑时编号不变 → 覆盖仍然命中；
    /// 一关里的每对绑带编号不同 → 每根管子各自独立。
    ///
    /// 数据流：
    ///   Editor/StrapTuningWindow（Play 中修改 + 保存） → 本资产
    ///   → CarStrap.ApplyColors 每帧读取 → StrapPaint 写属性块（不改共享材质）
    /// </summary>
    [CreateAssetMenu(menuName = "LoopPark/Strap Tuning", fileName = "StrapTuning")]
    public sealed class StrapTuningConfig : ScriptableObject
    {
        /// <summary>单根管子的颜色覆盖（key = 配对编号）。</summary>
        [System.Serializable]
        public sealed class HoseColorEntry
        {
            [Tooltip("Stable id: link<pairing id> (unchanged when replaying the same level)")]
            public string key;
            public Color color = Color.white;
        }

        [Header("Global rule (hoses without their own override follow this)")]
        [Tooltip("On = sleeves/straps follow this car paint; off = use the single hose color below.")]
        public bool useCarPaint = true;

        [Tooltip("Single hose color (used when useCarPaint is off).")]
        public Color hoseColor = new Color(0.34f, 0.36f, 0.40f, 1f);

        [Tooltip("Off by default: tinting only changes code-generated sleeves; art suction cups/elbows keep the car paint. Enable only to tint the whole strap including art pieces." +
                 "Tick only if the whole strap (including art pieces) should be tinted.")]
        public bool affectsArt = false;

        [Header("Material look")]
        [Tooltip("Brightness added when following car paint (lerp toward white). 0 = identical to paint.")]
        [Range(0f, 0.6f)] public float lift = 0f;

        [Tooltip("Smoothness (highlight width). Lower = more matte, higher = glossier.")]
        [Range(0f, 1f)] public float smoothness = 0.18f;

        [Tooltip("Specular intensity. Cylindrical surfaces brighten noticeably; lower it if the hose looks too bright.")]
        [Range(0f, 1f)] public float specular = 0.08f;

        [Header("Per-hose override")]
        [Tooltip("Hoses listed here use their own colors; unlisted ones follow the global rule above.")]
        public List<HoseColorEntry> hoses = new List<HoseColorEntry>();

        /// <summary>查这根管子是否有单独指定的颜色。</summary>
        public bool TryGetHoseColor(string key, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(key) || hoses == null) return false;
            for (int i = 0; i < hoses.Count; i++)
            {
                if (hoses[i] == null || hoses[i].key != key) continue;
                color = hoses[i].color;
                return true;
            }
            return false;
        }

        /// <summary>设置/新增某根管子的颜色（运行中改完下一帧就生效）。</summary>
        public void SetHoseColor(string key, Color color)
        {
            if (string.IsNullOrEmpty(key)) return;
            hoses ??= new List<HoseColorEntry>();
            for (int i = 0; i < hoses.Count; i++)
            {
                if (hoses[i] == null || hoses[i].key != key) continue;
                hoses[i].color = color;
                return;
            }
            hoses.Add(new HoseColorEntry { key = key, color = color });
        }

        /// <summary>取消某根管子的单独颜色，回到全局规则。</summary>
        public void ClearHoseColor(string key)
        {
            if (hoses == null) return;
            for (int i = hoses.Count - 1; i >= 0; i--)
                if (hoses[i] == null || hoses[i].key == key) hoses.RemoveAt(i);
        }
    }
}
