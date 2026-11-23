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
    [CreateAssetMenu(menuName = "LoopPark/绑带调色", fileName = "StrapTuning")]
    public sealed class StrapTuningConfig : ScriptableObject
    {
        /// <summary>单根管子的颜色覆盖（key = 配对编号）。</summary>
        [System.Serializable]
        public sealed class HoseColorEntry
        {
            [Tooltip("稳定标识：link<配对编号>（同一关重跑不变）")]
            public string key;
            public Color color = Color.white;
        }

        [Header("全局规则（没有被单根覆盖的管子按这里）")]
        [Tooltip("开启 = 套管/绑带跟随本车车漆色；关闭 = 统一使用下面的软管色。")]
        public bool useCarPaint = true;

        [Tooltip("统一软管色（useCarPaint 关闭时生效）。")]
        public Color hoseColor = new Color(0.34f, 0.36f, 0.40f, 1f);

        [Tooltip("默认关闭：调色只改**代码生成的套管**，美术吸盘/弯头保持本车车漆色。" +
                 "只有想让整条绑带（含美术件）一起染色时才勾上。")]
        public bool affectsArt = false;

        [Header("材质观感")]
        [Tooltip("跟随车漆时的提亮量（朝白插值）。0 = 与车漆完全同色。")]
        [Range(0f, 0.6f)] public float lift = 0f;

        [Tooltip("光滑度（高光宽度）。越小越哑光，越大越亮。")]
        [Range(0f, 1f)] public float smoothness = 0.18f;

        [Tooltip("高光强度。圆柱面上高光会明显变亮，觉得管子偏亮就调小。")]
        [Range(0f, 1f)] public float specular = 0.08f;

        [Header("单根覆盖")]
        [Tooltip("列在这里的管子用各自的颜色；不在列表里的走上面的全局规则。")]
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
