using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 绑带车车顶附件（吸盘 + 软管）。
    ///
    /// 数据流（唯一写入者是 CarSkin3D / CarView，本组件不查询场景、不持有共享状态）：
    ///   CarView 完成绑带配对 → SetLinked(true)
    ///   CarSkin3D 收到车漆色与遮盖状态 → SetPaint / SetCovered
    /// 显示条件 = 属于一对绑带车，且没有被问号车布料盖住。
    ///
    /// 姿态规则：吸盘永远不动（贴死在车顶、盘面保持水平），只有软管绕自己的根部
    /// 旋转并沿轴向拉伸去够搭档车。早期版本旋转整个附件节点，吸盘会跟着歪掉、
    /// 并被绕车身中心甩到车顶边缘 —— 一根一体化的刚体做不出“吸盘贴车顶 + 软管转向”。
    ///
    /// 颜色写进 MaterialPropertyBlock 而不是共享材质，避免一辆车上色串到同材质的所有车；
    /// 读取属性块时不清空，保留 CarLayerDepth 每帧写入的 _CarDepth（车辆跨网格排序必需）。
    /// </summary>
    [DefaultExecutionOrder(100)] // 排在 CarLayerDepth（默认 0）之后，颜色补写在整车深度刷新之后
    // 备注：只在吸盘处转动软管节点，不动整个附件，避免吸盘歪掉或漂到车顶边缘。
    public sealed class CarStrap : MonoBehaviour
    {
        [Tooltip("附件渲染器；留空时在 Awake 自动收集子渲染器。")]
        [SerializeField] Renderer[] renderers;
        [Tooltip("相对车身整车的深度偏置；负数把它压到车顶之上，取值需小于相邻车的深度间隔。")]
        [SerializeField] float depthBias = -0.06f;
        [Tooltip("软管在模型局部空间的朝向；shengzi.fbx 的软管沿 -Z 伸出，配对后指向搭档车。")]
        [SerializeField] Vector3 hoseAxisLocal = new Vector3(0f, 0f, -1f);
        [Tooltip("软管最大拉伸倍数；超过后不再加长，避免远距离时拉成细面。")]
        [SerializeField] float maxHoseStretch = 8f;
        [Tooltip("软管最小收缩倍数：近距离配对时要能把钩形缩短，否则够不到两车中点。")]
        [SerializeField] float minHoseStretch = 0.2f;
        [Tooltip("软管竖直方向的压扁比例：把模型自带的弯弧压低，拉长后更像贴车顶的绑带而不是拱门。")]
        [Range(0.05f, 1f)][SerializeField] float hoseFlatten = 0.35f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Glossiness");
        static readonly int SpecularId = Shader.PropertyToID("_SpecColor");

        int[] materialCounts;
        MaterialPropertyBlock block;
        CarLayerDepth layerDepth;
        // 搭档的 CarStrap：双方都用对方的软管节点算中点，两边的中点才会是同一个点。
        CarStrap partner;
        // 软管节点（模型里最长的那个网格）与它的原始长度/姿态，配对时只改这一段。
        Transform hose;
        float hoseTipLength;
        float hoseBaseScaleY = 1f;
        float hoseBaseScaleZ = 1f;
        Quaternion hoseBaseRotation = Quaternion.identity;
        Quaternion axisFix = Quaternion.identity;
        bool linked;    // 这辆车属于一对绑带
        bool covered;   // 问号车布料盖住了车身
        Color paint = Color.white;
        float smoothness = 0.18f;
        float specular = 0.08f;

        /// <summary>绑带附件当前是否可见；供编辑器检查工具读取。</summary>
        public bool IsVisible => linked && !covered;

        /// <summary>软管节点（旋转与拉伸的唯一对象）；供编辑器预览工具读取。</summary>
        public Transform Hose => hose;

        void Awake()
        {
            axisFix = Quaternion.FromToRotation(Vector3.back, hoseAxisLocal.normalized);
            Collect();
            Apply();
        }

        void Collect()
        {
            if (renderers != null && renderers.Length > 0 && materialCounts != null) return;
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>(true);
            materialCounts = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                materialCounts[i] = renderers[i] != null ? renderers[i].sharedMaterials.Length : 0;
            FindHose();
        }

        /// <summary>
        /// 识别软管节点：模型里沿局部 z 最长的网格（吸盘短而宽）。只动这一段，
        /// 吸盘保持原尺寸与原姿态；记录原始长度/姿态，用来按两车距离换算拉伸倍数。
        /// </summary>
        void FindHose()
        {
            float longest = 0f;
            foreach (var renderer in renderers)
            {
                var filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                if (filter == null || filter.sharedMesh == null) continue;
                var bounds = filter.sharedMesh.bounds;
                if (bounds.size.z <= longest) continue;
                longest = bounds.size.z;
                hose = renderer.transform;
                // 软管沿 -z 伸出：末端到节点原点的距离（网格局部单位）。节点原点就在吸盘处，
                // 所以绕它旋转时软管根部不动，只有末端扫过车顶。
                hoseTipLength = Mathf.Max(0.0001f, -bounds.min.z);
            }
            if (hose != null)
            {
                hoseBaseScaleY = hose.localScale.y;
                hoseBaseScaleZ = hose.localScale.z;
                hoseBaseRotation = hose.localRotation;
            }
        }

        /// <summary>
        /// 向同一辆车的 CarLayerDepth 注册附件网格：车辆 Shader 会把世界 z 压缩到 _CarDepth 附近，
        /// 吸盘与车顶的自然深度差小于像素精度，必须靠偏置显式压到车顶之上。
        /// </summary>
        void RegisterDepth()
        {
            layerDepth ??= GetComponentInParent<CarLayerDepth>();
            if (layerDepth == null || renderers == null) return;
            foreach (var renderer in renderers)
                if (renderer != null) layerDepth.Register(renderer, depthBias);
        }

        /// <summary>绑带配对开关：只有成对车辆才显示车顶吸盘，并把软管朝向搭档车。</summary>
        public void SetLinked(bool on, CarStrap target)
        {
            partner = on ? target : null;
            linked = on;
            // 立即对齐一次：LateUpdate 每帧也会跟，但配对当帧（以及编辑器静态渲染）就要是正确姿态。
            Apply();
            ApplyAim();
        }

        /// <summary>问号车遮盖状态：被布料盖住时同步隐藏，避免从布缝里露出车顶附件。</summary>
        public void SetCovered(bool on)
        {
            if (covered == on) return;
            covered = on;
            Apply();
        }

        /// <summary>跟随车漆上色；光滑度与高光沿用该颜色编号的外观配置。</summary>
        public void SetPaint(Color color, float carSmoothness, float carSpecular)
        {
            paint = color;
            smoothness = carSmoothness;
            specular = carSpecular;
            Apply();
        }

        void Apply()
        {
            Collect();
            RegisterDepth();
            bool visible = IsVisible;
            // 用节点开关而不是 Renderer.enabled：CarLayerDepth 会持续给本节点写深度，
            // 节点关闭后不再提交 Draw Call，同时保留属性块内容供再次显示时复用。
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (visible) ApplyColors();
        }

        // CarLayerDepth 每帧都会重写整车属性块；这里在它之后补写颜色，保证两者共存。
        // 同时按搭档位置转动软管：两辆车的软管相向伸出，才能对接成一条绑带。
        void LateUpdate()
        {
            ApplyColors();
            ApplyAim();
        }

        /// <summary>
        /// 只转动软管节点：吸盘固定在车顶不动，软管绕自身根部（吸盘处）转向搭档，
        /// 每根伸到两车中点，两根头对头接上。车辆移动时每帧重算。
        /// </summary>
        void ApplyAim()
        {
            if (hose == null) return;

            if (partner == null || !linked)
            {
                // 取消绑带：软管回到预制体原始姿态。
                hose.localRotation = hoseBaseRotation;
                SetHoseStretch(hoseBaseScaleZ, hoseBaseScaleY);
                return;
            }

            var anchor = hose.parent;   // 软管的父节点，用它的旋转把世界朝向换算成局部朝向
            if (anchor == null) return;

            // 中点取“两根软管起点”的中点：两边算出的点是同一个，而且各自到它等距，
            // 两根软管才会各伸一半、在中点头对头接上（用车身中心算会因为吸盘偏移而一长一短）。
            Vector3 partnerPoint = partner.hose != null ? partner.hose.position : partner.transform.position;
            Vector3 midPoint = (hose.position + partnerPoint) * 0.5f;
            Vector3 toMid = midPoint - hose.position;
            float target = toMid.magnitude;
            if (target < 1e-4f) return;

            // 只用 localRotation：车身父级是非均匀缩放（1.55/1.3/1.3），直接写世界 rotation
            // 会让 Unity 反算局部矩阵时带上 shear，朝向和位置都会歪。
            Vector3 up = transform.parent != null ? transform.parent.up : Vector3.up;
            Quaternion worldRot = Quaternion.LookRotation(-toMid.normalized, up) * axisFix;
            hose.localRotation = Quaternion.Inverse(anchor.rotation) * worldRot;

            // 拉伸：软管局部 z 就是它的长度方向，按“到中点距离 / 当前长度”换算，
            // 同时把竖直弯弧压扁，让软管贴着车顶伸过去而不是拱成一道门。
            Vector3 tip = hose.TransformPoint(new Vector3(0f, 0f, -hoseTipLength));
            float current = Vector3.Distance(hose.position, tip);
            if (current < 1e-4f) return;
            float stretch = Mathf.Clamp(hose.localScale.z * (target / current),
                hoseBaseScaleZ * minHoseStretch, hoseBaseScaleZ * maxHoseStretch);
            SetHoseStretch(stretch, hoseBaseScaleY * hoseFlatten);
        }

        void SetHoseStretch(float scaleZ, float scaleY)
        {
            var scale = hose.localScale;
            if (Mathf.Approximately(scale.z, scaleZ) && Mathf.Approximately(scale.y, scaleY)) return;
            hose.localScale = new Vector3(scale.x, scaleY, scaleZ);
        }

        void ApplyColors()
        {
            if (!IsVisible || renderers == null || materialCounts == null) return;
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                for (int slot = 0; slot < materialCounts[i]; slot++)
                {
                    renderer.GetPropertyBlock(block, slot);
                    block.SetColor(ColorId, paint);
                    block.SetColor(BaseColorId, paint);
                    block.SetFloat(SmoothnessId, smoothness);
                    block.SetColor(SpecularId, Color.white * specular);
                    renderer.SetPropertyBlock(block, slot);
                }
            }
        }
    }
}
