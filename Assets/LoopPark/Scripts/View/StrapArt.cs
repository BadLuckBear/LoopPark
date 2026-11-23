using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 车顶绑带美术件的「量件 + 转身」器（纯逻辑类，无场景存在感，由 CarStrap 持有）。
    ///
    /// 数据来源是 shengzi.fbx 里的两块网格（同一个美术坐标系）：
    ///   xipan   = 吸盘，圆盘贴在车顶，盘面轴向 = 模型 +Y；
    ///   shengzi = L 形弯头，竖脚插进吸盘孔、弯折 90° 后沿模型 −Z 水平伸出，直口法线沿 −Z。
    ///
    /// 职责（单一）：把弯头挂到一个以「竖脚轴线」为转轴的 pivot 上，只做绕该轴的水平旋转，
    /// 并对外给出「直口中心的世界坐标 + 此刻在屏幕平面上的口径」。
    /// 竖脚是圆柱且与转轴同轴（2026-11-24 实测：轴线偏差 0.0043 / 脚半径 0.0364），
    /// 所以无论转到哪个角度，它都留在吸盘孔里 —— 这就是「固定插在吸盘上、只改方向」的几何依据。
    /// 反过来，早期版本旋转+拉伸整根弯头：L 形件的两个管口方向固定相差 90°，
    /// 「管口垂直插入」与「两端连线指向对方」互斥，因此永远对不上（见 unity-runtime-transform-debugging）。
    ///
    /// 坐标系：全部在 host（CarStrap 所在节点）的局部空间里算。该空间里 +Y = 车顶法线、
    /// −Z = 弯头伸出的水平方向。父级带非均匀缩放（carModelScale 1.4/1.2/1.0），
    /// 因此只写 localRotation，绝不写世界 rotation（非均匀缩放下的世界旋转会带出 shear、把位置也带偏）。
    /// </summary>
    public sealed class StrapArt
    {
        // 认件关键字：先按节点名挑，名字对不上才退回形状（形状打分对 L 形件不可靠，仅作兜底）。
        const string CupKeyword = "xipan";
        const string ElbowKeyword = "shengzi";

        // 兜底常量：取自 2026-11-24 对 shengzi.fbx 真实顶点的实测（CarStrap 局部空间）。
        // 只在网格不可读（FBX 未勾 Read/Write）或认不到件时使用，保证功能不会整条链断掉。
        const float FallbackAxisX = 0.0040f;
        const float FallbackAxisY = 0.4384f;
        const float FallbackAxisZ = -0.0533f;
        const float FallbackMuzzleY = 0.5366f;
        const float FallbackMuzzleZ = -0.3910f;
        const float FallbackRadius = 0.0316f;

        readonly Transform host;
        readonly Renderer[] renderers;

        Transform pivot;
        float scale = 1f;

        // 以下均为 host 局部空间、静息姿态（未旋转）下的量
        Vector3 axisPoint;        // 竖脚轴线上的一个点；轴线方向 = 局部 +Y
        Vector3 muzzleFromPivot;  // 直口中心相对转轴的偏移
        Vector3 rimFromPivot;     // 口径方向（垂直于弯头臂），用来换算屏幕平面上的口径
        Vector3 restRadial;       // 转轴 → 直口的水平方向，旋转角的基准

        public bool Ready { get; private set; }

        /// <summary>转轴上的世界坐标（= 吸盘孔中心）；配对时对方拿它当瞄准目标。</summary>
        /// <remarks>
        /// 直接读 pivot.position 而不是缓存值：两辆车的 CarStrap 靠 LateUpdate 各自刷新，
        /// 谁先跑不确定；若这里返回上一帧的缓存，先跑的一方会瞄到对方旧位置，两根套管的中点就对不上了。
        /// </remarks>
        public Vector3 SocketWorld => pivot != null ? pivot.position : host.position;

        /// <summary>直口中心的世界坐标；套管从这里出发。</summary>
        public Vector3 MuzzleWorld { get; private set; }

        /// <summary>直口此刻在屏幕平面上的半径（受父级非均匀缩放影响，因此每帧重算）。</summary>
        public float MouthRadiusWorld { get; private set; }

        /// <summary>直口法线的世界方向（= 弯头臂的朝向，指向套管要去的方向）。</summary>
        public Vector3 ArmDirectionWorld { get; private set; }

        public StrapArt(Transform host, Renderer[] renderers)
        {
            this.host = host;
            this.renderers = renderers;
        }

        /// <summary>弯头整体缩放：相邻两车过近、直口伸过两车中点时调小（0.7~1）。</summary>
        public float Scale
        {
            get => scale;
            set
            {
                scale = Mathf.Max(0.05f, value);
                if (pivot != null) pivot.localScale = Vector3.one * scale;
                Refresh();
            }
        }

        /// <summary>
        /// 一次性装配：量出竖脚轴线与直口 → 建 pivot → 把弯头挂进 pivot。
        /// 弯头只换父级、不改自身姿态（worldPositionStays = true），所以看起来完全没动过。
        /// </summary>
        public void Setup()
        {
            if (Ready) return;
            var elbow = PickElbow();
            if (elbow == null)
            {
                Debug.LogWarning("[LoopPark] Strap 子树里找不到弯头网格（名字含 " + ElbowKeyword + "），车顶绑带只会显示吸盘。");
                return;
            }

            Measure(elbow);

            var go = new GameObject("ElbowSwivel");
            // 运行时不要 DontSave（退出播放不会被销毁，会残留）；编辑模式验证时才加。
            go.hideFlags = HideFlags.HideInHierarchy |
                           (Application.isPlaying ? HideFlags.None : HideFlags.DontSave);
            pivot = go.transform;
            pivot.SetParent(host, false);
            pivot.localPosition = axisPoint;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            // 先把弯头原地挂进 pivot（此时 pivot 缩放为 1，姿态完全不变），再整体缩放，
            // 这样 elbowScale 是「绕转轴缩放」而不是被 worldPositionStays 反算掉。
            elbow.SetParent(pivot, true);
            pivot.localScale = Vector3.one * scale;

            Ready = true;
            Refresh();
        }

        /// <summary>
        /// 让直口在屏幕平面内指向某个世界坐标。
        ///
        /// 不能直接用「局部 SignedAngle 累加」：父级是「非均匀缩放（1.55/1.3/1.3）+ 停车区俯仰 54°」
        /// 的复合，局部角度绕局部 +Y 转过去之后，世界方向会被压成椭圆（实测 45° 方向偏 17%~30°，
        /// 表现为直口不指向搭档、两根套管在中点折出可见的角）。
        ///
        /// 解析解：世界径向 = A·cosθ + B·sinθ（A、B 是 θ=0 与 θ=90° 时同一根半径映射到世界的方向，
        /// 即椭圆的两条半轴）。要求它与目标方向 d 平行 → 叉积为零 →
        /// θ = atan2(-(A×d), B×d)，再用点积排除反向的那一个解。
        /// </summary>
        public void AimAt(Vector3 worldTarget)
        {
            if (!Ready) return;
            Vector3 want = worldTarget - pivot.position;
            Vector2 direction = new Vector2(want.x, want.y);
            if (direction.sqrMagnitude < 1e-10f) { Refresh(); return; }   // 目标在轴线上：保持上一次角度
            direction.Normalize();

            Vector3 a = host.TransformVector(restRadial);                              // θ = 0 的径向
            Vector3 b = host.TransformVector(Vector3.Cross(Vector3.up, restRadial));   // θ = 90° 的径向

            float crossA = a.x * direction.y - a.y * direction.x;
            float crossB = b.x * direction.y - b.y * direction.x;
            float angle = Mathf.Atan2(-crossA, crossB);

            // 平行有两种（正、反），用点积选朝外的那一个
            float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
            float projection = (a.x * cosine + b.x * sine) * direction.x
                             + (a.y * cosine + b.y * sine) * direction.y;
            if (projection < 0f) angle += Mathf.PI;

            // Atan2 返回弧度、AngleAxis 吃角度：漏乘 Rad2Deg 会让弯头几乎不转（实测直口反向）
            pivot.localRotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up);
            Refresh();
        }

        /// <summary>把局部量换算成世界量（直口位置、法线、口径）。旋转或车身姿态变化后必须调用。</summary>
        public void Refresh()
        {
            if (!Ready) return;
            MuzzleWorld = pivot.TransformPoint(muzzleFromPivot);

            Vector3 worldU = pivot.TransformVector(rimFromPivot);                       // 直口平面内的一条半径
            ArmDirectionWorld = pivot.TransformVector(restRadial).normalized;            // 直口法线
            Vector3 worldV = Vector3.Cross(ArmDirectionWorld, worldU);                   // 直口平面内与之垂直的另一条半径

            // 口径取「直口开口的两条半径里的较大者」：父级是 1.55/1.3/1.3 的非均匀缩放，
            // 美术管口在 3D 里已经是个椭圆（两条半径 0.0296~0.0346 不等，实测），
            // 套管是正圆 —— 只按剪影取一条半径会盖不满开口，斜看就会从管口透出背景
            // （美术管壁是单面壳，看到内侧即被剔除）。取最大值才能保证「套住」。
            MouthRadiusWorld = Mathf.Max(worldU.magnitude, worldV.magnitude);
        }

        /// <summary>量出竖脚轴线、直口中心与口径；网格不可读时退回实测常量。</summary>
        void Measure(Transform elbow)
        {
            axisPoint = new Vector3(FallbackAxisX, FallbackAxisY, FallbackAxisZ);
            muzzleFromPivot = new Vector3(0f, FallbackMuzzleY, FallbackMuzzleZ) - axisPoint;
            rimFromPivot = new Vector3(FallbackRadius, 0f, 0f);
            restRadial = Horizontal(muzzleFromPivot);

            var filter = elbow.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) return;

            // 顶点一次性换算到 host 局部空间（FBX 未勾 Read/Write 时上面已经退回了）
            Matrix4x4 toHost = host.worldToLocalMatrix * elbow.localToWorldMatrix;
            var source = mesh.vertices;
            var local = new Vector3[source.Length];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < source.Length; i++)
            {
                local[i] = toHost.MultiplyPoint3x4(source[i]);
                minY = Mathf.Min(minY, local[i].y);
                maxY = Mathf.Max(maxY, local[i].y);
            }

            // 1) 竖脚：y 最低的一圈顶点 → 轴线位置
            float legBand = minY + Mathf.Max(1e-5f, (maxY - minY) * 0.06f);
            Vector3 legSum = Vector3.zero;
            int legCount = 0;
            for (int i = 0; i < local.Length; i++)
                if (local[i].y <= legBand) { legSum += local[i]; legCount++; }
            if (legCount > 0)
            {
                Vector3 leg = legSum / legCount;
                axisPoint = new Vector3(leg.x, minY, leg.z);
            }

            // 2) 直口：离轴线最远的那一圈顶点 → 直口中心
            float farthest = 0f;
            for (int i = 0; i < local.Length; i++)
                farthest = Mathf.Max(farthest, AxisDistance(local[i], axisPoint));
            if (farthest <= 1e-5f) return;

            Vector3 centre = Vector3.zero;
            int centreCount = 0;
            float ringBand = farthest - Mathf.Max(1e-4f, farthest * 0.02f);
            for (int i = 0; i < local.Length; i++)
                if (AxisDistance(local[i], axisPoint) >= ringBand) { centre += local[i]; centreCount++; }
            if (centreCount == 0) return;
            centre /= centreCount;

            // 3) 口径：直口平面内、垂直于弯头臂的那条半径（局部 X 方向），用作剪影宽度
            Vector3 rim = new Vector3(FallbackRadius, 0f, 0f);
            float rimLength = 0f;
            for (int i = 0; i < local.Length; i++)
            {
                Vector3 offset = local[i] - centre;
                if (Mathf.Abs(offset.x) > rimLength) { rimLength = Mathf.Abs(offset.x); rim = new Vector3(offset.x, 0f, 0f); }
            }
            if (rimLength > 1e-5f) rimFromPivot = rim;

            muzzleFromPivot = centre - axisPoint;
            restRadial = Horizontal(muzzleFromPivot);
            if (restRadial.sqrMagnitude < 1e-8f) restRadial = Vector3.back;
        }

        /// <summary>顶点到轴线（过 axisPoint 的局部 +Y 线）的水平距离。</summary>
        static float AxisDistance(Vector3 point, Vector3 axis)
        {
            float dx = point.x - axis.x, dz = point.z - axis.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>投影到水平面（局部 XZ）并归一化。</summary>
        static Vector3 Horizontal(Vector3 v)
        {
            var flat = new Vector3(v.x, 0f, v.z);
            return flat.sqrMagnitude < 1e-10f ? Vector3.back : flat.normalized;
        }

        Transform PickElbow()
        {
            Transform byName = null, byShape = null;
            float longest = 0f;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                string name = renderer.name.ToLowerInvariant();
                if (name.Contains(CupKeyword)) continue;          // 吸盘不转
                if (name.Contains(ElbowKeyword)) { byName = renderer.transform; continue; }
                // 兜底：细长（长轴远大于截面）的那个才是弯头
                var size = filter.sharedMesh.bounds.size;
                float length = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                float thickness = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
                if (length > thickness * 2f && length > longest) { longest = length; byShape = renderer.transform; }
            }
            return byName != null ? byName : byShape;
        }
    }
}

