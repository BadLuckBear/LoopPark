using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 程序化直套管：一头套住美术弯头的直口，另一头钉在「两车直口的中点」。
    /// 纯逻辑类（由 CarStrap 持有），只负责一段几何的生成与显示。
    ///
    /// 四个刻意的设计（每条都是这轮实测踩出来的）：
    /// 1) 挂到场景根、网格坐标直接写世界坐标 —— 车身的非均匀缩放（1.55/1.3/1.3）
    ///    会把任何子网格拉成椭圆，脱离父级才能保证套管是正圆。
    /// 2) 不建端盖 + 用双面 shader（LoopPark/Layered Hose）。
    ///    管子是开壳：单面渲染时，斜看接缝会穿过管内壁（背面被剔除）直接看到背景，
    ///    表现为管身中间一个透亮的「镜头形」缺口；改成两端封口又会在接缝露出断面圆盘。
    ///    双面 + 无端盖时，接缝处看到的是对面那半根管的内壁（暗色），像真实软管的对接。
    /// 3) 材质吃同款 shader 的参数（_CarDepth / _Color / _Glossiness），
    ///    跨车排序与车漆上色沿用既有体系；不复制美术材质（那会带上 _OcclusionMap 把管子压黑）。
    /// 4) 网格缓冲只分配一次，每帧只改写顶点/法线（零 GC）。
    /// </summary>
    public sealed class StrapSleeve
    {
        const int Sides = 12;   // 圆周分段：12 段在 5cm 半径下已经看不出棱
        const string HoseShaderName = "LoopPark/Layered Hose";

        readonly GameObject go;
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly Material material;
        readonly Vector3[] vertices = new Vector3[Sides * 2];
        readonly Vector3[] normals = new Vector3[Sides * 2];
        readonly Vector2[] uvs = new Vector2[Sides * 2];
        readonly int[] triangles = new int[Sides * 6];

        public StrapSleeve(string name, Material source = null)
        {
            go = new GameObject(name);
            // 运行时：一个标志都不加 DontSave —— 带 DontSave 的根对象在编辑器退出播放时**不会被销毁**，
            // 会残留在 Hierarchy 里（本次实测堆了几十个）。
            // 编辑模式（验证工具里 instantiate 时）才加 DontSave，免得弄脏场景；
            // HideInHierarchy 只是别让这些场景根对象在 Hierarchy 刷屏，不影响渲染。
            go.hideFlags = HideFlags.HideInHierarchy |
                           (Application.isPlaying ? HideFlags.None : HideFlags.DontSave);
            go.transform.SetParent(null, false);  // 场景根：绕开车身非均匀缩放
            var filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            mesh = new Mesh { name = name + "Mesh" };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;

            // 材质从美术绑带材质（Strap.mat）派生：贴图、_Glossiness、_SpecColor、描边参数全部继承，
            // 这样美术在 Inspector 里调 Strap.mat，套管立刻跟着变，两者观感永远一致。
            // 唯一必须换的是 shader → LoopPark/Layered Hose：它在 Layered Car 基础上只多了 Cull Off
            // （套管是开壳，单面渲染斜看接缝会穿过管内壁看到背景）。
            // 注意是「复制」不是共用实例：共用会把逐车上色的属性块写到所有绑带上。
            var hoseShader = Shader.Find(HoseShaderName);
            Shader target = hoseShader != null
                ? hoseShader
                : (source != null && source.shader != null ? source.shader : Shader.Find("Standard"));
            material = source != null
                ? new Material(source) { name = name + "Mat" }
                : new Material(target) { name = name + "Mat" };
            // 切 shader 时 Unity 会保留同名同类型的属性值（_Color/_Glossiness/_SpecColor/_MainTex…）。
            if (material.shader != target) material.shader = target;
            // 描边 pass 绕车身中心外扩，细长件上会撑出黑环；本 shader 没有描边 pass，这里只作保险。
            if (material.HasProperty("_CarOutlineCenter")) material.SetVector("_CarOutlineCenter", Vector4.zero);
            renderer.sharedMaterial = material;

            BuildTriangles();
            go.SetActive(false);
        }

        /// <summary>供 CarStrap 注册深度、写颜色用的渲染器。</summary>
        public Renderer Renderer => renderer;

        bool disposed;

        /// <summary>当前是否已画出一段。</summary>
        public bool Visible => !disposed && go != null && go.activeSelf;

        /// <summary>在两点之间画一根直管（世界坐标）。半径 <= 0 或两点重合时自动隐藏。</summary>
        public void Show(Vector3 from, Vector3 to, float radius)
        {
            // 退出 Play / 车被回收时，套管根节点可能已经先于 CarStrap 被销毁：
            // Unity 的 == 对已销毁对象返回 true，这里必须挡掉，否则 activeSelf 会抛 MissingReferenceException。
            if (disposed || go == null) return;

            Vector3 span = to - from;
            float length = span.magnitude;
            if (length < 1e-5f || radius <= 1e-5f) { Hide(); return; }

            Vector3 axis = span / length;
            // 参考轴不能和轴线平行，否则叉积退化（两车上下排列时轴线竖直，这里取横向）
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 normal = Vector3.Cross(axis, reference).normalized;
            Vector3 binormal = Vector3.Cross(axis, normal).normalized;

            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 centre = ring == 0 ? from : to;
                for (int j = 0; j < Sides; j++)
                {
                    float angle = (float)j / Sides * Mathf.PI * 2f;
                    Vector3 radial = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    int index = ring * Sides + j;
                    vertices[index] = centre + radial * radius;
                    normals[index] = radial;
                    // uv.y = 沿管长方向：0 = 起点（本车直口），1 = 终点（两车中点）。
                    // 双色渐变靠它插值（见 LoopPark/Layered Hose 的 surf）。
                    uvs[index] = new Vector2((float)j / Sides, ring);
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            if (!go.activeSelf) go.SetActive(true);
        }

        public void Hide()
        {
            // 同上：节点已被销毁（退出 Play / 车回收）时直接返回，不能碰 activeSelf。
            if (disposed || go == null) return;
            if (go.activeSelf) go.SetActive(false);
        }

        /// <summary>
        /// 绕序：Unity 以顺时针为正面，外向法线 = cross(v1-v0, v2-v0)（逐面叉积验算过）。
        /// 只建筒壁、不建端盖：双面渲染下无需封口，接缝处才能「看到对面管内壁」而不是断面。
        /// </summary>
        void BuildTriangles()
        {
            int index = 0;
            for (int j = 0; j < Sides; j++)
            {
                int next = (j + 1) % Sides;
                int a = j, b = next, c = Sides + j, d = Sides + next;
                triangles[index++] = a; triangles[index++] = b; triangles[index++] = c;
                triangles[index++] = b; triangles[index++] = d; triangles[index++] = c;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
        }

        /// <summary>车被销毁/回收时回收网格、材质与根节点（脱离父级后必须自己管生命周期）。</summary>
        public void Dispose()
        {
            if (disposed) return;   // 幂等：OnDestroy + 场景卸载可能都调一次
            disposed = true;
            // 编辑器工具在编辑模式下调 Dispose：Destroy 是延迟销毁，会留下孤儿节点，必须用 DestroyImmediate。
            if (go != null) Kill(go);
            if (mesh != null) Kill(mesh);
            if (material != null) Kill(material);
        }

        static void Kill(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}


