using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 车衣爆衣动画的几何部分：只负责把「已拆分的衣片」按归一化时间炸开／复位。
    /// 不关心什么时候遮盖、车辆颜色、上下停车区姿态——这些由 <see cref="CarRevealCover"/> 决定。
    ///
    /// 数据流：CarRevealCover 播放时调用 <see cref="Prepare"/> → 每帧用 <see cref="SampleAt(float)"/>
    /// 把 t（0~1）写进每个衣片的局部 Transform → 结束再调 <see cref="Reset"/>。
    /// 衣片基准姿态在 Awake 缓存，之后每帧只做覆盖写，不累积误差。
    /// </summary>
    public sealed class CoverBurst : MonoBehaviour
    {
        /// <summary>拆分衣片的根节点名（CarModelSetup 生成预制体时按此命名）。</summary>
        public const string ShardNodeName = "BuliaoPosui";
        /// <summary>整体车衣节点名，爆衣期间隐藏。</summary>
        public const string StaticCoverNodeName = "Buliao";

        [Header("衣片节点")]
        [Tooltip("拆分衣片根节点；留空时按子节点名 BuliaoPosui 自动查找。")]
        public Transform shardRoot;
        [Tooltip("整体车衣节点；爆衣开始时隐藏，避免和衣片同时显示。留空按子节点名 Buliao 查找。")]
        public GameObject staticCover;

        [Header("炸开参数（单位：车衣本地空间，车衣长约 1.12）")]
        [Tooltip("衣片沿径向（车衣 x/z 平面）飞出的基础距离。")]
        [SerializeField] float burstDistance = 0.55f;
        [Tooltip("衣片朝镜头方向（模型 y 轴）抛出的额外距离。")]
        [SerializeField] float burstLift = 0.12f;
        [Tooltip("每个衣片的飞出距离倍率区间；按片序伪随机，保证每次播放一致。")]
        [SerializeField] Vector2 distanceRange = new Vector2(0.75f, 1.3f);
        [Tooltip("衣片自旋总角度（度）。")]
        [SerializeField] float pieceSpin = 220f;
        [Tooltip("动画末段衣片缩小到的比例；缩到极小后随覆盖层一起隐藏，避免残片停在画面上。")]
        [Range(0f, 1f)] [SerializeField] float endScale = 0.12f;

        // 一片衣片的基准姿态与炸开方向；dir/dist/axis/angle 与片序绑定，播放结果可复现。
        struct Piece
        {
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Vector3 direction;
            public float distance;
            public Vector3 spinAxis;
            public float spinAngle;
        }

        Piece[] pieces;

        /// <summary>True 表示存在拆分衣片，可以播放真正的爆衣；否则调用方应退回整体车衣动画。</summary>
        public bool HasPieces => pieces != null && pieces.Length > 0;

        void Awake()
        {
            CachePieces();
        }

        /// <summary>
        /// 缓存衣片基准姿态并生成固定的炸开方向。
        /// 用 mesh.bounds 而不是 Renderer.bounds：衣片节点默认是 inactive，Renderer.bounds 此时不可靠。
        /// </summary>
        void CachePieces()
        {
            // 只缓存一次：爆衣被打断（车辆被回收/覆盖层停用）时衣片可能停在炸开姿态，
            // 若重复缓存就会把「炸开后的位置」当成基准，下次遮盖会拼不回完整车衣。
            if (pieces != null) return;
            if (shardRoot == null) shardRoot = transform.Find(ShardNodeName);
            if (staticCover == null)
            {
                var staticNode = transform.Find(StaticCoverNodeName);
                if (staticNode != null) staticCover = staticNode.gameObject;
            }
            if (shardRoot == null) { pieces = null; return; }

            var found = new List<Piece>();
            foreach (var renderer in shardRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var tf = renderer.transform;
                // 衣片在车衣空间里的几何中心：用于确定炸开方向（从车衣中心向外）。
                Vector3 center = shardRoot.InverseTransformPoint(tf.TransformPoint(filter.sharedMesh.bounds.center));
                Vector3 radial = new Vector3(center.x, 0f, center.z);
                if (radial.sqrMagnitude < 1e-6f) radial = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                radial.Normalize();

                int seed = found.Count * 733 + 29;
                var rng = new System.Random(seed);
                // 径向为主，掺一点朝镜头（模型 +y）的分量，炸开时才有厚度感。
                var direction = new Vector3(
                    radial.x * Range(rng, 0.75f, 1.25f),
                    Range(rng, 0.1f, 0.45f),
                    radial.z * Range(rng, 0.75f, 1.25f)).normalized;

                found.Add(new Piece
                {
                    transform = tf,
                    position = tf.localPosition,
                    rotation = tf.localRotation,
                    scale = tf.localScale,
                    direction = direction,
                    distance = Range(rng, distanceRange.x, distanceRange.y),
                    // 自旋轴以视觉平面法线为主，衣片才像被掀翻而不是原地打转。
                    spinAxis = new Vector3(Range(rng, -0.4f, 0.4f), 1f, Range(rng, -0.4f, 0.4f)).normalized,
                    spinAngle = pieceSpin * Range(rng, 0.6f, 1.2f),
                });
            }
            pieces = found.Count > 0 ? found.ToArray() : null;
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>爆衣开始：换成拆分衣片（整体车衣隐藏），并复位到基准姿态。</summary>
        public void Prepare()
        {
            CachePieces();
            if (!HasPieces) return;
            if (staticCover != null) staticCover.SetActive(false);
            shardRoot.gameObject.SetActive(true);
            Reset();
        }

        /// <summary>把衣片恢复成完整车衣的样子；覆盖层本体可见性由调用方决定。</summary>
        public void Reset()
        {
            if (!HasPieces) return;
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i];
                p.transform.localPosition = p.position;
                p.transform.localRotation = p.rotation;
                p.transform.localScale = p.scale;
            }
        }

        /// <summary>爆衣结束：衣片复位并隐藏，整体车衣重新挂上（下次遮盖从完整车衣开始）。</summary>
        public void Finish()
        {
            Reset();
            if (shardRoot != null) shardRoot.gameObject.SetActive(false);
            if (staticCover != null) staticCover.SetActive(true);
        }

        /// <summary>
        /// 采样归一化时间 t（0~1）：衣片沿径向抛出、自旋、末段缩小。
        /// 位置用三次缓出（初速快、收尾慢），缩放用幂次曲线（后段才快速消失）。
        /// </summary>
        public void SampleAt(float t)
        {
            if (!HasPieces) return;
            t = Mathf.Clamp01(t);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float shrink = Mathf.Pow(t, 1.7f);
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = pieces[i];
                p.transform.localPosition = p.position
                    + p.direction * (p.distance * burstDistance * eased)
                    + Vector3.up * (burstLift * eased);
                p.transform.localRotation = p.rotation * Quaternion.AngleAxis(p.spinAngle * eased, p.spinAxis);
                p.transform.localScale = p.scale * Mathf.LerpUnclamped(1f, endScale, shrink);
            }
        }
    }
}
