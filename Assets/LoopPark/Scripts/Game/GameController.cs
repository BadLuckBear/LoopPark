using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LoopPark
{
    /// <summary>
    /// 核心玩法（实时）：发车道 → 环路 → 停车列 → 挤出 → 消除。
    /// 规则说明见 PuzzleSim。
    /// </summary>
    public class GameController : MonoBehaviour
    {
        public static GameController I { get; private set; }

        public LevelData Level { get; private set; }
        public int LoopLimit { get; private set; }
        public int LoopCount { get; private set; }
        public int Remaining { get; private set; }
        public bool Ended { get; private set; }
        public bool Paused { get; set; }
        public bool InputLocked { get; set; }

        /// <summary>新手引导：只允许点击该车道（-1 = 不限制）。</summary>
        public int TutorialLane = -1;
        public bool AutoPlay;

        public event Action<string, CarView> OnEvent;   // tap park push clear reveal loopfull deny
        public event Action LoopChanged;
        public event Action Won;
        public event Action Failed;

        // ---------- 布局 ----------
        // 停车列和发车队列使用相同的排距。
        const float QueuedRowSpacing = 0.96f;
        float cell, carW, laneW, colGap, pad, gateH;
        float lotW, lotH, lotCy;
        float[] colX, entryS;
        float colBottomY;
        float[] laneX;
        float laneFrontY, laneStep, laneMinY;
        float loopSpeed, moveSpeed, spacing;
        float ParkingSpeed => loopSpeed * 0.78f;
        TrackPath track;

        // ---------- 状态 ----------
        List<CarView>[] cols;
        bool[] fence, closed, clearing;
        int[] incoming;
        List<CarView>[] lanes;
        readonly List<CarView> loopCars = new List<CarView>();
        readonly List<CarView> queued = new List<CarView>();
        readonly List<CarView> allCars = new List<CarView>();
        readonly Dictionary<CarView, float> loopPositions = new Dictionary<CarView, float>();
        readonly List<(CarView car, int column, float speed)> parkArrivals = new List<(CarView, int, float)>();
        readonly Dictionary<CarView, float> loopSpeeds = new Dictionary<CarView, float>();
        CarView mergingCar;
        readonly Vector3[] departurePath = new Vector3[12];
        float mergingS;

        class Rope
        {
            public CarView a, b;
            public SpriteRenderer ra, rb, outline;
        }
        readonly List<Rope> ropes = new List<Rope>();

        // 钥匙锁
        int keyStack;
        TextMesh keyText;
        Transform keyBadge;
        // 帘子
        int[] curtainLeft, curtainColor;
        GameObject[] curtainGo;
        TextMesh[] curtainText;

        public int KeyStack => keyStack;
        // 彩色车道 / 车库 / 冰车道 / 电梯
        int[] laneColorOf, garageColorOf, iceLeft, levelIdx;
        GameObject[] garageGo, iceGo;
        TextMesh[] iceText, elevatorText;
        GameObject[] elevatorPlate;
        // 发车道车库生成器：队首前 genVisible 辆可见，之后 genLeft 辆藏在车库里
        int[] genVisible, genLeft;
        GameObject[] genGo;
        TextMesh[] genText;
        public static bool Verbose;
        float stuckTimer, autoTimer, autoIdle;
        int transit;

        Transform root, carRoot, fxRoot;
        Mesh edgeMesh;
        Color edgeColor;
        GameObject[] closedMarks;
        TextMesh counterText;
        SpriteRenderer counterBg;
        float counterPunch;

        void Awake() => I = this;

        public bool Covered(int j) => curtainLeft != null &&
            (curtainLeft[j] > 0 || garageColorOf[j] >= 0 || iceLeft[j] > 0);

        /// <summary>该颜色是否被某条可用的彩色车道预留。</summary>
        bool Reserved(int color)
        {
            for (int j = 0; j < cols.Length; j++)
                if (laneColorOf[j] == color && !closed[j]) return true;
            return false;
        }
        /// <summary>GM：直接通过本关。</summary>
        public void ForceWin()
        {
            if (Level == null || Ended) return;
            Ended = true;
            Sfx.Play("win");
            Won?.Invoke();
        }

        // ===================================================================
        // 开始 / 结束
        // ===================================================================
        public void StartLevel(LevelData d)
        {
            Cleanup();
            Level = d;
            LoopLimit = d.loopLimit;
            Ended = false;
            Paused = false;
            InputLocked = false;
            stuckTimer = 0;
            transit = 0;
            TutorialLane = -1;
            Remaining = d.TotalCars();

            root = new GameObject("Board").transform;
            root.SetParent(transform, false);
            carRoot = new GameObject("Cars").transform;
            carRoot.SetParent(root, false);
            fxRoot = new GameObject("Fx").transform;
            fxRoot.SetParent(root, false);

            Layout();
            BuildStatic();
            SpawnCars();
            RecountLoop();
        }

        public void Cleanup()
        {
            StopAllCoroutines();
            if (root != null) Destroy(root.gameObject);
            root = null;
            loopCars.Clear();
            queued.Clear();
            mergingCar = null;
            allCars.Clear();
            loopPositions.Clear();
            parkArrivals.Clear();
            loopSpeeds.Clear();
            ropes.Clear();
            Level = null;
            Ended = true;
        }

        public void Continue(int extraSlots)
        {
            LoopLimit += extraSlots;
            Ended = false;
            Paused = false;
            stuckTimer = 0;
            RecountLoop();
            PunchCounter();
        }

        // ===================================================================
        // 布局
        // ===================================================================
        void Layout()
        {
            var cam = Camera.main;
            float H = cam.orthographicSize, W = H * cam.aspect;
            int C = Level.columns.Count, cap = Level.capacity;

            float wU = C + (C - 1) * 0.14f + 0.44f;
            float hU = cap + 0.64f + 0.44f;
            float top = H - 2.0f, bottom = -H + 2.5f;
            float avail = top - bottom;
            cell = Mathf.Min(1.25f, (2 * W - 0.4f) / (wU + 2.66f), avail / (hU + 2.66f + 3.9f));

            colGap = 0.14f * cell;
            pad = 0.22f * cell;
            gateH = 0.32f * cell;
            laneW = 1.15f * cell;
            carW = 0.76f * cell;
            lotW = wU * cell;
            lotH = hU * cell;
            float margin = laneW / 2 + 0.1f * cell;

            float blockH = (hU + 2.66f + 3.9f) * cell;
            float blockTop = top - (avail - blockH) * 0.3f;
            lotCy = blockTop - 1.33f * cell - lotH / 2;

            track = new TrackPath(new Vector2(0, lotCy), lotW / 2 + margin, lotH / 2 + margin, cell * 1.0f);
            loopSpeed = track.Length / 4.6f;
            moveSpeed = 11f * cell;
            spacing = cell * 1.5f;

            colX = new float[C];
            entryS = new float[C];
            for (int j = 0; j < C; j++)
            {
                colX[j] = -lotW / 2 + pad + cell / 2 + j * (cell + colGap);
                entryS[j] = track.SAtTop(colX[j] + 0.34f * cell);
            }
            colBottomY = lotCy - lotH / 2 + pad + gateH;

            int L = Level.lanes.Count;
            laneX = new float[L];
            float maxHalf = track.a - track.r;
            float step = L > 1 ? Mathf.Min(1.3f * cell, 2 * maxHalf * 0.95f / (L - 1)) : 0;
            for (int i = 0; i < L; i++) laneX[i] = (i - (L - 1) / 2f) * step;
            laneFrontY = track.BottomY - laneW / 2 - 0.6f * cell - 0.5f * cell;
            laneStep = cell * QueuedRowSpacing;
            laneMinY = -H + 2.5f;
        }

        // 车位位置、围栏和机关共用整列高度，扩大行距后仍与车列对齐。
        float ColumnHeight => Level.capacity * cell;
        Vector3 SlotPos(int j, int i) => new Vector3(colX[j], colBottomY + ColumnHeight * 0.5f
            + (i - (Level.capacity - 1) * 0.5f) * cell * QueuedRowSpacing, 0);
        Vector3 LanePos(int l, int k) => new Vector3(laneX[l], laneFrontY - k * laneStep, 0);
        float ColTopY => colBottomY + ColumnHeight;

        public Vector3 LaneFrontWorld(int l) => root.TransformPoint(LanePos(l, 0));
        public Vector3 ColumnTopWorld(int j) => root.TransformPoint(new Vector3(colX[j], ColTopY, 0));
        public Vector3 CounterWorld => counterBg != null ? counterBg.transform.position : Vector3.zero;
        public float Cell => cell;

        void BuildStatic()
        {
            var skin = SkinConfig.Instance;
            var cam = Camera.main;
            cam.backgroundColor = skin.background;
            float H = cam.orthographicSize, W = H * cam.aspect;

            if (skin.backgroundSprite != null)
            {
                var bg = new GameObject("BG").AddComponent<SpriteRenderer>();
                bg.transform.SetParent(root, false);
                bg.sprite = skin.backgroundSprite;
                bg.sortingOrder = -10;
                var sz = skin.backgroundSprite.bounds.size;
                float k = Mathf.Max(2 * W / sz.x, 2 * H / sz.y);
                bg.transform.localScale = Vector3.one * k;
            }

            // 发车区底板
            float qTop = track.BottomY - laneW / 2 - 0.35f * cell;
            float qBottom = -H - 1f;
            var pad2 = ShapeMesh.RoundedRect("QueuePad", root, new Vector2(0, (qTop + qBottom) / 2),
                new Vector2(Mathf.Min(W - 0.2f, Mathf.Abs(laneX.Length > 0 ? laneX[0] : 0) + cell * 1.1f), (qTop - qBottom) / 2), cell * 0.5f,
                Color.Lerp(skin.background, skin.trackColor, 0.55f), -5);
            for (int i = 0; i < laneX.Length; i++)
            {
                // 车道分隔虚线
                if (i == 0) continue;
                float x = (laneX[i - 1] + laneX[i]) / 2;
                for (float y = qTop - 0.4f * cell; y > laneMinY - 2; y -= 0.6f * cell)
                    Quad("dash", new Vector3(x, y), new Vector2(0.06f * cell, 0.3f * cell), new Color(1, 1, 1, 0.35f), -4);
            }
            // 两侧草坪装饰
            float grassW = W - (Mathf.Abs(laneX.Length > 0 ? laneX[0] : 0) + cell * 1.1f) - 0.15f;
            if (grassW > 0.3f)
            {
                ShapeMesh.RoundedRect("GrassL", root, new Vector2(-W + grassW / 2 - 0.3f, (qTop + qBottom) / 2), new Vector2(grassW / 2 + 0.3f, (qTop - qBottom) / 2), cell * 0.6f, skin.grassColor, -6);
                ShapeMesh.RoundedRect("GrassR", root, new Vector2(W - grassW / 2 + 0.3f, (qTop + qBottom) / 2), new Vector2(grassW / 2 + 0.3f, (qTop - qBottom) / 2), cell * 0.6f, skin.grassColor, -6);
            }

            // 赛道：外沿（可变色） → 路面 → 停车场
            float edge = 0.09f * cell;
            var edgeGo = ShapeMesh.RoundedRect("TrackEdge", root, track.center, new Vector2(track.a + laneW / 2 + edge, track.b + laneW / 2 + edge), track.r + laneW / 2 + edge, skin.trackEdge, 0);
            edgeMesh = edgeGo.GetComponent<MeshFilter>().sharedMesh;
            edgeColor = skin.trackEdge;
            ShapeMesh.RoundedRect("Track", root, track.center, new Vector2(track.a + laneW / 2, track.b + laneW / 2), track.r + laneW / 2, skin.trackColor, 1);
            ShapeMesh.RoundedRect("InnerEdge", root, track.center, new Vector2(track.a - laneW / 2, track.b - laneW / 2), Mathf.Max(0.05f, track.r - laneW / 2), skin.trackEdge, 2);
            ShapeMesh.RoundedRect("Lot", root, track.center, new Vector2(track.a - laneW / 2 - edge, track.b - laneW / 2 - edge), Mathf.Max(0.05f, track.r - laneW / 2 - edge), skin.lotColor, 3);

            // 中心虚线
            for (float s = 0; s < track.Length; s += 0.7f * cell)
            {
                var p = track.Eval(s, out var dir);
                var q = Quad("dash", p, new Vector2(0.28f * cell, 0.05f * cell), new Color(1, 1, 1, 0.25f), 2);
                q.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            }

            // 停车列
            int C = Level.columns.Count;
            closedMarks = new GameObject[C];
            for (int j = 0; j < C; j++)
            {
                float h = ColumnHeight + 0.12f * cell;
                var colSpec = Level.columns[j];
                var colBg = colSpec.colored ? Color.Lerp(skin.columnColor, Palette.Get(colSpec.lcol), 0.55f) : skin.columnColor;
                ShapeMesh.RoundedRect("Col" + j, root, new Vector2(colX[j], colBottomY + ColumnHeight / 2), new Vector2(cell / 2 + 0.03f * cell, h / 2), 0.18f * cell, colBg, 4);
                if (colSpec.colored)
                {
                    // 彩色车道：入口处画一个同色标记
                    var mk = new GameObject("LaneMark").AddComponent<SpriteRenderer>();
                    mk.transform.SetParent(root, false);
                    mk.sprite = SpriteFactory.Circle();
                    mk.color = Palette.Get(colSpec.lcol);
                    mk.sortingOrder = 6;
                    mk.transform.localPosition = new Vector3(colX[j], ColTopY + gateH * 0.55f, 0);
                    mk.transform.localScale = Vector3.one * 0.32f * cell;
                }
                // 入口箭头
                var arrow = Quad("arrow", new Vector3(colX[j], ColTopY + gateH * 0.55f), new Vector2(0.22f * cell, 0.22f * cell), new Color(1, 1, 1, 0.45f), 5);
                arrow.transform.rotation = Quaternion.Euler(0, 0, 45);
                if (Level.columns[j].fence)
                {
                    Gate(new Vector3(colX[j], ColTopY + gateH * 0.5f));
                    Gate(new Vector3(colX[j], colBottomY - gateH * 0.5f));
                }
                // 关闭标记（栅栏列清空后显示）
                var mark = new GameObject("Closed" + j);
                mark.transform.SetParent(root, false);
                for (int k = 0; k < 2; k++)
                {
                    float y = colBottomY + ColumnHeight * (k == 0 ? 0.3f : 0.7f);
                    var a = Quad("x", new Vector3(colX[j], y), new Vector2(0.06f * cell, 0.7f * cell), new Color(1, 1, 1, 0.4f), 6, mark.transform);
                    a.transform.rotation = Quaternion.Euler(0, 0, 45);
                    var b = Quad("x", new Vector3(colX[j], y), new Vector2(0.06f * cell, 0.7f * cell), new Color(1, 1, 1, 0.4f), 6, mark.transform);
                    b.transform.rotation = Quaternion.Euler(0, 0, -45);
                    var cone = new GameObject("cone").AddComponent<SpriteRenderer>();
                    cone.transform.SetParent(mark.transform, false);
                    cone.sprite = SpriteFactory.Circle();
                    cone.color = new Color(1f, 0.45f, 0.15f);
                    cone.sortingOrder = 7;
                    cone.transform.position = new Vector3(colX[j], (y + colBottomY + ColumnHeight * 0.5f) / 2);
                    cone.transform.localScale = Vector3.one * 0.18f * cell;
                }
                mark.SetActive(false);
                closedMarks[j] = mark;
            }

            // 环路计数牌
            var badge = new GameObject("Counter");
            badge.transform.SetParent(root, false);
            badge.transform.localPosition = new Vector3(track.a + laneW * 0.1f - 0.2f * cell, track.BottomY + laneW * 0.1f, 0);
            counterBg = badge.AddComponent<SpriteRenderer>();
            counterBg.sprite = SpriteFactory.RoundedRect(64, 20);
            counterBg.drawMode = SpriteDrawMode.Sliced;
            counterBg.size = new Vector2(1.3f, 0.75f);
            counterBg.color = new Color(0.22f, 0.22f, 0.3f);
            counterBg.sortingOrder = 40;
            badge.transform.localScale = Vector3.one * cell;
            var t = new GameObject("Text");
            t.transform.SetParent(badge.transform, false);
            t.transform.localScale = Vector3.one * 0.07f;
            counterText = t.AddComponent<TextMesh>();
            counterText.font = SpriteFactory.Font;
            counterText.GetComponent<MeshRenderer>().sharedMaterial = SpriteFactory.Font.material;
            counterText.GetComponent<MeshRenderer>().sortingOrder = 41;
            counterText.fontSize = 64;
            counterText.fontStyle = FontStyle.Bold;
            counterText.anchor = TextAnchor.MiddleCenter;
            counterText.color = Color.white;
        }

        GameObject Quad(string n, Vector3 pos, Vector2 size, Color c, int order, Transform parent = null)
        {
            var go = new GameObject(n);
            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Square();
            sr.color = c;
            sr.sortingOrder = order;
            return go;
        }

        void Gate(Vector3 pos)
        {
            var go = new GameObject("Gate");
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Hazard();
            sr.sortingOrder = 8;
            go.transform.localScale = new Vector3(cell * 0.95f, gateH * 0.8f * 4f, 1);
        }

        void SpawnCars()
        {
            int C = Level.columns.Count;
            cols = new List<CarView>[C];
            fence = new bool[C];
            closed = new bool[C];
            clearing = new bool[C];
            incoming = new int[C];
            curtainLeft = new int[C];
            curtainColor = new int[C];
            laneColorOf = new int[C];
            garageColorOf = new int[C];
            iceLeft = new int[C];
            levelIdx = new int[C];
            garageGo = new GameObject[C];
            iceGo = new GameObject[C];
            iceText = new TextMesh[C];
            elevatorText = new TextMesh[C];
            elevatorPlate = new GameObject[C];
            for (int j = 0; j < C; j++) { laneColorOf[j] = -1; garageColorOf[j] = -1; }
            curtainGo = new GameObject[C];
            curtainText = new TextMesh[C];
            keyStack = 0;
            for (int j = 0; j < C; j++)
            {
                cols[j] = new List<CarView>();
                fence[j] = Level.columns[j].fence;
                var specs = Level.columns[j].cars;
                for (int i = 0; i < specs.Count; i++)
                {
                    var cv = NewCar(specs[i]);
                    cv.state = CarState.Parked;
                    cv.column = j;
                    cv.transform.localPosition = SlotPos(j, i);
                    cv.SetAngle(180, true);
                    cv.hasKey = specs[i].k;
                    cv.RefreshBadges();
                    cols[j].Add(cv);
                }
                RevealEnds(j);
                if (Level.columns[j].cv > 0) BuildCurtain(j, Level.columns[j].cc, Level.columns[j].cv);
                laneColorOf[j] = Level.columns[j].colored ? Level.columns[j].lcol : -1;
                if (Level.columns[j].garage) BuildGarage(j, Level.columns[j].gcol);
                if (Level.columns[j].ice > 0) BuildIce(j, Level.columns[j].ice);
                if (Level.columns[j].levels.Count > 0) BuildElevatorBadge(j);
            }
            BuildKeyBadge();

            lanes = new List<CarView>[Level.lanes.Count];
            genVisible = new int[lanes.Length];
            genLeft = new int[lanes.Length];
            genGo = new GameObject[lanes.Length];
            genText = new TextMesh[lanes.Length];
            for (int l = 0; l < lanes.Length; l++)
            {
                lanes[l] = new List<CarView>();
                genVisible[l] = Level.lanes[l].gn > 0 ? Level.lanes[l].gi : 0;
                genLeft[l] = Level.lanes[l].gn;
                var specs = Level.lanes[l].cars;
                for (int k = 0; k < specs.Count; k++)
                {
                    var cv = NewCar(specs[k]);
                    cv.state = CarState.InLane;
                    cv.lane = l;
                    cv.link = specs[k].l;
                    cv.locked = specs[k].lk;
                    cv.RefreshBadges();
                    cv.transform.localPosition = LanePos(l, VisualIndex(l, k));
                    cv.SetAngle(0, true);
                    lanes[l].Add(cv);
                }
                if (lanes[l].Count > 0) lanes[l][0].Reveal();
                if (genLeft[l] > 0) BuildGenerator(l);
                UpdateLaneVisibility(l);
            }

            // 绑带：配对并画绳子
            var byLink = new Dictionary<int, CarView>();
            foreach (var lane in lanes)
                foreach (var cv in lane)
                {
                    if (cv.link == 0) continue;
                    if (byLink.TryGetValue(cv.link, out var other))
                    {
                        cv.partner = other;
                        other.partner = cv;
                        var ol = RopeHalf(other);
                        ol.color = new Color(0.16f, 0.13f, 0.24f, 0.55f);
                        ol.sortingOrder = 25;
                        ropes.Add(new Rope { a = other, b = cv, ra = RopeHalf(other), rb = RopeHalf(cv), outline = ol });
                    }
                    else byLink[cv.link] = cv;
                }
            UpdateRopes();
        }

        SpriteRenderer RopeHalf(CarView car)
        {
            var go = new GameObject("Rope");
            go.transform.SetParent(carRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Capsule();
            sr.drawMode = SpriteDrawMode.Sliced;
            var rc = Color.Lerp(Palette.Get(car.color), Color.white, 0.18f);
            rc.a = 0.92f;
            sr.color = rc;
            sr.sortingOrder = 26;
            return sr;
        }

        void UpdateRopes()
        {
            foreach (var r in ropes)
            {
                if (r.a == null || r.b == null) continue;
                var pa = r.a.transform.localPosition;
                var pb = r.b.transform.localPosition;
                var mid = (pa + pb) / 2;
                bool vis = r.a.gameObject.activeSelf && r.b.gameObject.activeSelf;
                // 粗绑带：从车身中心连到车身中心，两半各用自己车的颜色，画在车模型上面
                SetRope(r.outline, pa, pb, vis, 0.40f, -3.0f);
                SetRope(r.ra, pa, mid, vis, 0.32f, -3.1f);
                SetRope(r.rb, mid, pb, vis, 0.32f, -3.1f);
            }
        }

        void SetRope(SpriteRenderer sr, Vector3 a, Vector3 b, bool visible, float thick, float z)
        {
            sr.enabled = visible;
            var d = b - a;
            float len = d.magnitude;
            // 9 宫格的圆角边框是固定像素：直接把 size 设成比边框还细会把两端圆角挤成“蝴蝶结”。
            // 这里保持 sprite 原始高度，用整体缩放控制粗细，两端始终是完整的半圆头。
            float h = sr.sprite.rect.height / sr.sprite.pixelsPerUnit;
            float k = thick * cell / h;
            sr.transform.localPosition = new Vector3((a.x + b.x) / 2, (a.y + b.y) / 2, z);
            sr.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            sr.transform.localScale = new Vector3(k, k, 1);
            sr.size = new Vector2(Mathf.Max(h, (len + thick * cell) / k), h);
        }

        void SnapRope(CarView car)
        {
            for (int i = ropes.Count - 1; i >= 0; i--)
            {
                var r = ropes[i];
                if (r.a != car && r.b != car) continue;
                var mid = (r.a.transform.position + r.b.transform.position) / 2;
                FxParticle.Burst(fxRoot, mid, r.ra.color, 10, cell * 0.12f, cell * 3f);
                FxParticle.Burst(fxRoot, mid, r.rb.color, 10, cell * 0.12f, cell * 3f);
                Destroy(r.ra.gameObject);
                Destroy(r.rb.gameObject);
                Destroy(r.outline.gameObject);
                ropes.RemoveAt(i);
            }
        }

        /// <summary>该车道队首能否发车（考虑绑带）。</summary>
        public bool LaneReady(int l)
        {
            if (lanes[l].Count == 0 || LaneBusy(l)) return false;
            var car = lanes[l][0];
            if (car.locked || (car.partner != null && car.partner.locked)) return false;
            if (car.partner == null) return LoopCount < LoopLimit;
            int pl = car.partner.lane;
            return pl >= 0 && lanes[pl].Count > 0 && lanes[pl][0] == car.partner && !LaneBusy(pl) && LoopCount + 2 <= LoopLimit;
        }

        CarView NewCar(CarSpec spec)
        {
            var cv = CarView.Create(carRoot, spec.c, spec.h, carW);
            allCars.Add(cv);
            return cv;
        }

        /// <summary>车道里第 k 辆车的显示位置（车库里的车都“在”车库格子里）。</summary>
        int VisualIndex(int l, int k)
        {
            if (genLeft == null || genLeft[l] <= 0) return k;
            int v = genVisible[l], g = genLeft[l];
            return k < v ? k : (k < v + g ? v : k - g + 1);
        }

        bool InGarage(int l, int k) => genLeft != null && genLeft[l] > 0 && k >= genVisible[l] && k < genVisible[l] + genLeft[l];

        void UpdateLaneVisibility(int l)
        {
            for (int k = 0; k < lanes[l].Count; k++)
            {
                var y = LanePos(l, VisualIndex(l, k)).y;
                lanes[l][k].gameObject.SetActive(y > laneMinY - laneStep && !InGarage(l, k));
            }
        }

        // ===================================================================
        // 规则
        // ===================================================================
        public bool CanEnter(int j, int color)
        {
            if (closed[j] || clearing[j] || Covered(j)) return false;
            if (laneColorOf[j] >= 0) return laneColorOf[j] == color;
            var c = cols[j];
            if (c.Count == 0) return !Reserved(color) && !AnyMatchingTop(color);
            var top = c[c.Count - 1];
            return !top.hidden && top.color == color;
        }

        /// <summary>场上是否已有顶部同色、可进入的列。</summary>
        bool AnyMatchingTop(int color)
        {
            for (int j = 0; j < cols.Length; j++)
            {
                if (closed[j] || clearing[j] || Covered(j)) continue;
                if (laneColorOf[j] >= 0) { if (laneColorOf[j] == color) return true; continue; }
                var c = cols[j];
                if (c.Count == 0) continue;
                var top = c[c.Count - 1];
                if (!top.hidden && top.color == color) return true;
            }
            return false;
        }

        bool AnyTargetFor(int color)
        {
            for (int j = 0; j < cols.Length; j++) if (CanEnter(j, color)) return true;
            return false;
        }

        void RevealEnds(int j)
        {
            var c = cols[j];
            if (c.Count == 0) return;
            if (c[0].hidden) { c[0].Reveal(); Emit("reveal", c[0]); }
            var t = c[c.Count - 1];
            if (t.hidden) { t.Reveal(); Emit("reveal", t); }
        }

        void RecountLoop()
        {
            int n = queued.Count;
            foreach (var c in allCars)
                if (c != null && (c.state == CarState.ToLoop || c.state == CarState.OnLoop || c.state == CarState.Ejecting))
                    n++;
            if (n != LoopCount)
            {
                LoopCount = n;
            }
            LoopChanged?.Invoke();
        }

        public bool LaneBusy(int l)
        {
            foreach (var car in queued)
                if (car.lane == l) return true;
            foreach (var c in allCars)
                if (c != null && c.lane == l && (c.state == CarState.ToLoop))
                    return true;
            return false;
        }

        public bool LaneHasCar(int l) => lanes[l].Count > 0;
        public int LaneCount => lanes == null ? 0 : lanes.Length;
        public int LaneFrontColor(int l) => lanes[l].Count > 0 ? lanes[l][0].color : -1;

        public bool TapLane(int l)
        {
            if (Ended || Paused || l < 0 || l >= lanes.Length || lanes[l].Count == 0) return false;
            if (TutorialLane >= 0 && l != TutorialLane) return false;
            if (LaneBusy(l)) return false;
            if (LoopCount >= LoopLimit)
            {
                Sfx.Play("deny");
                PunchCounter();
                Emit("loopfull", null);
                return false;
            }

            var front = lanes[l][0];
            if (front.locked || (front.partner != null && front.partner.locked))
            {
                Sfx.Play("deny");
                front.Punch(0.4f);
                Emit("lockblocked", front);
                return false;
            }
            int pl = -1;
            if (front.partner != null)
            {
                pl = front.partner.lane;
                bool partnerFront = pl >= 0 && lanes[pl].Count > 0 && lanes[pl][0] == front.partner;
                if (!partnerFront)
                {
                    Sfx.Play("deny");
                    front.Punch(0.4f);
                    front.partner.Punch(0.4f);
                    Emit("linkblocked", front);
                    return false;
                }
                if (LaneBusy(pl)) return false;
                if (LoopCount + 2 > LoopLimit)
                {
                    Sfx.Play("deny");
                    PunchCounter();
                    Emit("loopfull", null);
                    return false;
                }
            }

            var car = front;
            if (pl >= 0 && laneX[pl] > laneX[l]) queued.Add(front.partner);
            queued.Add(car);
            if (pl >= 0)
            {
                if (laneX[pl] <= laneX[l]) queued.Add(front.partner);
                SnapRope(car);
                Emit("linkgo", car);
            }
            RecountLoop();
            Sfx.Play("tap");
            Emit("tap", car);
            if (LoopCount >= LoopLimit) Emit("loopfull", car);
            return true;
        }

        void Dispatch(int l, float mergeS, float distance)
        {
            var car = lanes[l][0];
            lanes[l].RemoveAt(0);
            car.Reveal();
            car.state = CarState.ToLoop;
            car.SetLayer(30);
            car.PlayEvent("tap");
            car.SetIdle(false);
            DriveToMerge(car, mergeS, loopSpeed, departurePath, distance);
        }

        void AdvanceLane(int l)
        {
            if (genLeft[l] > 0)
            {
                // 车库保持在原位置，吐出一辆车
                genLeft[l]--;
                int emerged = genVisible[l] - 1;
                if (emerged >= 0 && emerged < lanes[l].Count)
                {
                    var ec = lanes[l][emerged];
                    ec.transform.localPosition = LanePos(l, genVisible[l]);
                    ec.Punch(0.5f);
                }
                UpdateGenerator(l);
            }
            for (int k = 0; k < lanes[l].Count; k++)
            {
                var lc = lanes[l][k];
                if (InGarage(l, k)) continue;
                lc.gameObject.SetActive(LanePos(l, VisualIndex(l, k)).y > laneMinY - laneStep);
                lc.ParkAlong(new[] { LanePos(l, VisualIndex(l, k)) }, 0f, moveSpeed * 0.6f, moveSpeed, null, face: false);
            }
            if (lanes[l].Count > 0) lanes[l][0].Reveal();
            UpdateLaneVisibility(l);
            TryUnlock();
        }

        void Enter(CarView car, int j, float entrySpeed)
        {
            loopCars.Remove(car);
            loopSpeeds.Remove(car);
            car.state = CarState.Entering;
            car.column = j;
            car.lane = -1;
            incoming[j]++;
            var c = cols[j];
            c.Add(car);

            CarView ejected = null;
            if (c.Count > Level.capacity)
            {
                ejected = c[0];
                c.RemoveAt(0);
            }
            CarView leader = incoming[j] > 1 ? c[c.Count - 2] : null;

            int slot = c.Count - 1;
            car.SetLayer(30);
            car.ParkAlong(track.ParkingPath(car.s, SlotPos(j, slot), 0.65f * cell),
                entrySpeed, ParkingSpeed, moveSpeed, () => OnCarParked(car, j), leader: leader, gap: laneStep - 0.001f * cell);
            car.BeginTireTrail(fxRoot);
            StartCoroutine(EmitWheelSmoke(car));

            if (ejected != null)
            {
                for (int i = 0; i < slot; i++)
                    c[i].MoveAlong(new[] { SlotPos(j, i) }, moveSpeed * 0.8f, null, false);
                ejected.state = CarState.Ejecting;
                ejected.column = -1;
                ejected.SetLayer(30);
                ejected.Reveal();
                ejected.PlayEvent("push");
                if (ejected.hasKey) ReleaseKey(ejected);
                DriveToMerge(ejected, track.SAtBottom(colX[j]), moveSpeed * 0.9f);
                Sfx.Play("push");
                Emit("push", ejected);
            }

            RevealEnds(j);
            if (IsClearable(j))
            {
                clearing[j] = true;
                if (incoming[j] <= 0) StartCoroutine(ClearColumn(j));
            }
            RecountLoop();
            if (Verbose) Debug.Log($"[LoopPark] enter car{car.color} -> col{j} eject={(ejected != null ? ejected.color.ToString() : "-")} {Dump()}");
        }

        IEnumerator EmitWheelSmoke(CarView car)
        {
            for (int i = 0; i < 3; i++)
            {
                if (car == null || fxRoot == null) yield break;
                FxParticle.WheelSmoke(fxRoot, car);
                if (i < 2) yield return new WaitForSeconds(0.1f);
            }
        }



        public string Dump()
        {
            var sb = new System.Text.StringBuilder();
            for (int j = 0; j < cols.Length; j++)
            {
                sb.Append(closed[j] ? "X" : clearing[j] ? "C" : "").Append('[');
                foreach (var c in cols[j]) sb.Append(c.hidden ? "?" : "").Append(c.color).Append(c.state == CarState.Parked ? "" : "*").Append(',');
                sb.Append($"]i{incoming[j]} ");
            }
            sb.Append(" loop:");
            foreach (var c in loopCars) sb.Append(c.color).Append(',');
            if (mergingCar != null) sb.Append(" merging:").Append(mergingCar.color);
            sb.Append($" count {LoopCount}/{LoopLimit}");
            return sb.ToString();
        }

        bool IsClearable(int j)
        {
            var c = cols[j];
            if (c.Count != Level.capacity) return false;
            for (int i = 1; i < c.Count; i++) if (c[i].color != c[0].color) return false;
            return true;
        }

        void OnCarParked(CarView car, int j)
        {
            autoIdle = 0;
            car.state = CarState.Parked;
            car.SetLayer(20);
            car.PlayEvent("park");
            incoming[j]--;
            Sfx.Play("park", 1f + 0.05f * cols[j].Count);
            Emit("park", car);
            if (clearing[j] && incoming[j] == 0) StartCoroutine(ClearColumn(j));
        }

        IEnumerator ClearColumn(int j)
        {
            transit++;
            yield return new WaitForSeconds(0.12f);
            var c = new List<CarView>(cols[j]);
            Sfx.Play("clear");
            Sfx.Vibrate();
            var colColor = c.Count > 0 ? Palette.Get(c[0].color) : Color.white;
            FxParticle.Flash(fxRoot, root.TransformPoint(new Vector3(colX[j], colBottomY + ColumnHeight / 2)), new Vector2(cell, ColumnHeight), new Color(1, 1, 1, 0.55f), 0.35f);
            foreach (var car in c)
            {
                if (car.hasKey) ReleaseKey(car);
                car.state = CarState.Clearing;
                car.Reveal();
                car.PlayEvent("clear");
                Emit("clear", car);
            }
            // 从上到下依次弹起消失
            for (int i = c.Count - 1; i >= 0; i--)
            {
                StartCoroutine(PopAndDie(c[i], colColor));
                yield return new WaitForSeconds(0.05f);
            }
            yield return new WaitForSeconds(0.3f);
            cols[j].Clear();
            clearing[j] = false;
            Remaining -= c.Count;
            if (c.Count > 0) CurtainProgress(c[0].color, c.Count);
            if (c.Count > 0) GarageProgress(c[0].color);
            IceCountdown(j);
            ElevatorRise(j);
            if (fence[j])
            {
                closed[j] = true;
                closedMarks[j].SetActive(true);
            }
            transit--;
            Emit("cleared", null);
            if (Remaining <= 0) StartCoroutine(WinRoutine());
        }

        IEnumerator PopAndDie(CarView car, Color color)
        {
            float t = 0;
            var start = car.Visual.localScale;
            var sr = car.GetComponentsInChildren<SpriteRenderer>();
            while (t < 0.28f)
            {
                t += Time.deltaTime;
                float k = t / 0.28f;
                float s = k < 0.4f ? Mathf.Lerp(1f, 1.3f, k / 0.4f) : Mathf.Lerp(1.3f, 0f, (k - 0.4f) / 0.6f);
                car.Visual.localScale = start * s;
                yield return null;
            }
            FxParticle.Burst(fxRoot, car.transform.position, color, 8, cell * 0.16f, cell * 4f);
            allCars.Remove(car);
            Destroy(car.gameObject);
        }

        IEnumerator WinRoutine()
        {
            Ended = true;
            yield return new WaitForSeconds(0.6f);
            Sfx.Play("win");
            Won?.Invoke();
        }

        // ===================================================================
        // 每帧
        // ===================================================================
        void Update()
        {
            if (Level == null || root == null) return;
            float dt = Time.deltaTime;
            UpdateLaneIdle();
            if (Paused) return;

            HandleInput();
            MoveLoop(dt);
            ProcessDispatchQueue();
            CheckStuck(dt);
            AnimateCounter(dt);
            UpdateRopes();
            if (AutoPlay) AutoStep(dt);
        }

        /// <summary>发车道第一排的车播放待机抖动和尾气（车在移动时 CarView 会自动暂停）。</summary>
        void UpdateLaneIdle()
        {
            if (lanes == null) return;
            for (int l = 0; l < lanes.Length; l++)
                for (int k = 0; k < lanes[l].Count; k++)
                    lanes[l][k].SetIdle(k == 0);
        }

        void HandleInput()
        {
            if (Ended || InputLocked || AutoPlay) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (Input.touchCount > 0 && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)) return;
            var wp = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            var lp = root.InverseTransformPoint(wp);
            int best = -1;
            float bestD = float.MaxValue;
            for (int l = 0; l < laneX.Length; l++)
            {
                float dx = Mathf.Abs(lp.x - laneX[l]);
                if (dx > Mathf.Max(0.65f * cell, (laneX.Length > 1 ? Mathf.Abs(laneX[1] - laneX[0]) : cell) * 0.5f)) continue;
                if (lp.y > laneFrontY + 0.8f * cell) continue;
                if (dx < bestD) { bestD = dx; best = l; }
            }
            if (best >= 0) TapLane(best);
        }

        void MoveLoop(float dt)
        {
            if (dt <= 0f) return;
            loopPositions.Clear();
            parkArrivals.Clear();
            // 所有车辆读取同一帧的前车位置，结果不受更新顺序影响。
            foreach (var car in loopCars) loopPositions.Add(car, car.s);
            // 车尾离开环道前，后车仍需给入库车辆留出车距。
            for (int j = 0; j < incoming.Length; j++)
            {
                if (incoming[j] == 0) continue;
                var entering = cols[j][cols[j].Count - 1];
                if (!ParkingTailClear(entering))
                    loopPositions.Add(entering, entering.s);
            }

            foreach (var car in loopCars)
            {
                float prev = car.s;
                float free = float.PositiveInfinity;
                foreach (var front in loopPositions)
                {
                    if (front.Key != car) free = Mathf.Min(free, track.Forward(prev, front.Value) - spacing);
                }
                if (mergingCar != null) free = Mathf.Min(free, track.Forward(prev, mergingS) - spacing);

                int target = -1;
                float bestAhead = float.MaxValue;
                for (int j = 0; !Ended && j < entryS.Length; j++)
                {
                    float ahead = track.Forward(prev, track.Wrap(entryS[j]));
                    if (ahead < bestAhead && CanEnter(j, car.color))
                    {
                        bestAhead = ahead;
                        target = j;
                    }
                }

                float room = Mathf.Max(0f, free);
                float speedLimit = Mathf.Min(loopSpeed, Mathf.Sqrt(2f * moveSpeed * room));
                if (target >= 0)
                {
                    float turnSpeed = ParkingEntryReady(car, target, true) ? ParkingSpeed : 0f;
                    speedLimit = Mathf.Min(speedLimit, Mathf.Sqrt(turnSpeed * turnSpeed + 2f * moveSpeed * bestAhead));
                }
                float oldSpeed = loopSpeeds[car];
                float nextSpeed = Mathf.Min(oldSpeed + moveSpeed * dt, speedLimit);
                float step = Mathf.Min(bestAhead, Mathf.Min(room, nextSpeed * dt));
                bool atEntry = target >= 0 && bestAhead <= step;
                car.s = atEntry ? track.Wrap(entryS[target]) : track.Wrap(prev + step);
                float speed = step / dt;
                if (oldSpeed > loopSpeed * 0.5f && nextSpeed <= loopSpeed * 0.5f) car.Brake(0.5f, movingForward: true);
                loopSpeeds[car] = speed;
                car.transform.localPosition = track.Eval(car.s, out var dir);
                car.SetAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f, false);
                if (atEntry) parkArrivals.Add((car, target, Mathf.Min(nextSpeed, ParkingSpeed)));
            }

            foreach (var (car, column, speed) in parkArrivals)
            {
                if (!CanEnter(column, car.color) || !ParkingEntryReady(car, column, false)) continue;
                Enter(car, column, speed);
            }
        }

        bool ParkingTailClear(CarView car) => track.TopY - car.transform.localPosition.y >= laneStep;

        bool ParkingEntryReady(CarView car, int j, bool useSnapshot)
        {
            var column = cols[j];
            if (column.Count < Level.capacity)
                return incoming[j] == 0 || ParkingTailClear(column[column.Count - 1]);
            if (incoming[j] > 0) return false;
            var outgoing = column[0];
            float s = track.SAtBottom(colX[j]);
            float travel = Vector3.Distance(outgoing.transform.localPosition, track.Eval(s, out _)) / (moveSpeed * 0.9f);
            return !outgoing.Moving && CanStartMerge(s, travel, car, useSnapshot);
        }

        void DriveToMerge(CarView car, float s, float speed, Vector3[] curve = null, float distance = 0f)
        {
            mergingCar = car;
            mergingS = curve == null ? s : track.SAtBottom(laneX[car.lane]);
            void Arrive()
            {
                if (mergingCar != car) return;
                Debug.Assert(loopCars.TrueForAll(other =>
                    track.Forward(other.s, s) >= spacing - 0.001f * cell &&
                    track.Forward(s, other.s) >= spacing - 0.001f * cell), "并线完成时环道车距不足");
                car.s = s;
                car.state = CarState.OnLoop;
                loopSpeeds.Add(car, loopSpeed);
                loopCars.Add(car);
                mergingCar = null;
                if (curve != null) AdvanceLane(car.lane);
            }
            if (curve == null) car.MoveAlong(new[] { (Vector3)track.Eval(s, out _) }, speed, Arrive, brake: false);
            else car.DepartAlong(curve, distance, speed, Arrive);
            car.BeginTireTrail(fxRoot);
        }

        void ProcessDispatchQueue()
        {
            if (Ended || mergingCar != null || queued.Count == 0) return;
            var chosen = queued[0];
            if (chosen.Moving) return;
            float startS = track.SAtBottom(laneX[chosen.lane]);
            float s = track.Wrap(startS + 0.75f * cell);
            float distance = track.DeparturePath(chosen.transform.localPosition, s, departurePath);
            if (!CanStartMerge(s, CarView.DepartureTime(distance, loopSpeed), reservationStart: startS)) return;
            Debug.Assert(lanes[chosen.lane].Count > 0 && lanes[chosen.lane][0] == chosen,
                "待发车辆必须仍在车道队首");
            queued.RemoveAt(0);
            Dispatch(chosen.lane, s, distance);
        }

        bool CanStartMerge(float s, float travel, CarView departing = null, bool useSnapshot = false, float? reservationStart = null)
        {
            if (mergingCar != null) return false;
            // 按巡航速度预留后车在驶入期间能走的最远距离。
            float rearClearance = spacing * 1.2f + travel * loopSpeed;
            // 空档较短时，给后车留出在发车口前刹停的距离。
            if (reservationStart.HasValue)
                rearClearance = Mathf.Min(rearClearance, track.Forward(reservationStart.Value, s) + spacing + loopSpeed * loopSpeed / (2f * moveSpeed));
            foreach (var c in loopCars)
            {
                if (c == departing) continue;
                float position = useSnapshot ? loopPositions[c] : c.s;
                if (track.Forward(position, s) < rearClearance || track.Forward(s, position) < spacing) return false;
            }
            return true;
        }

        void CheckStuck(float dt)
        {
            if (Ended) { stuckTimer = 0; return; }
            bool busy = transit > 0 || queued.Count > 0;
            foreach (var c in allCars)
            {
                if (c == null) continue;
                if (c.state == CarState.ToLoop || c.state == CarState.Entering || c.state == CarState.Ejecting || c.state == CarState.Clearing)
                {
                    busy = true;
                    break;
                }
            }
            for (int j = 0; j < clearing.Length && !busy; j++) if (clearing[j]) busy = true;
            if (busy) { stuckTimer = 0; return; }

            bool anyTarget = false;
            foreach (var c in loopCars) if (AnyTargetFor(c.color)) { anyTarget = true; break; }

            bool canTap = false;
            for (int l = 0; l < lanes.Length; l++) if (LaneReady(l)) { canTap = true; break; }

            if (!anyTarget && !canTap)
            {
                stuckTimer += dt;
                if (stuckTimer > 2.2f)
                {
                    Ended = true;
                    stuckTimer = 0;
                    Debug.Log("[LoopPark] STUCK " + Dump());
                    Sfx.Play("fail");
                    Failed?.Invoke();
                }
            }
            else stuckTimer = 0;
        }

        void AnimateCounter(float dt)
        {
            if (counterText == null) return;
            counterText.text = $"{LoopCount}/{LoopLimit}";
            bool full = LoopCount >= LoopLimit;
            float pulse = full ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8f) : 0f;
            counterBg.color = full ? Color.Lerp(new Color(0.85f, 0.2f, 0.2f), new Color(1f, 0.45f, 0.4f), pulse) : new Color(0.22f, 0.22f, 0.3f);
            if (edgeMesh != null)
            {
                var c = full ? Color.Lerp(edgeColor, new Color(1f, 0.25f, 0.25f), 0.4f + 0.6f * pulse) : edgeColor;
                var cs = edgeMesh.colors;
                if (cs.Length > 0 && cs[0] != c)
                {
                    for (int i = 0; i < cs.Length; i++) cs[i] = c;
                    edgeMesh.colors = cs;
                }
            }
            counterPunch = Mathf.MoveTowards(counterPunch, 0, dt * 2f);
            float k = 1 + Mathf.Sin(counterPunch * 25f) * counterPunch * 0.4f;
            counterBg.transform.localScale = Vector3.one * cell * k;
        }

        void PunchCounter() => counterPunch = 0.6f;

        void Emit(string id, CarView car) => OnEvent?.Invoke(id, car);

        // ===================================================================
        // 道具
        // ===================================================================
        public bool CanUseBooster(Booster b)
        {
            if (Ended || Paused) return false;
            if (b == Booster.Shuffle)
            {
                foreach (var c in allCars)
                    if (c != null && (c.state == CarState.Entering || c.state == CarState.Clearing)) return false;
                foreach (var cl in clearing) if (cl) return false;
            }
            if (b == Booster.Reveal)
            {
                foreach (var c in allCars) if (c != null && c.hidden) return true;
                return false;
            }
            return true;
        }

        public void UseBooster(Booster b)
        {
            switch (b)
            {
                case Booster.ExtraSlot:
                    LoopLimit += 1;
                    stuckTimer = 0;
                    PunchCounter();
                    RecountLoop();
                    break;
                case Booster.Shuffle:
                    {
                        var parked = new List<CarView>();
                        for (int j = 0; j < cols.Length; j++) if (!closed[j] && !Covered(j)) parked.AddRange(cols[j]);
                        var colors = new List<int>();
                        foreach (var p in parked) colors.Add(p.color);
                        for (int tries = 0; tries < 30; tries++)
                        {
                            for (int i = colors.Count - 1; i > 0; i--)
                            {
                                int k = UnityEngine.Random.Range(0, i + 1);
                                (colors[i], colors[k]) = (colors[k], colors[i]);
                            }
                            // 尽量让至少一辆环路上的车能进列
                            int idx = 0;
                            bool ok = loopCars.Count == 0;
                            for (int j = 0; j < cols.Length && !ok; j++)
                            {
                                if (closed[j]) continue;
                                if (cols[j].Count == 0) { ok = true; break; }
                                int top = colors[idx + cols[j].Count - 1];
                                foreach (var lc in loopCars) if (lc.color == top) ok = true;
                                idx += cols[j].Count;
                            }
                            if (ok) break;
                        }
                        for (int i = 0; i < parked.Count; i++)
                        {
                            parked[i].color = colors[i];
                            parked[i].Refresh();
                            parked[i].Punch(0.4f);
                        }
                        for (int j = 0; j < cols.Length; j++)
                        {
                            RevealEnds(j);
                            if (!closed[j] && IsClearable(j) && incoming[j] == 0)
                            {
                                clearing[j] = true;
                                StartCoroutine(ClearColumn(j));
                            }
                        }
                        stuckTimer = 0;
                        Sfx.Play("reveal");
                        break;
                    }
                case Booster.Reveal:
                    foreach (var c in allCars) if (c != null && c.hidden) c.Reveal();
                    Sfx.Play("reveal");
                    break;
            }
        }

        /// <summary>死局救援：把停车列里的车按颜色整理，满列同色的立即消除。</summary>
        public void RescueSort()
        {
            // 被锁卡住的车道直接开锁
            for (int l = 0; l < lanes.Length; l++)
                if (lanes[l].Count > 0 && lanes[l][0].locked) { keyStack++; }
            TryUnlock();
            var parked = new List<CarView>();
            for (int j = 0; j < cols.Length; j++) if (!closed[j] && !Covered(j)) parked.AddRange(cols[j]);
            var colors = new List<int>();
            foreach (var p in parked) colors.Add(p.color);
            // 数量多的颜色排前面，同色连续
            var count = new Dictionary<int, int>();
            foreach (var c in colors) { count.TryGetValue(c, out var v); count[c] = v + 1; }
            colors.Sort((a, b) => count[a] != count[b] ? count[b].CompareTo(count[a]) : a.CompareTo(b));
            // 按列从下到上依次填回（每列车数不变）
            int idx = 0;
            for (int j = 0; j < cols.Length; j++)
            {
                if (closed[j] || Covered(j)) continue;
                foreach (var car in cols[j])
                {
                    car.color = colors[idx++];
                    car.hidden = false;
                    car.Refresh();
                    car.Punch(0.4f);
                }
            }
            for (int j = 0; j < cols.Length; j++)
            {
                if (!closed[j] && IsClearable(j) && incoming[j] == 0 && !clearing[j])
                {
                    clearing[j] = true;
                    StartCoroutine(ClearColumn(j));
                }
            }
            stuckTimer = 0;
            Sfx.Play("reveal");
        }

        // ===================================================================
        // 钥匙锁
        // ===================================================================
        void BuildKeyBadge()
        {
            bool any = false;
            foreach (var c in Level.columns) foreach (var s in c.cars) if (s.k) any = true;
            foreach (var l in Level.lanes) foreach (var s in l.cars) if (s.lk) any = true;
            if (!any) return;
            float W = Camera.main.orthographicSize * Camera.main.aspect;
            float x = Mathf.Max(-W + 0.7f * cell, laneX[0] - 1.25f * cell);
            var go = new GameObject("KeyStack");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(x, laneFrontY, -1f);
            keyBadge = go.transform;
            var bg = go.AddComponent<SpriteRenderer>();
            bg.sprite = SpriteFactory.Circle();
            bg.color = new Color(0.18f, 0.18f, 0.26f, 0.85f);
            bg.sortingOrder = 40;
            go.transform.localScale = Vector3.one * cell * 0.95f;
            var icon = new GameObject("Icon").AddComponent<SpriteRenderer>();
            icon.transform.SetParent(go.transform, false);
            icon.transform.localPosition = new Vector3(0, 0.12f, 0);
            icon.transform.localScale = Vector3.one * 0.7f;
            icon.sprite = SpriteFactory.KeyIcon();
            icon.color = new Color(1f, 0.82f, 0.2f);
            icon.sortingOrder = 41;
            var t = new GameObject("Count");
            t.transform.SetParent(go.transform, false);
            t.transform.localPosition = new Vector3(0, -0.28f, 0);
            t.transform.localScale = Vector3.one * 0.05f;
            keyText = t.AddComponent<TextMesh>();
            keyText.font = SpriteFactory.Font;
            keyText.GetComponent<MeshRenderer>().sharedMaterial = SpriteFactory.Font.material;
            keyText.GetComponent<MeshRenderer>().sortingOrder = 42;
            keyText.fontSize = 64;
            keyText.fontStyle = FontStyle.Bold;
            keyText.anchor = TextAnchor.MiddleCenter;
            keyText.color = Color.white;
            UpdateKeyText();
        }

        void UpdateKeyText()
        {
            if (keyText != null) keyText.text = "x" + keyStack;
        }

        void ReleaseKey(CarView car)
        {
            car.hasKey = false;
            car.RefreshBadges();
            keyStack++;
            if (keyBadge != null) StartCoroutine(FlyIcon(car.transform.position, keyBadge.position, SpriteFactory.KeyIcon(), new Color(1f, 0.82f, 0.2f)));
            UpdateKeyText();
            Emit("key", car);
            TryUnlock();
        }

        IEnumerator FlyIcon(Vector3 from, Vector3 to, Sprite sp, Color c)
        {
            var go = new GameObject("fly");
            go.transform.SetParent(fxRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.color = c;
            sr.sortingOrder = 60;
            float t = 0;
            from.z = to.z = -1.5f;
            while (t < 1)
            {
                t += Time.deltaTime / 0.55f;
                float k = Mathf.SmoothStep(0, 1, t);
                go.transform.position = Vector3.Lerp(from, to, k) + new Vector3(0, Mathf.Sin(k * Mathf.PI) * cell, 0);
                go.transform.localScale = Vector3.one * cell * (0.6f + Mathf.Sin(k * Mathf.PI) * 0.3f);
                yield return null;
            }
            Destroy(go);
            if (keyBadge != null) keyBadge.localScale = Vector3.one * cell * 1.15f;
            yield return new WaitForSeconds(0.1f);
            if (keyBadge != null) keyBadge.localScale = Vector3.one * cell * 0.95f;
        }

        /// <summary>有钥匙时，给队首上锁的车开锁（按车道顺序）。</summary>
        void TryUnlock()
        {
            for (int l = 0; l < lanes.Length && keyStack > 0; l++)
            {
                if (lanes[l].Count == 0) continue;
                var car = lanes[l][0];
                if (!car.locked) continue;
                keyStack--;
                car.locked = false;
                car.RefreshBadges();
                car.Punch(0.4f);
                FxParticle.Burst(fxRoot, car.transform.position, new Color(1f, 0.85f, 0.3f), 12, cell * 0.12f, cell * 3f);
                Sfx.Play("reveal");
                Emit("unlock", car);
            }
            UpdateKeyText();
            stuckTimer = 0;
        }

        // ===================================================================
        // 帘子
        // ===================================================================
        void BuildCurtain(int j, int color, int value)
        {
            curtainLeft[j] = value;
            curtainColor[j] = color;
            float h = ColumnHeight + 0.12f * cell;
            var go = ShapeMesh.RoundedRect("Curtain" + j, root, new Vector2(colX[j], colBottomY + ColumnHeight / 2),
                new Vector2(cell / 2 + 0.03f * cell, h / 2), 0.18f * cell, Color.Lerp(Palette.Get(color), new Color(0.2f, 0.2f, 0.3f), 0.45f), 35);
            go.transform.localPosition = new Vector3(0, 0, -1.2f);
            curtainGo[j] = go;
            // 褶皱条纹
            for (int k = 0; k < 3; k++)
            {
                var stripe = new GameObject("fold").AddComponent<SpriteRenderer>();
                stripe.transform.SetParent(go.transform, false);
                stripe.sprite = SpriteFactory.Square();
                stripe.color = new Color(0, 0, 0, 0.15f);
                stripe.sortingOrder = 36;
                stripe.transform.localPosition = new Vector3(colX[j] + (k - 1) * cell * 0.28f, colBottomY + ColumnHeight / 2, 0);
                stripe.transform.localScale = new Vector3(0.05f * cell, h * 0.92f, 1);
            }
            // 颜色小车 + 数字
            var icon = new GameObject("icon").AddComponent<SpriteRenderer>();
            icon.transform.SetParent(go.transform, false);
            icon.sprite = SpriteFactory.CarBody();
            icon.color = Palette.Get(color);
            icon.sortingOrder = 37;
            icon.transform.localPosition = new Vector3(colX[j], colBottomY + ColumnHeight * 0.62f, 0);
            icon.transform.localScale = Vector3.one * cell * 0.6f;
            var t = new GameObject("count");
            t.transform.SetParent(go.transform, false);
            t.transform.localPosition = new Vector3(colX[j], colBottomY + ColumnHeight * 0.36f, 0);
            t.transform.localScale = Vector3.one * cell * 0.09f;
            var tm = t.AddComponent<TextMesh>();
            tm.font = SpriteFactory.Font;
            tm.GetComponent<MeshRenderer>().sharedMaterial = SpriteFactory.Font.material;
            tm.GetComponent<MeshRenderer>().sortingOrder = 38;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;
            tm.text = value.ToString();
            curtainText[j] = tm;
            foreach (var car in cols[j]) car.gameObject.SetActive(false);
        }

        void CurtainProgress(int color, int count)
        {
            for (int j = 0; j < cols.Length; j++)
            {
                if (curtainLeft[j] <= 0 || curtainColor[j] != color) continue;
                curtainLeft[j] = Mathf.Max(0, curtainLeft[j] - count);
                if (curtainText[j] != null) curtainText[j].text = curtainLeft[j].ToString();
                if (curtainLeft[j] == 0) StartCoroutine(OpenCurtain(j));
                else if (curtainGo[j] != null) StartCoroutine(PunchTransform(curtainText[j].transform));
            }
        }

        IEnumerator PunchTransform(Transform t)
        {
            var s0 = t.localScale;
            for (float k = 0; k < 1; k += Time.deltaTime / 0.3f)
            {
                t.localScale = s0 * (1 + Mathf.Sin(k * Mathf.PI) * 0.4f);
                yield return null;
            }
            t.localScale = s0;
        }

        IEnumerator OpenCurtain(int j)
        {
            var go = curtainGo[j];
            curtainGo[j] = null;
            foreach (var car in cols[j]) car.gameObject.SetActive(true);
            RevealEnds(j);
            Sfx.Play("clear", 1.3f);
            Emit("curtainopen", null);
            stuckTimer = 0;
            if (go == null) yield break;
            // 帘子向上收起（以列顶为轴缩放）
            float top = colBottomY + ColumnHeight + 0.06f * cell;
            for (float k = 0; k < 1; k += Time.deltaTime / 0.45f)
            {
                float sy = 1 - k;
                go.transform.localScale = new Vector3(1, sy, 1);
                go.transform.localPosition = new Vector3(0, top * (1 - sy), -1.2f);
                yield return null;
            }
            Destroy(go);
        }

        // ===================================================================
        // 车库（锁定车位）
        // ===================================================================
        void BuildGarage(int j, int color)
        {
            garageColorOf[j] = color;
            float h = ColumnHeight + 0.12f * cell;
            var go = ShapeMesh.RoundedRect("Garage" + j, root, new Vector2(colX[j], colBottomY + ColumnHeight / 2),
                new Vector2(cell / 2 + 0.03f * cell, h / 2), 0.14f * cell, new Color(0.30f, 0.32f, 0.40f), 35);
            go.transform.localPosition = new Vector3(0, 0, -1.2f);
            garageGo[j] = go;
            // 卷帘门横条
            int slats = Level.capacity * 2;
            for (int k = 0; k < slats; k++)
            {
                var sl = new GameObject("slat").AddComponent<SpriteRenderer>();
                sl.transform.SetParent(go.transform, false);
                sl.sprite = SpriteFactory.Square();
                sl.color = new Color(0, 0, 0, 0.18f);
                sl.sortingOrder = 36;
                sl.transform.localPosition = new Vector3(colX[j], colBottomY + (k + 0.5f) * (ColumnHeight / slats), 0);
                sl.transform.localScale = new Vector3(cell * 0.9f, 0.04f * cell, 1);
            }
            var plate = new GameObject("plate").AddComponent<SpriteRenderer>();
            plate.transform.SetParent(go.transform, false);
            plate.sprite = SpriteFactory.Circle();
            plate.color = Palette.Get(color);
            plate.sortingOrder = 37;
            plate.transform.localPosition = new Vector3(colX[j], colBottomY + ColumnHeight * 0.5f, 0);
            plate.transform.localScale = Vector3.one * cell * 0.75f;
            var lockIcon = new GameObject("lock").AddComponent<SpriteRenderer>();
            lockIcon.transform.SetParent(go.transform, false);
            lockIcon.sprite = SpriteFactory.LockIcon();
            lockIcon.color = Color.white;
            lockIcon.sortingOrder = 38;
            lockIcon.transform.localPosition = new Vector3(colX[j], colBottomY + ColumnHeight * 0.5f, 0);
            lockIcon.transform.localScale = Vector3.one * cell * 0.55f;
            foreach (var car in cols[j]) car.gameObject.SetActive(false);
        }

        void GarageProgress(int color)
        {
            for (int j = 0; j < cols.Length; j++)
            {
                if (garageColorOf[j] != color) continue;
                garageColorOf[j] = -1;
                var go = garageGo[j];
                garageGo[j] = null;
                foreach (var car in cols[j]) car.gameObject.SetActive(Covered(j) == false || curtainLeft[j] <= 0);
                RevealEnds(j);
                Sfx.Play("clear", 1.2f);
                Emit("garageopen", null);
                if (Verbose) Debug.Log($"[LoopPark] garage open col{j} color{color}");
                stuckTimer = 0;
                if (go != null) StartCoroutine(RollUp(go, j));
            }
        }

        IEnumerator RollUp(GameObject go, int j)
        {
            float top = colBottomY + ColumnHeight + 0.06f * cell;
            for (float k = 0; k < 1; k += Time.deltaTime / 0.45f)
            {
                float sy = 1 - k;
                go.transform.localScale = new Vector3(1, sy, 1);
                go.transform.localPosition = new Vector3(0, top * (1 - sy), -1.2f);
                yield return null;
            }
            Destroy(go);
        }

        // ===================================================================
        // 冰车道
        // ===================================================================
        void BuildIce(int j, int count)
        {
            iceLeft[j] = count;
            float h = ColumnHeight + 0.12f * cell;
            var go = ShapeMesh.RoundedRect("Ice" + j, root, new Vector2(colX[j], colBottomY + ColumnHeight / 2),
                new Vector2(cell / 2 + 0.05f * cell, h / 2 + 0.03f * cell), 0.16f * cell, new Color(0.72f, 0.9f, 1f, 0.62f), 35);
            go.transform.localPosition = new Vector3(0, 0, -1.2f);
            iceGo[j] = go;
            for (int k = 0; k < 4; k++)
            {
                var shine = new GameObject("shine").AddComponent<SpriteRenderer>();
                shine.transform.SetParent(go.transform, false);
                shine.sprite = SpriteFactory.Square();
                shine.color = new Color(1, 1, 1, 0.45f);
                shine.sortingOrder = 36;
                shine.transform.localPosition = new Vector3(colX[j] - cell * 0.22f + k * 0.05f * cell, colBottomY + ColumnHeight * (0.2f + 0.2f * k), 0);
                shine.transform.localRotation = Quaternion.Euler(0, 0, 35);
                shine.transform.localScale = new Vector3(0.05f * cell, 0.35f * cell, 1);
            }
            var bg = new GameObject("badge").AddComponent<SpriteRenderer>();
            bg.transform.SetParent(go.transform, false);
            bg.sprite = SpriteFactory.Circle();
            bg.color = new Color(0.25f, 0.55f, 0.85f);
            bg.sortingOrder = 37;
            bg.transform.localPosition = new Vector3(colX[j], colBottomY + ColumnHeight * 0.5f, 0);
            bg.transform.localScale = Vector3.one * cell * 0.62f;
            iceText[j] = MakeText(go.transform, new Vector3(colX[j], colBottomY + ColumnHeight * 0.5f, 0), cell * 0.08f, 38, count.ToString());
        }

        void IceCountdown(int clearedColumn)
        {
            for (int j = 0; j < cols.Length; j++)
            {
                if (j == clearedColumn || iceLeft[j] <= 0) continue;
                iceLeft[j]--;
                if (iceText[j] != null) iceText[j].text = iceLeft[j].ToString();
                var center = root.TransformPoint(new Vector3(colX[j], colBottomY + ColumnHeight / 2));
                FxParticle.Burst(fxRoot, center, new Color(0.8f, 0.95f, 1f), 6, cell * 0.12f, cell * 2f);
                if (iceLeft[j] == 0)
                {
                    FxParticle.Burst(fxRoot, center, new Color(0.85f, 0.97f, 1f), 24, cell * 0.16f, cell * 5f);
                    if (iceGo[j] != null) Destroy(iceGo[j]);
                    iceGo[j] = null;
                    RevealEnds(j);
                    Sfx.Play("clear", 1.5f);
                    Emit("icebreak", null);
                    if (Verbose) Debug.Log($"[LoopPark] ice break col{j}");
                    stuckTimer = 0;
                }
                else Sfx.Play("reveal", 1.3f);
            }
        }

        // ===================================================================
        // 电梯
        // ===================================================================
        void BuildElevatorBadge(int j)
        {
            var pos = new Vector3(colX[j], colBottomY - gateH * 0.55f, -1f);
            var plate = new GameObject("ElevatorPlate").AddComponent<SpriteRenderer>();
            plate.transform.SetParent(root, false);
            plate.sprite = SpriteFactory.RoundedRect(64, 20);
            plate.drawMode = SpriteDrawMode.Sliced;
            plate.size = new Vector2(1f, 0.42f);
            plate.color = new Color(0.95f, 0.6f, 0.15f);
            plate.sortingOrder = 38;
            plate.transform.localPosition = pos;
            plate.transform.localScale = Vector3.one * cell;
            elevatorText[j] = MakeText(root, pos + new Vector3(0, 0, -0.01f), cell * 0.045f, 39, "");
            elevatorText[j].GetComponent<MeshRenderer>().gameObject.name = "ElevatorText";
            elevatorPlate[j] = plate.gameObject;
            UpdateElevatorBadge(j);
        }

        void UpdateElevatorBadge(int j)
        {
            if (elevatorText[j] == null) return;
            int left = Level.columns[j].levels.Count - levelIdx[j];
            elevatorText[j].text = left > 0 ? "电梯 " + left : "";
            if (left <= 0 && elevatorPlate[j] != null) { Destroy(elevatorPlate[j]); elevatorPlate[j] = null; }
        }

        void ElevatorRise(int j)
        {
            var lv = Level.columns[j].levels;
            if (closed[j] || levelIdx[j] >= lv.Count) return;
            var specs = lv[levelIdx[j]].cars;
            levelIdx[j]++;
            UpdateElevatorBadge(j);
            for (int i = 0; i < specs.Count; i++)
            {
                var cv = NewCar(specs[i]);
                cv.state = CarState.Parked;
                cv.column = j;
                cv.transform.localPosition = SlotPos(j, i);
                cv.SetAngle(180, true);
                cv.hasKey = specs[i].k;
                cv.RefreshBadges();
                cols[j].Add(cv);
                StartCoroutine(GrowIn(cv, i * 0.05f));
            }
            RevealEnds(j);
            if (IsClearable(j)) { clearing[j] = true; StartCoroutine(ClearColumn(j)); }
            Sfx.Play("tap", 0.8f);
            Emit("elevator", null);
            if (Verbose) Debug.Log($"[LoopPark] elevator rise col{j} level {levelIdx[j]} {Dump()}");
            stuckTimer = 0;
        }

        IEnumerator GrowIn(CarView cv, float delay)
        {
            var v = cv.Visual;
            var target = v.localScale;
            v.localScale = Vector3.zero;
            yield return new WaitForSeconds(delay);
            for (float k = 0; k < 1 && cv != null; k += Time.deltaTime / 0.25f)
            {
                v.localScale = target * Mathf.SmoothStep(0, 1, k);
                yield return null;
            }
            if (cv != null) { v.localScale = target; cv.Punch(0.3f); }
        }

        // ===================================================================
        // 发车道车库生成器
        // ===================================================================
        void BuildGenerator(int l)
        {
            var go = new GameObject("Generator" + l);
            go.transform.SetParent(root, false);
            var body = go.AddComponent<SpriteRenderer>();
            body.sprite = SpriteFactory.RoundedRect(64, 18);
            body.drawMode = SpriteDrawMode.Sliced;
            body.size = new Vector2(1f, 1f);
            body.color = new Color(0.36f, 0.30f, 0.42f);
            body.sortingOrder = 24;
            var roof = new GameObject("roof").AddComponent<SpriteRenderer>();
            roof.transform.SetParent(go.transform, false);
            roof.sprite = SpriteFactory.Square();
            roof.color = new Color(0.9f, 0.45f, 0.3f);
            roof.sortingOrder = 25;
            roof.transform.localPosition = new Vector3(0, 0.42f, 0);
            roof.transform.localScale = new Vector3(1.05f, 0.18f, 1);
            genGo[l] = go;
            genText[l] = MakeText(go.transform, new Vector3(0, -0.05f, 0), 0.075f, 26, "");
            UpdateGenerator(l);
        }

        void UpdateGenerator(int l)
        {
            if (genGo[l] == null) return;
            if (genLeft[l] <= 0)
            {
                FxParticle.Burst(fxRoot, genGo[l].transform.position, new Color(0.9f, 0.6f, 0.4f), 12, cell * 0.12f, cell * 3f);
                Destroy(genGo[l]);
                genGo[l] = null;
                return;
            }
            genGo[l].transform.localPosition = LanePos(l, genVisible[l]) + new Vector3(0, 0, -1f);
            genGo[l].transform.localScale = Vector3.one * cell * 0.92f;
            genText[l].text = genLeft[l].ToString();
        }

        TextMesh MakeText(Transform parent, Vector3 localPos, float scale, int order, string text)
        {
            var t = new GameObject("Text");
            t.transform.SetParent(parent, false);
            t.transform.localPosition = localPos;
            t.transform.localScale = Vector3.one * scale;
            var tm = t.AddComponent<TextMesh>();
            tm.font = SpriteFactory.Font;
            tm.GetComponent<MeshRenderer>().sharedMaterial = SpriteFactory.Font.material;
            tm.GetComponent<MeshRenderer>().sortingOrder = order;
            tm.fontSize = 64;
            tm.fontStyle = FontStyle.Bold;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;
            tm.text = text;
            return tm;
        }

        // ===================================================================
        // 自动演示（测试用）
        // ===================================================================
        void AutoStep(float dt)
        {
            if (Ended) return;
            autoTimer -= dt;
            autoIdle += dt;
            if (autoTimer > 0) return;
            autoTimer = 0.45f;
            if (LoopCount >= LoopLimit) return;
            int best = -1;
            float bestScore = float.MinValue;
            for (int l = 0; l < lanes.Length; l++)
            {
                if (!LaneReady(l)) continue;
                int color = lanes[l][0].color;
                float score = UnityEngine.Random.value;
                if (AnyTargetFor(color)) score += 5;
                // 避免把环路塞满
                if (LoopCount >= LoopLimit - 1 && !AnyTargetFor(color)) score -= 10;
                if (score > bestScore) { bestScore = score; best = l; }
            }
            // 长时间没有进展时，允许冒险发车（避免自动试玩干等）
            if (best >= 0 && (bestScore > -5 || autoIdle > 3f)) { if (TapLane(best)) autoIdle = 0; }
        }

        /// <summary>提示：返回推荐点击的车道（用一步前瞻模拟）。</summary>
        public int SuggestLane()
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int l = 0; l < lanes.Length; l++)
            {
                if (lanes[l].Count == 0) continue;
                if (lanes[l][0].partner != null && !LaneReady(l)) continue;
                float score = AnyTargetFor(lanes[l][0].color) ? 10 : 0;
                score -= l * 0.01f;
                if (score > bestScore) { bestScore = score; best = l; }
            }
            return best;
        }
    }
}
