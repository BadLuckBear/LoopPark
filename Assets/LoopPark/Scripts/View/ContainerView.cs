using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 3D 集装箱视图：把 jizhuangxiang 外壳按「列宽 × 列高」自适应缩放，并把整壳摆到列内车辆的
    /// 排序带前后：背板落在所有车之后（开箱后车在背板之前可见），顶盖落在列内最前物体之前
    /// （未开箱时挡住车与问号徽章）。深度用真实 Z，不参与 CarLayered 的 z 压缩 —— 因此这里必须显式
    /// 按 CarLayerDepth.ComputeDepth 算出的车深度来定带子，不能凭模型包围盒猜。
    ///
    /// 层级约定（见 Container3D.prefab）：
    ///   Container3D(本组件) → Fit(只缩放/居中, 无旋转) → VisualAxis(只旋转 -90,0,0) → RoofPivot / FrameRoot
    /// 旋转与缩放必须分居两层：模型长轴是 Z、高是 Y，旋转后靠缩放轴映射到世界 X/Y/Z，同一节点既转又
    /// 非均匀缩放会让「宽高」用到错误的轴。
    ///
    /// 数据流：GameController.BuildGarage → Setup(...)；开箱由 GarageProgress 调 ContainerOpenAnimator。
    /// </summary>
    public sealed class ContainerView : MonoBehaviour
    {
        [Header("Hierarchy references (inside prefab)")]
        [Tooltip("Scale/centering-only node; must not be rotated itself")]
        [SerializeField] Transform _fitRoot;
        [Tooltip("Orientation-only node; must be (-90,0,0)")]
        [SerializeField] Transform _visualAxis;
        [SerializeField] Transform _roofPivot;
        [SerializeField] Transform _topClipAnchor;
        [Tooltip("End posts / corner rings (polySurface43 / polySurface48) used to cancel non-uniform vertical stretch")]
        [SerializeField] Transform _topCap;
        [SerializeField] Transform _bottomCap;
        [SerializeField] ContainerColorIcon _colorIcon;
        [SerializeField] ContainerOpenAnimator _animator;

        // 尺寸/深度/提示物参数统一在 LoopParkSkin（SkinConfig）里调，见「集装箱外壳」分组；
        // 本组件只负责把列尺寸和列内车辆深度变成箱体姿态，自己不留可调数值。
        // 动画时长同样来自 SkinConfig，由 ContainerOpenAnimator 在 Initialize 时读取。

        static readonly Quaternion ExpectedAxisRotation = Quaternion.Euler(-90f, 0f, 0f);

        Bounds _modelBounds;      // VisualAxis 局部空间的整壳包围盒
        float _roofMaxLocalY;     // 顶盖在模型局部 +Y 上的最高点

        // 两端盖的原始姿态（相对 FrameRoot，FrameRoot 与 VisualAxis 同原点）与局部包围盒，
        // 用于抵消「模型局部 Z（→世界 Y）被放大 2 倍以上」造成的角环纵向拉长。
        Vector3 _topCapBaseLocalPos, _topCapBaseLocalScale;
        Vector3 _bottomCapBaseLocalPos, _bottomCapBaseLocalScale;
        Bounds _topCapLocalBounds, _bottomCapLocalBounds;
        bool _capsCached;
        bool _ready;

        /// <summary>适配后的整壳高度（世界单位），供验证/调试读取。</summary>
        public float FittedHeight { get; private set; }
        /// <summary>整壳背板所在的世界 Z。</summary>
        public float DepthBack { get; private set; }
        /// <summary>整壳正面（顶盖平面）所在的世界 Z。</summary>
        public float DepthFront { get; private set; }

        /// <summary>
        /// 摆好集装箱。调用方先设置本地 x/y（列中心），本方法负责算 z、缩放与提示物。
        /// </summary>
        /// <param name="columnWidth">列宽（世界单位，一般 = cell）</param>
        /// <param name="columnHeight">列高（世界单位，一般 = ColumnHeight）</param>
        /// <param name="cell">棋盘格宽，用于提示物尺寸与贴边补偿</param>
        /// <param name="carFrontDepth">列内最靠前物体的 Z（取车身与问号徽章中更靠前者）</param>
        /// <param name="carBackDepth">列内最靠后车身的 Z</param>
        /// <param name="unlockColor">解锁颜色索引，负数表示任意颜色</param>
        public void Setup(float columnWidth, float columnHeight, float cell, float carFrontDepth, float carBackDepth, int unlockColor)
        {
            if (!CacheModelBounds()) return;

            if (!_ready && !IsAxisRotationValid())
                Debug.LogError("[ContainerView] VisualAxis rotation is not (-90,0,0); size fitting will be off", this);

            // 可调参数全部来自 LoopParkSkin，改完对新生成的集装箱立即生效
            var skin = SkinConfig.Instance;
            float targetWidth = columnWidth * skin.containerWidthScale;
            float targetHeight = columnHeight + skin.containerHeightExtraCells * cell;

            var size = _modelBounds.size;
            if (size.x < 1e-4f || size.y < 1e-4f || size.z < 1e-4f)
            {
                Debug.LogError("[ContainerView] Degenerate model bounds, cannot fit size", this);
                return;
            }

            // 模型轴映射：局部 X→世界X(宽)、局部 Z→世界Y(高)、局部 Y→世界Z(厚)
            float sx = targetWidth / size.x;
            float sy = targetHeight / size.z;

            // 深度带：背板在所有车之后，顶盖（模型 +Y 最高点）在列内最前物体之前
            float roofLocalY = Mathf.Max(0.01f, _roofMaxLocalY - _modelBounds.min.y);
            DepthBack = carBackDepth + skin.containerBackMargin;
            float needDepth = DepthBack - carFrontDepth + skin.containerFrontMargin;
            float sz = Mathf.Max(0.01f, needDepth / roofLocalY);
            DepthFront = DepthBack - sz * roofLocalY;

            _fitRoot.localScale = new Vector3(sx, sy, sz);
            // 两端角环做纵向反拉伸，保证上下边缘仍贴齐整壳
            RestoreCapAspect(sx, sy);
            // 居中：模型中心对到 x/y 原点，模型最低面（背板）对到本节点 z=0
            var c = _modelBounds.center;
            _fitRoot.localPosition = new Vector3(-sx * c.x, -sy * c.z, sz * _modelBounds.min.y);

            var lp = transform.localPosition;
            transform.localPosition = new Vector3(lp.x, lp.y, DepthBack);

            FittedHeight = targetHeight;
            if (_topClipAnchor != null)
                _topClipAnchor.localPosition = new Vector3(0f, targetHeight * 0.5f, 0f);

            float clipWorldY = _topClipAnchor != null
                ? _topClipAnchor.position.y
                : transform.position.y + targetHeight * 0.5f;

            if (_animator != null) _animator.Initialize(targetHeight, clipWorldY);

            if (_colorIcon != null)
            {
                float iconWidth = Mathf.Max(0.01f, cell * skin.containerIconSizeCells);
                // 提示物摆在箱体正中心（不再顶在最上边），深度仍贴在顶盖之前
                var iconPos = new Vector3(
                    transform.position.x,
                    transform.position.y,
                    DepthFront - skin.containerIconFrontOffset);
                _colorIcon.Configure(unlockColor, iconWidth, iconPos);
            }

            _ready = true;
        }

        bool IsAxisRotationValid() =>
            _visualAxis == null || Quaternion.Angle(_visualAxis.localRotation, ExpectedAxisRotation) < 0.5f;

        /// <summary>
        /// 播放开箱动画。3D 外壳动画结束后销毁自身；未挂动画组件时直接销毁，避免残留空壳。
        /// 由 GameController.GarageProgress 在解锁条件满足时调用。
        /// </summary>
        public void PlayOpen(System.Action onComplete = null)
        {
            if (_animator != null)
            {
                _animator.PlayOpen(onComplete);
                return;
            }
            onComplete?.Invoke();
            Destroy(gameObject);
        }

        bool CacheModelBounds()
        {
            if (_visualAxis == null || _fitRoot == null)
            {
                Debug.LogError("[ContainerView] Missing hierarchy references (_fitRoot/_visualAxis)", this);
                return false;
            }
            if (!TryLocalBounds(_visualAxis, _visualAxis, out _modelBounds))
            {
                Debug.LogError("[ContainerView] No usable mesh under VisualAxis", this);
                return false;
            }
            if (_roofPivot != null && TryLocalBounds(_visualAxis, _roofPivot, out var roofBounds))
                _roofMaxLocalY = roofBounds.max.y;
            else
                _roofMaxLocalY = _modelBounds.max.y;

            // 只在首次缓存：Setup 可能被重复调用，基准姿态必须是「未补偿」的那一份
            if (!_capsCached)
            {
                if (_topCap != null)
                {
                    _topCapBaseLocalPos = _topCap.localPosition;
                    _topCapBaseLocalScale = _topCap.localScale;
                    TryLocalBounds(_visualAxis, _topCap, out _topCapLocalBounds);
                }
                if (_bottomCap != null)
                {
                    _bottomCapBaseLocalPos = _bottomCap.localPosition;
                    _bottomCapBaseLocalScale = _bottomCap.localScale;
                    TryLocalBounds(_visualAxis, _bottomCap, out _bottomCapLocalBounds);
                }
                _capsCached = true;
            }
            return true;
        }

        /// <summary>
        /// 抵消两端盖的纵向非均匀拉伸：
        /// 模型局部 Z 映射到世界 Y，被 sy 放大；把端盖自身的局部 Z 缩比乘上 sx/sy，
        /// 使其最终截面缩放与 X 一致（角环恢复正圆），再按外缘位置补一次平移，
        /// 保证上端盖仍贴 _modelBounds.max.z、下端盖仍贴 _modelBounds.min.z。
        /// </summary>
        void RestoreCapAspect(float sx, float sy)
        {
            if (sy < 1e-4f) return;
            float zCompensate = sx / sy;

            if (_topCap != null)
            {
                var s = _topCapBaseLocalScale;
                _topCap.localScale = new Vector3(s.x, s.y, s.z * zCompensate);
                float outerZ = _topCapLocalBounds.max.z;
                float offset = (outerZ - _topCapBaseLocalPos.z) * (1f - zCompensate);
                var p = _topCapBaseLocalPos;
                _topCap.localPosition = new Vector3(p.x, p.y, p.z + offset);
            }

            if (_bottomCap != null)
            {
                var s = _bottomCapBaseLocalScale;
                _bottomCap.localScale = new Vector3(s.x, s.y, s.z * zCompensate);
                float outerZ = _bottomCapLocalBounds.min.z;
                float offset = (outerZ - _bottomCapBaseLocalPos.z) * (1f - zCompensate);
                var p = _bottomCapBaseLocalPos;
                _bottomCap.localPosition = new Vector3(p.x, p.y, p.z + offset);
            }
        }

        /// <summary>
        /// 在指定空间的局部轴下累计包围盒。
        /// 用 MeshFilter.sharedMesh.bounds（不依赖 Renderer.bounds / 激活状态），
        /// 避免父级旋转把世界 AABB 变成误导性的立方体。
        /// </summary>
        static bool TryLocalBounds(Transform space, Transform root, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mesh = filters[i].sharedMesh;
                if (mesh == null) continue;
                var mb = mesh.bounds;
                var toSpace = space.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Vector3(
                        (corner & 1) == 0 ? mb.min.x : mb.max.x,
                        (corner & 2) == 0 ? mb.min.y : mb.max.y,
                        (corner & 4) == 0 ? mb.min.z : mb.max.z);
                    var q = toSpace.MultiplyPoint3x4(p);
                    if (!has) { bounds = new Bounds(q, Vector3.zero); has = true; }
                    else bounds.Encapsulate(q);
                }
            }
            return has;
        }
    }
}
