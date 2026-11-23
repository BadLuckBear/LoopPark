using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 绑带车车顶附件：美术吸盘 + 美术 L 形弯头 + 程序化直套管。
    ///
    /// 最终形态（2026-11-24 定稿，几何依据见 StrapArt 的注释与实测数据）：
    ///   吸盘 xipan 贴死在车顶不动；L 形弯头 shengzi 的竖脚插在吸盘孔里，
    ///   只绕该竖脚轴线水平旋转，把 90° 的弯折当成「万向弯头」，让直口指向搭档车；
    ///   两车直口之间由程序化直套管补上，一头套进直口、一头钉在双方直口的中点。
    ///
    /// 为什么不再旋转/拉伸美术弯头：L 形件两个管口方向固定相差 90°，
    /// 「管口垂直插进吸盘」与「两端连线指向对方」互斥，靠调参永远对不上；
    /// 拆成「弯头只转方向 + 套管负责距离」之后，两个约束各自满足，且各占一次单一职责。
    ///
    /// 数据流（唯一写入者是 CarView / CarSkin3D，本组件不查询场景、不持有共享状态）：
    ///   CarView 配对完成 → SetLinked(true, partnerStrap)
    ///   CarSkin3D 收到车漆色与遮盖状态 → SetPaint / SetCovered
    /// 显示条件 = 属于一对绑带车，且没有被问号车布料盖住。
    ///
    /// 颜色写进 MaterialPropertyBlock（Strap.mat 被所有车共用，直接改材质会串色）；
    /// 读属性块时不清空，保留 CarLayerDepth 每帧写入的 _CarDepth。
    /// </summary>
    [DefaultExecutionOrder(100)]   // 排在 CarLayerDepth（默认 0）之后：颜色与深度都落笔在它后面
    public sealed class CarStrap : MonoBehaviour
    {
        [Tooltip("附件渲染器；留空时在 Awake 自动收集子渲染器。")]
        [SerializeField] Renderer[] renderers;
        [Tooltip("相对车身整车的深度偏置；负数压到车顶之上，取值需小于相邻车的深度间隔。")]
        [SerializeField] float depthBias = -0.06f;
        [Tooltip("弯头整体缩放：相邻两车过近、直口伸过两车中点时调小（0.7~1）。")]
        [Range(0.2f, 1.5f)][SerializeField] float elbowScale = 1f;
        [Tooltip("套管半径相对美术直口口径的比例。大于 1 = 套管像卡箍一样套在管口外侧；等于或小于 1 斜看会从单面壳管口透出背景。")]
        [Range(0.6f, 1.3f)][SerializeField] float hoseRadiusScale = 1f;
        [Tooltip("套管起点往直口内探进的深度，按口径的倍数计。保持 0：探进去会和美术管身重叠成 z-fighting（实测会在接缝处露出缺口）。")]
        [Range(0f, 6f)][SerializeField] float socketInset = 0f;
        [Tooltip("绑带色对车漆色的提亮量（朝白色插值）。默认 0 = 与车漆完全同色（弯头与车身一致）。" +
                 "只有在极暗车漆下觉得管子完全看不出受光时才调，例如 0.15。")]
        [Range(0f, 0.6f)][SerializeField] float hoseLift = 0f;
        [Tooltip("绑带调色资产（可空）。非空时它覆盖这里的颜色与材质观感 —— " +
                 "运行中用 LoopPark/绑带/实时调色 窗口改，改完保存，下次启动继续生效。")]
        [SerializeField] StrapTuningConfig tuning;

        [Header("发车扯断")]
        [Tooltip("拉长到原长的多少倍就立刻啪地扯断（推荐 1.15 ~ 1.25）。")]
        [SerializeField] float breakMaxStretch = 1.2f;
        [Tooltip("拉伸阶段最长持续时间（秒）。车开得慢时按时间兜底，保证一定断得掉。")]
        [SerializeField] float breakMaxSeconds = 0.14f;
        [Tooltip("扯断后，断管缩回直口的时间（秒）。")]
        [SerializeField] float retractSeconds = 0.10f;
        [Tooltip("扯断后，吸盘与弯头平滑缩小消失的时间（秒）。")]
        [SerializeField] float fadeOutSeconds = 0.22f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Glossiness");
        static readonly int SpecularId = Shader.PropertyToID("_SpecColor");

        int[] materialCounts;
        readonly StrapPaint paint = new StrapPaint();   // 上色单独成类：状态/姿态与颜色是两件事
        CarLayerDepth layerDepth;
        StrapArt art;
        StrapSleeve sleeve;

        // 搭档的 CarStrap：双方各自瞄准对方的吸盘轴线，套管终点取双方直口中点，
        // 两个中点由同一公式算出，必然重合。
        CarStrap partner;
        string hoseId;   // 关卡搭建时写入的唯一编号（见 SetHoseId），逐根调色的钥匙
        string hoseKey;  // 兜底标识（没写 hoseId 时用配对编号）
        bool built;      // 一次性装配已完成（节点默认 inactive，Awake 可能晚于 SetLinked）
        bool linked;    // 这辆车属于一对绑带
        bool covered;   // 问号车布料盖住了车身

        // ── 发车扯断演出（2026-11-24 加入）────────────────────────────────────────
        // 三段式：None（正常连接）→ Stretching（拉长一点点、变细、轻颤）
        //        → Fading（断管缩回直口，吸盘与弯头整体缩小消失）→ None。
        // 只有「第一辆车发车」会触发：GameController.Dispatch 对双方各调一次 SetLinked(false)，
        // 两边用同一基准距离与同一断口坐标，因此阈值与演出必然同步。
        /// <summary>扯断演出的阶段。</summary>
        enum BreakState { None, Stretching, Fading }

        BreakState breakState;
        CarStrap breakPartner;       // 断开的另一半；队友车可能先被回收，一律用 == null 判活
        float breakTimer;            // 当前阶段已用时（秒）
        float breakBaseDist = 0.1f;  // 开始拉伸那一刻双方吸盘的间距，作为拉伸倍率的基准
        Vector3 snapWorld;           // 「啪」那一下的断口世界坐标：两个断管都朝它缩回
        Vector3 baseLocalScale = Vector3.one;  // 附件节点原始缩放；淡出时按它乘系数

        /// <summary>绑带附件当前是否可见：属于一对绑带，或正在播发车扯断演出；问号车被盖住时一律不可见。</summary>
        public bool IsVisible => (linked || breakState != BreakState.None) && !covered;

        /// <summary>所属车辆本身是否已显示（未藏在屏幕外或车库里）。</summary>
        /// <remarks>
        /// 读父节点的激活链，而不是 gameObject.activeInHierarchy：本节点在预制体里默认 inactive，
        /// 由 Apply() 按 IsVisible 自行开关，读自己只会得到「附件自己的显隐」。
        /// 车辆被 GameController.UpdateLaneVisibility 关掉（滚出屏幕 / 还在车库）时整条父链失活，
        /// 搭档那侧靠这个判断收起套管，避免场上的车往空地伸出半根悬空管子。
        /// </remarks>
        bool HostCarVisible => transform.parent == null || transform.parent.gameObject.activeInHierarchy;

        /// <summary>套管节点；供诊断/编辑器工具读取。</summary>
        public Transform Hose => sleeve != null ? sleeve.Renderer.transform : null;

        /// <summary>本车吸盘孔的世界坐标；供诊断与将来的断开演出读取。</summary>
        public Vector3 SocketWorldPosition => art != null && art.Ready ? art.SocketWorld : transform.position;

        /// <summary>直口中心的世界坐标（套管起点按这个点再缩进）；供诊断工具读取。</summary>
        public Vector3 MuzzleWorldPosition => art != null && art.Ready ? art.MuzzleWorld : transform.position;

        /// <summary>直口此刻的口径（世界半径，已含父级非均匀缩放的影响）；供编辑器工具读取。</summary>
        public float MouthRadiusWorld => art != null && art.Ready ? art.MouthRadiusWorld : 0f;

        void Awake()
        {
            Build();
            Apply();
        }

        /// <summary>
        /// 一次性装配：收集渲染器、量件转身器、程序化套管、深度注册。
        /// 节点在预制体里默认 inactive，因此 SetLinked 可能先于 Awake 到来，这里要能重入。
        /// </summary>
        void Build()
        {
            if (built) return;
            built = true;
            baseLocalScale = transform.localScale;   // 记下原始缩放：淡出阶段按它乘系数，结束时原样还原
            Collect();
            art = new StrapArt(transform, renderers) { Scale = elbowScale };
            art.Setup();
            RegisterDepth();
        }

        /// <summary>
        /// 套管按需创建：只有真正配对（要画连接）的车才建。
        /// 顺手把「每辆车都在 Awake 建一根」改成懒创建 —— 否则整关几百辆车会各留一根空套管对象，
        /// Hierarchy 里堆成一片，纯浪费（它们是场景根对象，不跟着车身层级走）。
        /// </summary>
        StrapSleeve EnsureSleeve()
        {
            if (sleeve != null) return sleeve;
            // 套管材质直接派生自美术绑带正在用的材质（Strap.mat）：颜色/贴图/光滑度与美术件完全同源，
            // 只有 shader 换成双面版（开壳必需）。找不到材质时 StrapSleeve 自己会退回 Layered Hose 默认材质。
            Material source = null;
            if (renderers != null)
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    source = renderers[i].sharedMaterial;
                    if (source != null) break;
                }
            sleeve = new StrapSleeve("StrapSleeve", source);
            RegisterDepth();   // 新渲染器要补注册进本车的深度列表（Register 自身幂等）
            return sleeve;
        }

        void Collect()
        {
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>(true);
            materialCounts = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                materialCounts[i] = renderers[i] != null ? renderers[i].sharedMaterials.Length : 0;
        }

        /// <summary>
        /// 向同一辆车的 CarLayerDepth 注册网格：车辆 Shader 会把世界 z 压缩到 _CarDepth 附近，
        /// 附件与车顶的真实深度差小于像素精度，必须靠偏置显式压到车顶之上。
        /// 套管是横跨两车的细长件，注册时要求抑制描边，否则描边 pass 会把端部径向撑成黑环。
        /// </summary>
        void RegisterDepth()
        {
            layerDepth ??= GetComponentInParent<CarLayerDepth>();
            if (layerDepth == null) return;
            foreach (var renderer in renderers)
                if (renderer != null) layerDepth.Register(renderer, depthBias);
            if (sleeve != null) layerDepth.Register(sleeve.Renderer, depthBias, true);
        }

        /// <summary>
        /// 绑带配对开关：只有成对车辆才显示车顶附件，弯头与套管都朝向搭档。
        /// 关闭时若此前确实连着，就进入扯断演出（而不是整根直接消失）：
        /// 记录搭档与此刻的吸盘间距作基准，交给 RefreshStrap 的 Stretching 阶段逐步拉长。
        /// </summary>
        public void SetLinked(bool on, CarStrap target)
        {
            Build();
            if (!on && linked && breakState == BreakState.None && target != null)
            {
                breakPartner = target;
                breakBaseDist = Mathf.Max(0.02f, Vector3.Distance(SocketWorldPosition, target.SocketWorldPosition));
                breakTimer = 0f;
                breakState = BreakState.Stretching;
            }
            else if (on)
            {
                breakState = BreakState.None;   // 重新配对 / 复用：清掉上一次的演出残留
                breakPartner = null;
                transform.localScale = baseLocalScale;
            }

            partner = on ? target : null;
            linked = on;
            Apply();
            RefreshStrap();
            if (on && target != null) target.RefreshStrap();
        }

        /// <summary>
        /// 从拉伸切到收尾。由先达到阈值的一方调用，并顺手把同一断口坐标推给搭档，
        /// 保证两根断管从同一点缩回；重复调用（另一方晚一帧到阈值）直接忽略。
        /// </summary>
        void EnterFading(Vector3 snapPoint)
        {
            if (breakState != BreakState.Stretching) return;
            breakState = BreakState.Fading;
            snapWorld = snapPoint;
            breakTimer = 0f;
        }

        /// <summary>问号车遮盖状态：被布料盖住时同步隐藏，避免从布缝里露出车顶附件。</summary>
        public void SetCovered(bool on)
        {
            if (covered == on) return;
            covered = on;
            Apply();
        }

        /// <summary>跟随车漆上色；光滑度与高光沿用该颜色编号的外观配置。
        /// 绑带不是车漆本体：朝白色提 hoseLift（美术弯头与套管用同一个提亮后颜色，接缝不会出现明暗台阶）。</summary>
        public void SetPaint(Color color, float carSmoothness, float carSpecular)
        {
            paint.Set(color, carSmoothness, carSpecular, hoseLift);
            Apply();
        }

        void Apply()
        {
            Build();
            bool visible = IsVisible;
            // 用节点开关而不是 Renderer.enabled：节点关闭后不再提交 Draw Call，
            // 属性块内容保留，再次显示时直接复用。
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) sleeve?.Hide();  // 节点被关掉时 LateUpdate 不再跑，套管必须在这里收
            if (visible) ApplyColors();
        }

        // CarLayerDepth 每帧都会重写整车属性块，这里在它之后补写颜色，两者才能共存。
        // 颜色补写放在 RefreshStrap 内部（Show 之后）：套管是 Show 那一刻才存在的渲染器，
        // 先写颜色会漏掉它，而编辑模式下没有 LateUpdate 帮忙补。
        void LateUpdate() => RefreshStrap();

        /// <summary>
        /// 每帧姿态。三个分支各自单一职责：
        ///   正常连接 —— 弯头水平指向搭档的吸盘轴线 → 取双方直口中点 → 重画套管；
        ///   拉伸     —— 车刚发车把距离拉开，套管按倍率变细并轻颤，到阈值立刻同步扯断；
        ///   收尾     —— 断管缩回自己直口，整根附件（吸盘 + 弯头）按原缩放平滑缩到 0。
        /// 全部在世界空间算位置，父级的非均匀缩放不会进入公式。
        /// </summary>
        void RefreshStrap()
        {
            if (!built) return;
            // 演出用真实帧时间；编辑器里 deltaTime 可能为 0（失焦/暂停），退回 unscaled 保证还推进。
            float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;

            // 阶段 1：第一辆车刚发车，拉长一点点、变细、轻颤 -> 达到阈值双方同步扯断
            if (breakState == BreakState.Stretching)
            {
                if (covered || art == null || !art.Ready || breakPartner == null || breakPartner.art == null || !breakPartner.art.Ready)
                {
                    FinishBreak();
                    return;
                }

                art.AimAt(breakPartner.SocketWorldPosition);
                Vector3 mine = art.MuzzleWorld;
                Vector3 theirs = breakPartner.art.MuzzleWorld;
                Vector3 mid = (mine + theirs) * 0.5f;

                float dist = Vector3.Distance(SocketWorldPosition, breakPartner.SocketWorldPosition);
                float ratio = dist / Mathf.Max(0.01f, breakBaseDist);
                breakTimer += dt;

                if (ratio >= breakMaxStretch || breakTimer >= breakMaxSeconds)
                {
                    EnterFading(mid);                    // 断口定在双方中点，两边从同一点缩回
                    breakPartner.EnterFading(mid);
                    return;
                }

                Vector3 span = mid - mine;
                if (span.sqrMagnitude > 1e-8f)
                {
                    float baseRadius = Mathf.Max(1e-4f, art.MouthRadiusWorld * hoseRadiusScale);
                    float thinRadius = baseRadius / Mathf.Sqrt(Mathf.Max(1f, ratio));   // 拉得越长越细，像橡皮筋
                    Vector3 dir = span.normalized;
                    Vector3 perp = Vector3.Cross(dir, Vector3.forward);
                    if (perp.sqrMagnitude < 1e-4f) perp = Vector3.Cross(dir, Vector3.up);
                    float tension = Mathf.Clamp01((ratio - 1f) / Mathf.Max(0.05f, breakMaxStretch - 1f));
                    Vector3 jitter = perp.normalized * (Mathf.Sin(Time.time * 75f) * (0.008f + 0.016f * tension));

                    Vector3 start = mine - dir * (thinRadius * socketInset);
                    EnsureSleeve().Show(start, mid + jitter, thinRadius);
                }
                ApplyColors();
                return;
            }

            // 阶段 2：已扯断 -> 断管迅速缩回直口，吸盘和弯头平滑缩小消失
            if (breakState == BreakState.Fading)
            {
                if (covered || art == null || !art.Ready)
                {
                    FinishBreak();
                    return;
                }

                breakTimer += dt;
                art.AimAt(snapWorld);
                Vector3 mine = art.MuzzleWorld;

                float rk = Mathf.Clamp01(breakTimer / Mathf.Max(0.01f, retractSeconds));
                if (rk < 1f)
                {
                    float ease = 1f - Mathf.Pow(1f - rk, 3f);   // 先快后慢：像被弹回弯头里
                    Vector3 tip = Vector3.Lerp(snapWorld, mine, ease);
                    float baseRadius = Mathf.Max(1e-4f, art.MouthRadiusWorld * hoseRadiusScale);
                    float r = baseRadius * Mathf.Lerp(0.75f, 0.1f, ease);
                    EnsureSleeve().Show(mine, tip, r);
                }
                else
                {
                    sleeve?.Hide();
                }

                // 附件节点整体缩小：吸盘与弯头一起退场，不需要逐个改 Renderer.enabled
                float fk = Mathf.Clamp01(breakTimer / Mathf.Max(0.01f, fadeOutSeconds));
                float scaleFactor = 1f - Mathf.SmoothStep(0f, 1f, fk);
                transform.localScale = baseLocalScale * scaleFactor;

                if (fk >= 1f)
                {
                    FinishBreak();
                    return;
                }

                ApplyColors();
                return;
            }

            // 阶段 0：正常连接 —— 只有双方车辆都已经出来显示了，才画套管
            // （另一半还在车库/屏幕外时，场上这辆不许往空地伸管子；两车都出来后自然接上）
            bool usable = linked && !covered && partner != null
                && HostCarVisible && partner.HostCarVisible
                && art != null && art.Ready && partner.art != null && partner.art.Ready;
            if (!usable) { sleeve?.Hide(); ApplyColors(); return; }

            art.AimAt(partner.SocketWorldPosition);
            Vector3 myMuzzle = art.MuzzleWorld;
            Vector3 theirMuzzle = partner.art.MuzzleWorld;
            Vector3 center = (myMuzzle + theirMuzzle) * 0.5f;
            Vector3 normalSpan = center - myMuzzle;
            if (normalSpan.sqrMagnitude < 1e-8f) { sleeve?.Hide(); return; }

            float radius = Mathf.Max(1e-4f, art.MouthRadiusWorld * hoseRadiusScale);
            Vector3 normalStart = myMuzzle - normalSpan.normalized * (radius * socketInset);
            EnsureSleeve().Show(normalStart, center, radius);
            ApplyColors();
        }

        /// <summary>
        /// 演出收尾：清状态、还原缩放、收起套管，最后由 Apply 按 IsVisible 把节点关掉（不再提交 Draw Call）。
        /// </summary>
        void FinishBreak()
        {
            breakState = BreakState.None;
            breakPartner = null;
            transform.localScale = baseLocalScale;
            sleeve?.Hide();
            Apply();
        }

        void OnDisable()
        {
            // 车停用/回收时先收起套管；演出被打断也要还原缩放，避免下次复用还是一根缩小的残件。
            if (sleeve != null) sleeve.Hide();
            if (built) transform.localScale = baseLocalScale;
        }

        void OnEnable()
        {
            if (!built) return;
            Apply();
            RefreshStrap();
        }

        /// <summary>这一根生成套管的唯一编号（由关卡搭建时按「车道_车道内序号」写入，
        /// 同一关重跑不变）。调色资产就是按它逐根存颜色，所以每根可以不一样。</summary>
        public void SetHoseId(string id) => hoseId = id;

        /// <summary>逐根调色的钥匙；未写入时退回配对编号（只用于兜底显示，不用于资产命中）。</summary>
        public string HoseKey
        {
            get
            {
                if (!string.IsNullOrEmpty(hoseId)) return hoseId;
                if (!string.IsNullOrEmpty(hoseKey)) return hoseKey;
                var view = GetComponentInParent<CarView>();
                hoseKey = view != null && view.link > 0 ? "link" + view.link : "unlinked";
                return hoseKey;
            }
        }

        /// <summary>车漆上色：颜色优先级 = 单根覆盖 → 全局规则（调色资产）→ 本车车漆色。
        /// 每帧都读资产，所以 Play 中改完立刻见效，不需要重启或重新生成任何东西。</summary>
        void ApplyColors()
        {
            if (!IsVisible) return;
            var sleeveRenderer = sleeve != null && sleeve.Visible ? sleeve.Renderer : null;
            if (tuning == null)
            {
                paint.Apply(renderers, materialCounts, sleeveRenderer);
                return;
            }

            Color strapColor;
            if (tuning.TryGetHoseColor(HoseKey, out Color hoseOverride))
            {
                // 单根指定色：整根管（两半）都用它
                strapColor = hoseOverride;
            }
            else
            {
                strapColor = tuning.useCarPaint ? paint.Display : tuning.hoseColor;
            }
            Color artColor = tuning.affectsArt ? strapColor : paint.Display;
            paint.Apply(renderers, materialCounts, sleeveRenderer, artColor, strapColor,
                        tuning.smoothness, tuning.specular);
        }

        void OnDestroy()
        {
            sleeve?.Dispose();   // 套管挂在场景根，不随车身销毁，必须自己回收
        }
    }
}



