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
        MaterialPropertyBlock block;
        int layer = 20;
        static readonly int DepthId = Shader.PropertyToID("_CarDepth");
        static readonly int OutlineCenterId = Shader.PropertyToID("_CarOutlineCenter");

        /// <summary>绑定车辆根节点，所有独立网格共用同一排序基准。</summary>
        public void Initialize(Transform root)
        {
            anchor = root;
            renderers = GetComponentsInChildren<MeshRenderer>(true);
            materialCounts = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) materialCounts[i] = renderers[i].sharedMaterials.Length;
            block = new MaterialPropertyBlock();
        }

        /// <summary>沿用现有 SetLayer：30 为移动车，20 为停车与排队车辆。</summary>
        public void SetLayer(int value) => layer = value;

        void LateUpdate() => RefreshDepth();

        /// <summary>位置和动画更新后同步深度；层级验证也通过此入口检查移动车覆盖。</summary>
        public void RefreshDepth()
        {
            if (anchor == null || renderers == null) return;
            // 只改变 Shader 深度，保留 XY 外观、物理位置和完整的车内自遮挡。
            float depth = -2f + anchor.position.y * 0.05f - (layer - 20) * 0.2f;
            Vector3 center = transform.position;
            var outlineCenter = new Vector4(center.x, center.y, 0f, 1f);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                for (int slot = 0; slot < materialCounts[i]; slot++)
                {
                    renderer.GetPropertyBlock(block, slot);
                    block.SetFloat(DepthId, depth);
                    block.SetVector(OutlineCenterId, outlineCenter);
                    renderer.SetPropertyBlock(block, slot);
                }
            }
        }
    }
}
