using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 绑带附件上色：把车漆色 / 光滑度 / 高光写进每个渲染器的 MaterialPropertyBlock。
    ///
    /// 为什么单独成类：CarStrap 管的是「绑带状态与姿态」，颜色是另一件事；
    /// 而且这里有两个硬约束必须在同一个地方说清：
    /// 1) 只能写属性块，不能改 material —— Strap.mat 被所有绑带车共用，改材质会把颜色串到每一辆车上；
    /// 2) 读属性块时**不能 Clear()** —— CarLayerDepth 每帧往同一个块里写 _CarDepth，
    ///    清空会把跨车排序一起清掉（表现为附件被自己的车顶盖住）。
    /// </summary>
    public sealed class StrapPaint
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ColorEndId = Shader.PropertyToID("_ColorEnd");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Glossiness");
        static readonly int SpecularId = Shader.PropertyToID("_SpecColor");

        // 懒创建：**绝不能**写成字段初始化 `= new MaterialPropertyBlock()`。
        // 本类由 CarStrap 的字段初始化 new 出来（那发生在 MonoBehaviour 构造函数里），
        // 而 Unity 禁止在构造/字段初始化中创建 MaterialPropertyBlock 这类原生对象：
        //   UnityException: CreateImpl is not allowed to be called from a MonoBehaviour constructor
        // 一旦抛在这里，CarStrap 构造失败 → 预制体实例化失败 → StartLevel 在 lanes 赋值前中断
        // → 每帧 CheckStuck NRE（2026-11-24 真实事故）。
        MaterialPropertyBlock block;

        Color paint = Color.white;
        float smoothness = 0.18f;
        float specular = 0.08f;

        /// <summary>跟随车漆；光滑度与高光取自该颜色编号的外观配置；lift = 朝白色提亮的量（见 CarStrap.hoseLift）。</summary>
        public void Set(Color color, float carSmoothness, float carSpecular, float lift = 0f)
        {
            // 为什么要提亮而不是直接用车漆色：近黑车漆（实测 albedo≈0.07）会让整根套管看起来「没有受光」，
            // 玩家/美术都会误判成 shader 坏了。提一点亮度既保留「跟本车同色」，又保证任何颜色都能看到圆柱明暗与高光。
            paint = lift > 0f ? Color.Lerp(color, Color.white, Mathf.Clamp01(lift)) : color;
            smoothness = carSmoothness;
            specular = carSpecular;
        }

        /// <summary>提亮后的绑带色（弯头用这个色）；供 CarStrap 与搭档取平均。</summary>
        public Color Display => paint;

        /// <summary>给美术件与套管上色。
        /// artColor / sleeveColor 为空 = 用本车车漆色；非空 = 用传入颜色（调参资产覆盖时用）。
        /// smoothness / specular 传负数 = 用该车外观配置里的值。</summary>
        public void Apply(Renderer[] renderers, int[] materialCounts, Renderer sleeve,
                          Color? artColor = null, Color? sleeveColor = null,
                          float smoothnessOverride = -1f, float specularOverride = -1f)
        {
            float smooth = smoothnessOverride >= 0f ? smoothnessOverride : smoothness;
            float spec = specularOverride >= 0f ? specularOverride : specular;
            Color art = artColor ?? paint;
            if (renderers != null && materialCounts != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null) continue;
                    for (int slot = 0; slot < materialCounts[i]; slot++) Write(renderer, slot, art, art, smooth, spec);
                }
            }
            if (sleeve != null)
            {
                Color tube = sleeveColor ?? paint;
                for (int slot = 0; slot < sleeve.sharedMaterials.Length; slot++) Write(sleeve, slot, tube, tube, smooth, spec);
            }
        }

        void Write(Renderer renderer, int slot, Color color, Color colorEnd, float smooth, float spec)
        {
            block ??= new MaterialPropertyBlock();   // 首次使用时才创建（此时已不在构造函数里）
            renderer.GetPropertyBlock(block, slot);
            block.SetColor(ColorId, color);
            block.SetColor(ColorEndId, colorEnd);
            block.SetColor(BaseColorId, color);
            block.SetFloat(SmoothnessId, smooth);
            block.SetColor(SpecularId, Color.white * spec);
            renderer.SetPropertyBlock(block, slot);
        }
    }
}

