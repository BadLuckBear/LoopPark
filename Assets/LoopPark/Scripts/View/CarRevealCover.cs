using System.Collections;
using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 问号车的车衣覆盖层（状态与姿态）：
    /// 平时整块车衣遮住车身；显色时切换到拆分衣片并播放一次爆衣动画，结束后彻底隐藏覆盖层。
    ///
    /// 结构约定（由 CarModelSetup 生成）：
    ///   Cover（本组件 + CoverBurst，承担整体姿态）
    ///     ├─ Buliao      整体车衣，静态遮盖用，1 个网格
    ///     └─ BuliaoPosui 拆分衣片，只在爆衣那 0.4s 内启用
    /// 这样正常遮盖时每辆车仍只有 1 个 Draw Call，只有爆衣瞬间才付 42 片的代价。
    ///
    /// 没有拆分衣片的老预制体仍走原来的「整块车衣抛出」动画，行为不变。
    /// </summary>
    public sealed class CarRevealCover : MonoBehaviour
    {
        [Header("爆衣动画")]
        [Tooltip("动画时长，单位为秒。")]
        [SerializeField] float duration = 0.42f;
        [Tooltip("（无拆分衣片时）整块车衣沿车辆长度方向抛出的本地距离。")]
        [SerializeField] float burstDistance = 0.28f;
        [Tooltip("（无拆分衣片时）车衣爆开时的额外放大比例。")]
        [SerializeField] float burstScale = 1.28f;
        [Tooltip("（无拆分衣片时）车衣爆开时绕模型深度轴旋转的角度。")]
        [SerializeField] float spinDegrees = 24f;
        [Tooltip("拆分衣片的爆衣动画组件；留空时从本节点自动获取。")]
        public CoverBurst shardBurst;
        [Header("上方停车区姿态")]
        // 停车区与发车区共用同一车衣中心，避免上方停车列的车衣整体偏高。
        // Cover 挂在 Body 下，停车区姿态必须使用 Body 的局部原点，不能沿用旧的根节点偏移。
        [SerializeField] Vector3 upperPosition = new Vector3(-0.006f, 0f, 0f);
        [SerializeField] Vector3 upperRotation = new Vector3(-14.1f, 0.37f, -2.221f);
        [SerializeField] Vector3 upperScale = new Vector3(1f, 0.9f, 1f);
        [Header("模型自适应")]
        [Tooltip("按车身实际渲染边界校正车衣的平面位置，适配不同模型旋转和缩放。")]
        [SerializeField] bool autoAlignToBody = true;

        Vector3 basePosition;
        Quaternion baseRotation;
        Vector3 baseScale;
        Vector3 lowerPosition;
        Quaternion lowerRotation;
        Vector3 lowerScale;
        Coroutine revealRoutine;
        bool revealing;

        /// <summary>动画期间保持覆盖层启用，供 CarSkin3D 暂时保留布料。</summary>
        public bool IsRevealing => revealing;

        /// <summary>切换上方停车区姿态；默认姿态仍用于下方发车道。</summary>
        public void SetUpperArea(bool upperArea)
        {
            basePosition = upperArea ? upperPosition : lowerPosition;
            baseRotation = upperArea ? Quaternion.Euler(upperRotation) : lowerRotation;
            baseScale = upperArea ? upperScale : lowerScale;
            if (revealing) return;
            ApplyBasePose();
        }

        void ApplyBasePose()
        {
            transform.localPosition = basePosition;
            transform.localRotation = baseRotation;
            transform.localScale = ResolveScale(baseScale);
            transform.localPosition = ResolvePosition(transform.localPosition);
        }

        // 车衣 FBX 与车身 FBX 的原点不同，只校正画面平面轴，避免改变深度排序。
        Vector3 ResolvePosition(Vector3 candidate)
        {
            if (!autoAlignToBody || transform.parent == null) return candidate;
            var body = transform.parent;
            if (!TryGetBounds(body, body, transform, out var bodyBounds) ||
                !TryGetBounds(transform, body, null, out var coverBounds)) return candidate;
            Vector3 correction = bodyBounds.center - coverBounds.center;
            return candidate + new Vector3(correction.x, 0f, correction.z);
        }

        // 车尾露出时按车身和车衣的实际边界补足长度，避免为某一个 FBX 写死缩放。
        Vector3 ResolveScale(Vector3 candidate)
        {
            if (!autoAlignToBody || transform.parent == null) return candidate;
            transform.localScale = candidate;
            var body = transform.parent;
            // 车身长度对应 FBX 局部 z 轴，按车衣局部轴比较边界。
            if (!TryGetBounds(body, transform, transform, out var bodyBounds) ||
                !TryGetBounds(transform, transform, null, out var coverBounds)) return candidate;
            float x = Mathf.Max(1f, bodyBounds.size.x / Mathf.Max(0.001f, coverBounds.size.x) * 1.12f);
            float z = Mathf.Max(1.2f, bodyBounds.size.z / Mathf.Max(0.001f, coverBounds.size.z) * 1.2f);
            return new Vector3(candidate.x * x, candidate.y, candidate.z * z);
        }

        /// <summary>
        /// 把子树渲染边界换算到 space 空间。
        /// 有 MeshFilter 时用 mesh.bounds 而不是 Renderer.bounds：拆分衣片节点默认 inactive，
        /// Renderer.bounds 在未激活时不刷新，会污染覆盖层的自适应位置与缩放；
        /// 没有 MeshFilter 的渲染器（蒙皮等）退回 Renderer.bounds。
        /// </summary>
        static bool TryGetBounds(Transform root, Transform space, Transform excluded, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (excluded != null && (renderer.transform == excluded || renderer.transform.IsChildOf(excluded))) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                Bounds source;
                if (filter != null && filter.sharedMesh != null) source = filter.sharedMesh.bounds;
                else if (renderer is SkinnedMeshRenderer || renderer.enabled) source = LocalBoundsOf(renderer, space);
                else continue;
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = source.center + Vector3.Scale(source.extents, new Vector3(x, y, z));
                            Vector3 local = filter != null && filter.sharedMesh != null
                                ? space.InverseTransformPoint(filter.transform.TransformPoint(corner))
                                : corner; // LocalBoundsOf 已换算到 space 空间
                            if (!found) { bounds = new Bounds(local, Vector3.zero); found = true; }
                            else bounds.Encapsulate(local);
                        }
            }
            return found;
        }

        /// <summary>把渲染器的世界包围盒换算到 space 局部空间（用于没有 MeshFilter 的渲染器）。</summary>
        static Bounds LocalBoundsOf(Renderer renderer, Transform space)
        {
            Bounds world = renderer.bounds;
            Vector3 center = space.InverseTransformPoint(world.center);
            Vector3 extents = world.extents;
            return new Bounds(center, extents);
        }

        void Awake()
        {
            shardBurst ??= GetComponent<CoverBurst>();
            // 旧预制体曾把布料沿局部 y 轴抬高；该偏移在车辆倾斜后会变成画面位移。
            // 发车区和停车区共用车身中心，避免车衣脱离对应车辆。
            lowerPosition = new Vector3(transform.localPosition.x, 0f, transform.localPosition.z);
            lowerRotation = transform.localRotation;
            lowerScale = transform.localScale;
            basePosition = lowerPosition;
            baseRotation = lowerRotation;
            baseScale = lowerScale;
        }

        void OnEnable()
        {
            // Prefab/场景反序列化后先缓存初始姿态，避免首次 SetCovered 使用默认零缩放。
            if (lowerScale == Vector3.zero)
            {
                lowerPosition = transform.localPosition;
                lowerRotation = transform.localRotation;
                lowerScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
                basePosition = lowerPosition;
                baseRotation = lowerRotation;
                baseScale = lowerScale;
            }
        }

        /// <summary>设置问号车的初始遮盖状态，并清理上一次动画残留。</summary>
        public void SetCovered(bool covered)
        {
            if (covered)
            {
                StopReveal();
                // 衣片复位并隐藏，整体车衣重新挂上，保证下次遮盖是一整块。
                shardBurst?.Finish();
                ApplyBasePose();
                gameObject.SetActive(true);
                return;
            }

            if (!revealing) gameObject.SetActive(false);
        }

        /// <summary>播放一次爆衣（优先拆分衣片），动画结束后彻底隐藏覆盖层。</summary>
        public void PlayReveal()
        {
            StopReveal();
            gameObject.SetActive(true);
            revealRoutine = StartCoroutine(RevealRoutine());
        }

        /// <summary>中断动画并恢复到覆盖层初始姿态。</summary>
        void StopReveal()
        {
            if (revealRoutine != null) StopCoroutine(revealRoutine);
            revealRoutine = null;
            revealing = false;
        }

        /// <summary>
        /// 爆衣主循环：有拆分衣片时交给 CoverBurst 逐片炸开；
        /// 没有时沿用整块车衣抛出+放大（老预制体兜底）。
        /// </summary>
        IEnumerator RevealRoutine()
        {
            revealing = true;
            // 先换成完整衣片姿态再开始，避免上一帧残留位置被玩家看到。
            if (shardBurst != null) shardBurst.Prepare();
            bool useShards = shardBurst != null && shardBurst.HasPieces;
            float safeDuration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                if (useShards)
                {
                    shardBurst.SampleAt(t);
                }
                else
                {
                    float eased = 1f - Mathf.Pow(1f - t, 3f);
                    transform.localPosition = basePosition + Vector3.forward * (burstDistance * eased);
                    transform.localRotation = baseRotation * Quaternion.AngleAxis(spinDegrees * eased, Vector3.up);
                    transform.localScale = Vector3.LerpUnclamped(baseScale, baseScale * burstScale, eased);
                }
                yield return null;
            }

            if (useShards) shardBurst.Finish();
            ApplyBasePose();
            revealing = false;
            revealRoutine = null;
            gameObject.SetActive(false);
        }
    }
}
