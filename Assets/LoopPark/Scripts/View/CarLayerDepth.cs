using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>为整辆多网格车提供统一深度；屏幕下方车辆覆盖上方车辆，移动层优先。</summary>
    public sealed class CarLayerDepth : MonoBehaviour
    {
        [Tooltip("Logical position and layering source for the car; only reads position, never changes the path or spacing.")]
        [SerializeField] Transform anchor;
        Renderer[] renderers;
        int[] materialCounts;
        // 附件（如绑带吸盘）自带深度偏置：shader 会把世界 z 压缩到 _CarDepth 附近，
        // 车顶细节之间的真实深度差不足一个像素精度，必须显式前移才不会 Z-fighting。
        readonly Dictionary<Renderer, float> depthBiases = new Dictionary<Renderer, float>();
        // Initialize() 之前就登记进来的渲染器（CarStrap 在车刚实例化时就会注册附件）：
        // 那时 renderers 还是 null，早期实现直接 return 把登记丢掉，导致这些渲染器永远拿不到 _CarDepth
        // （实测 6 根套管里 2 根 _CarDepth=0，会被别的车盖住）。这里先挂起，Initialize 时补进来。
        readonly List<Renderer> pending = new List<Renderer>();
        // 横跨多车的细长件（绑带套管）：描边 pass 是「绕世界 XY 从车中心外扩」，
        // 对它等于把端部径向撑开成一圈黑环，因此登记为不描边。
        readonly HashSet<Renderer> outlineSuppressed = new HashSet<Renderer>();
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
            // 并入 Initialize 之前挂起的登记：那时 renderers 还是 null，Register 只记了偏置。
            for (int i = 0; i < pending.Count; i++)
                if (pending[i] != null) Append(pending[i]);
            pending.Clear();
        }

        /// <summary>
        /// 追加一个独立管理的渲染器（如后挂的附件），并给它相对整车深度的偏置。
        /// 负数 = 更靠近相机；取值应小于相邻车的深度间隔，避免越到别的车前面。
        /// suppressOutline：横跨多车的细长件传 true —— 描边 pass 会绕车中心径向外扩，
        /// 在细长件端部撑出一圈黑环。默认 false，既有调用方行为不变。
        /// </summary>
        public void Register(Renderer renderer, float depthBias, bool suppressOutline = false)
        {
            if (renderer == null) return;
            // 偏置与描边开关先记下来：即使车辆还没 Initialize，这些信息也不能丢。
            depthBiases[renderer] = depthBias;
            if (suppressOutline) outlineSuppressed.Add(renderer);
            else outlineSuppressed.Remove(renderer);
            if (renderers == null)
            {
                // 车辆尚未 Initialize：挂起，等 Initialize 统一并入（不要让登记静默消失）。
                if (!pending.Contains(renderer)) pending.Add(renderer);
                return;
            }
            Append(renderer);
        }

        /// <summary>把渲染器并入深度列表（已存在则跳过，materialCounts 同步）。</summary>
        void Append(Renderer renderer)
        {
            if (Contains(renderer)) return;
            int index = renderers.Length;
            System.Array.Resize(ref renderers, index + 1);
            System.Array.Resize(ref materialCounts, index + 1);
            renderers[index] = renderer;
            materialCounts[index] = renderer.sharedMaterials.Length;
        }

        /// <summary>该渲染器是否已在深度列表里。</summary>
        bool Contains(Renderer renderer)
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] == renderer) return true;
            return false;
        }

        /// <summary>沿用现有 SetLayer：30 为移动车，20 为停车与排队车辆。</summary>
        public void SetLayer(int value) => layer = value;

        /// <summary>
        /// 整车深度公式（唯一来源）：世界 y 越低越靠近相机，排序层级越大越靠前。
        /// 场景里需要与车对齐深度的物体（如集装箱）必须用这个方法取深度，不要复制常量。
        /// </summary>
        public static float ComputeDepth(float worldY, int layer) =>
            -2f + worldY * 0.05f - (layer - 20) * 0.2f;

        void LateUpdate() => RefreshDepth();

        /// <summary>位置和动画更新后同步深度；层级验证也通过此入口检查移动车覆盖。</summary>
        public void RefreshDepth()
        {
            if (anchor == null || renderers == null) return;
            // 兜底：域重载或极早的调用可能让 block 还没创建，避免每帧抛 ArgumentNullException。
            block ??= new MaterialPropertyBlock();
            // 只改变 Shader 深度，保留 XY 外观、物理位置和完整的车内自遮挡。
            float depth = ComputeDepth(anchor.position.y, layer);
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
                    // 描边开关也在这里每帧重写，所以在「写的一方」决定要不要描边；
                    // 在读取方（附件组件）设零是无效的。
                    block.SetVector(OutlineCenterId, outlineSuppressed.Contains(renderer) ? Vector4.zero : outlineCenter);
                    renderer.SetPropertyBlock(block, slot);
                }
            }
        }
    }
}
