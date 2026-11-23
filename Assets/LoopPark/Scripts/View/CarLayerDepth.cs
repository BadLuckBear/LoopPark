using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>为整辆多网格车提供统一深度；屏幕下方车辆覆盖上方车辆，移动层优先。</summary>
    public sealed class CarLayerDepth : MonoBehaviour
    {
        [Tooltip("车辆逻辑位置与层级来源；只读取位置，不修改行驶轨迹和车距。")]
        [SerializeField] Transform anchor;
        Renderer[] renderers;
        int[] materialCounts;
        // 附件（如绑带吸盘）自带深度偏置：shader 会把世界 z 压缩到 _CarDepth 附近，
        // 车顶细节之间的真实深度差不足一个像素精度，必须显式前移才不会 Z-fighting。
        readonly Dictionary<Renderer, float> depthBiases = new Dictionary<Renderer, float>();
        MaterialPropertyBlock block;
        int layer = 20;
        static readonly int DepthId = Shader.PropertyToID("_CarDepth");
        static readonly int OutlineCenterId = Shader.PropertyToID("_CarOutlineCenter");

        /// <summary>绑定车辆根节点，所有独立网格共用同一排序基准。</summary>
        public void Initialize(Transform root)
        {
            anchor = root;
            // 属性块要先于渲染器数组创建：注册（Register）可能早于本方法完成，
            // 之后 LateUpdate 一旦跑起来就必须能用。
            block = new MaterialPropertyBlock();
            var found = GetComponentsInChildren<MeshRenderer>(true);
            var owned = new List<MeshRenderer>(found.Length);
            foreach (var renderer in found)
            {
                // 绑带附件由 CarStrap 在启用时注册，并携带压到车顶之上的深度偏置。
                if (renderer.GetComponentInParent<CarStrap>(true) != null) continue;
                owned.Add(renderer);
            }
            renderers = owned.ToArray();
            materialCounts = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) materialCounts[i] = renderers[i].sharedMaterials.Length;
        }

        /// <summary>
        /// 追加一个独立管理的渲染器（如后挂的附件），并给它相对整车深度的偏置。
        /// 负数 = 更靠近相机；取值应小于相邻车的深度间隔，避免越到别的车前面。
        /// </summary>
        public void Register(Renderer renderer, float depthBias)
        {
            if (renderer == null || renderers == null) return;
            if (IndexOf(renderer) >= 0) { depthBiases[renderer] = depthBias; return; }
            int index = renderers.Length;
            System.Array.Resize(ref renderers, index + 1);
            System.Array.Resize(ref materialCounts, index + 1);
            renderers[index] = renderer;
            materialCounts[index] = renderer.sharedMaterials.Length;
            depthBiases[renderer] = depthBias;
        }

        /// <summary>该渲染器是否已在深度列表里；返回下标，没有返回 -1。</summary>
        int IndexOf(Renderer renderer)
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] == renderer) return i;
            return -1;
        }

        /// <summary>沿用现有 SetLayer：30 为移动车，20 为停车与排队车辆。</summary>
        public void SetLayer(int value) => layer = value;

        void LateUpdate() => RefreshDepth();

        /// <summary>位置和动画更新后同步深度；层级验证也通过此入口检查移动车覆盖。</summary>
        public void RefreshDepth()
        {
            if (anchor == null || renderers == null) return;
            // 兜底：域重载或极早的调用可能让 block 还没创建，避免每帧抛 ArgumentNullException。
            block ??= new MaterialPropertyBlock();
            // 只改变 Shader 深度，保留 XY 外观、物理位置和完整的车内自遮挡。
            float depth = -2f + anchor.position.y * 0.05f - (layer - 20) * 0.2f;
            Vector3 center = transform.position;
            var outlineCenter = new Vector4(center.x, center.y, 0f, 1f);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                float bias = depthBiases.TryGetValue(renderer, out float value) ? value : 0f;
                for (int slot = 0; slot < materialCounts[i]; slot++)
                {
                    renderer.GetPropertyBlock(block, slot);
                    block.SetFloat(DepthId, depth + bias);
                    block.SetVector(OutlineCenterId, outlineCenter);
                    renderer.SetPropertyBlock(block, slot);
                }
            }
        }
    }
}
