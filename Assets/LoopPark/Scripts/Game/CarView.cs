using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    public enum CarState
    {
        InLane,     // 在发车道排队
        ToLoop,     // 从发车道开往环路
        OnLoop,     // 在环路上行驶
        Entering,   // 驶入停车列
        Parked,     // 停在列里
        Ejecting,   // 被挤出，开往环路
        Clearing,   // 正在消除
    }

    /// <summary>可选：自定义车辆 prefab（Spine / 3D）实现此接口以接收颜色与事件。</summary>
    public interface ICarSkin
    {
        void SetCarColor(Color color, bool hidden, int colorIndex);
        void PlayEvent(string evt); // "tap" "park" "push" "clear" "reveal"
        void SetRevealArea(bool upperArea);
        /// <summary>绑带配对状态：成对车辆显示车顶吸盘附件，并把软管朝向搭档车的吸盘。</summary>
        void SetLinked(bool on, CarStrap partner);
    }

    // Apply path motion after GameController has advanced the loop for this frame.
    [DefaultExecutionOrder(10)]
    public class CarView : MonoBehaviour
    {
        public int color;
        public bool hidden;
        public CarState state;
        public float s;          // 环路参数
        public int lane = -1;
        public int column = -1;
        public int link;
        public CarView partner;
        public bool hasKey;   // 停车列里的车带钥匙
        public bool locked;   // 发车道里的车上锁
        SpriteRenderer keyIcon, lockIcon;

        SpriteRenderer under, body, detail;
        TextMesh question, questionShadow;
        Transform qRoot;
        Transform model;   // 3D 模型实例（用于整体倾斜出透视感）
        SpriteRenderer qRim, qBg;
        ICarSkin customSkin;
        Transform visual;
        CarLayerDepth modelDepth;
        // 持有订阅源，以便对象停用时对同一配置解除订阅。
        SkinConfig appearance;

        void OnEnable()
        {
            appearance = SkinConfig.Instance;
            appearance.CarAppearanceChanged += Refresh;
            if (visual != null) Refresh();
        }

        void OnDisable()
        {
            if (appearance != null) appearance.CarAppearanceChanged -= Refresh;
        }

        // 路径移动
        readonly List<Vector3> path = new List<Vector3>();
        // Index into the immutable path for this frame; removing points would allocate and shift the list.
        int pathIndex;
        float moveSpeed;
        // Portion of the current frame left after GameController starts this movement.
        float parkingSpeed, parkingAcceleration, parkingDistance, firstPathFrameTime;
        CarView parkingLeader;
        float parkingGap;
        float departureElapsed, departureRamp, departureDuration;
        Action onArrive;
        bool faceMovement;
        float targetAngle;
        float punch;
        bool brakeOnArrive;
        Vector3 lastMoveDir;

        // 刹车前倾（弹簧，单位：度，正 = 车头向下）
        float pitch, pitchVel;
        // 区域视角使用独立阻尼，避免发车、环路和停车之间突然切换。
        float viewTilt, viewTiltVelocity;
        bool viewTiltInitialized;
        // 待机（发车道第一排）：发动机抖动 + 尾气
        bool idleWanted;
        float idleT;
        GameObject idleExhaustObject;
        ParticleSystem[] idleExhaustSystems;
        ParticleSystem exhaust;
        static Material exhaustMat;
        TireTrail tireTrail;

        // A path is active until every sampled point has been consumed.
        public bool Moving => pathIndex < path.Count;

        public static CarView Create(Transform parent, int color, bool hidden, float width)
        {
            var go = new GameObject("Car");
            go.transform.SetParent(parent, false);
            var cv = go.AddComponent<CarView>();
            cv.Build(width);
            cv.color = color;
            cv.hidden = hidden;
            cv.Refresh();
            return cv;
        }

        void Build(float width)
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.one * width;
            visualBase = width;
            var skin = SkinConfig.Instance;
            if (skin.carPrefab != null)
            {
                var inst = Instantiate(skin.carPrefab, visual);
                inst.transform.localPosition = new Vector3(0, 0, -0.3f); // 3D 模型放在棋盘前面
                model = inst.transform;
                modelDepth = inst.AddComponent<CarLayerDepth>();
                modelDepth.Initialize(transform);
                customSkin = inst.GetComponentInChildren<ICarSkin>();
            }
            else
            {
                under = NewLayer("Under", SpriteFactory.CarUnder(), -1);
                body = NewLayer("Body", SpriteFactory.CarBody(), 0);
                detail = NewLayer("Detail", SpriteFactory.CarDetail(), 1);
            }
            // 问号：车身染成问号紫，车顶印一个带深色描边的白色大问号（不再用圆形徽章）
            bool is3D = skin.carPrefab != null;
            qRoot = new GameObject("Q").transform;
            qRoot.SetParent(visual, false);
            qRoot.localPosition = new Vector3(0, 0, is3D ? -2.2f : 0f);
            qRim = new GameObject("Rim").AddComponent<SpriteRenderer>();
            qRim.transform.SetParent(qRoot, false);
            qRim.sprite = SpriteFactory.Circle();
            qRim.color = Color.white;
            qRim.transform.localScale = Vector3.one * 0.82f;
            qBg = new GameObject("Bg").AddComponent<SpriteRenderer>();
            qBg.transform.SetParent(qRoot, false);
            qBg.sprite = skin.hiddenIcon != null ? skin.hiddenIcon : SpriteFactory.Circle();
            qBg.color = skin.hiddenIcon != null ? Color.white : new Color(0.33f, 0.22f, 0.55f);
            qBg.transform.localScale = Vector3.one * 0.7f;
            // 只有配置了自定义图标，或 2D 占位车（车身本身不变紫）时才显示圆形底
            bool badge = skin.hiddenIcon != null || !is3D;
            qRim.gameObject.SetActive(badge && skin.hiddenIcon == null);
            qBg.gameObject.SetActive(badge);
            questionShadow = NewQuestionText("Shadow", new Vector3(0.025f, -0.05f, 0.01f), new Color(0.2f, 0.12f, 0.38f));
            question = NewQuestionText("Text", new Vector3(0, -0.02f, 0), Color.white);
            SetLayer(20);
        }

        TextMesh NewQuestionText(string n, Vector3 pos, Color col)
        {
            var q = new GameObject(n);
            q.transform.SetParent(qRoot, false);
            q.transform.localPosition = pos;
            q.transform.localScale = Vector3.one * 0.13f;
            var t = q.AddComponent<TextMesh>();
            t.font = SpriteFactory.Font;
            t.GetComponent<MeshRenderer>().sharedMaterial = SpriteFactory.Font.material;
            t.fontSize = 64;
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.fontStyle = FontStyle.Bold;
            t.text = "?";
            t.color = col;
            return t;
        }

        SpriteRenderer NewLayer(string n, Sprite sp, int offset)
        {
            var go = new GameObject(n);
            go.transform.SetParent(visual, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.sortingOrder = 20 + offset;
            return sr;
        }

        /// <summary>设置排序层级（移动中的车画在停着的车上面）。</summary>
        public void SetLayer(int baseOrder)
        {
            if (modelDepth != null) modelDepth.SetLayer(baseOrder);
            customSkin?.SetRevealArea(state == CarState.Parked);
            if (under) under.sortingOrder = baseOrder - 1;
            if (body) body.sortingOrder = baseOrder;
            if (detail) detail.sortingOrder = baseOrder + 1;
            qRim.sortingOrder = baseOrder + 2;
            qBg.sortingOrder = baseOrder + 3;
            questionShadow.GetComponent<MeshRenderer>().sortingOrder = baseOrder + 4;
            question.GetComponent<MeshRenderer>().sortingOrder = baseOrder + 5;
        }

        public void Refresh()
        {
            if (visual == null) return;
            var skin = SkinConfig.Instance;
            // 在模型层缩放，保留上层已有的刹车、弹跳和消除动画。
            if (model != null) model.localScale = skin.carModelScale;
            var c = hidden ? skin.hiddenCarColor : Palette.Get(color);
            if (customSkin != null) customSkin.SetCarColor(c, hidden, color);
            if (body)
            {
                var byColor = skin.carSpritesByColor;
                if (!hidden && byColor != null && color < byColor.Length && byColor[color] != null)
                {
                    body.sprite = byColor[color];
                    body.color = Color.white;
                    if (detail) detail.enabled = false;
                }
                else
                {
                    body.sprite = SpriteFactory.CarBody();
                    body.color = c;
                    if (detail) detail.enabled = true;
                }
            }
            qRoot.gameObject.SetActive(hidden);
            if (hidden && skin.hiddenIcon != null) { question.text = ""; questionShadow.text = ""; }
        }

        public void Reveal()
        {
            if (!hidden) return;
            // 先播放布料爆开，车身颜色在布料仍覆盖时切换，避免显色瞬间穿模。
            customSkin?.PlayEvent("reveal");
            hidden = false;
            Refresh();
            Punch(0.35f);
        }

        public void Punch(float amount = 0.25f)
        {
            punch = Mathf.Max(punch, amount);
        }

        public void PlayEvent(string evt) => customSkin?.PlayEvent(evt);

        /// <summary>绑带配对状态：转给 3D 皮肤显示吸盘附件，并让软管指向搭档（取代旧的 2D 粗绳）。</summary>
        public void SetLinked(bool on, CarStrap partner) => customSkin?.SetLinked(on, partner);

        /// <summary>本车的车顶附件；2D 占位车没有，返回 null。</summary>
        public CarStrap Strap => (customSkin as CarSkin3D)?.strap ?? GetComponentInChildren<CarStrap>(true);

        public void SetAngle(float deg, bool instant)
        {
            targetAngle = deg;
            if (instant) transform.rotation = Quaternion.Euler(0, 0, deg);
        }

        /// <summary>沿折线移动。brake = 到达终点时播放刹车前倾。</summary>
        public void MoveAlong(IList<Vector3> points, float speed, Action done, bool face = true, bool brake = true)
        {
            path.Clear();
            path.AddRange(points);
            // Start from the first point while keeping the caller's frame timing intact.
            pathIndex = 0;
            firstPathFrameTime = float.PositiveInfinity;
            moveSpeed = speed;
            parkingAcceleration = 0f;
            parkingLeader = null;
            departureDuration = 0f;
            onArrive = done;
            faceMovement = face;
            brakeOnArrive = brake;
        }

        public void ParkAlong(IList<Vector3> points, float entrySpeed, float maxSpeed, float acceleration, Action done,
            bool face = true, CarView leader = null, float gap = 0f, float firstFrameTime = float.PositiveInfinity)
        {
            MoveAlong(points, maxSpeed, done, face);
            // Parking can begin partway through a frame after loop movement reaches the entry.
            firstPathFrameTime = firstFrameTime;
            parkingSpeed = entrySpeed;
            parkingAcceleration = acceleration;
            parkingLeader = leader;
            parkingGap = gap;
            parkingDistance = 0f;
            var previous = transform.localPosition;
            foreach (var point in points)
            {
                parkingDistance += Vector3.Distance(previous, point);
                previous = point;
            }
        }

        internal static float ParkingFollowStep(float step, float y, float leaderY, float gap) =>
            Mathf.Min(step, Mathf.Max(0f, y - leaderY - gap));

        static float DepartureRamp(float distance, float speed) => Mathf.Min(0.35f, 2f * distance / speed);

        public static float DepartureTime(float distance, float speed) => distance / speed + DepartureRamp(distance, speed) * 0.5f;

        public void DepartAlong(IList<Vector3> points, float distance, float speed, Action done,
            float firstFrameTime = float.PositiveInfinity)
        {
            MoveAlong(points, speed, done, brake: false);
            // Preserve the unused slice of the dispatch frame for continuous motion.
            firstPathFrameTime = firstFrameTime;
            departureElapsed = 0f;
            departureRamp = DepartureRamp(distance, speed);
            departureDuration = DepartureTime(distance, speed);
        }

        internal static float DepartureDistance(float elapsed, float ramp, float speed)
        {
            if (elapsed >= ramp) return speed * (elapsed - ramp * 0.5f);
            float t = elapsed / ramp;
            // Integral of the smoothstep speed ramp, so progress is independent of frame rate.
            return speed * ramp * t * t * t * (1f - 0.5f * t);
        }

        public void BeginTireTrail(Transform parent)
        {
            tireTrail?.Stop();
            tireTrail = TireTrail.Begin(this, parent);
        }

        /// <summary>刹车前倾：车头朝行驶方向点一下再回弹（倒退停车则车尾下沉）。</summary>
        public void Brake(float strength = 1f, bool movingForward = false)
        {
            var dir = transform.parent != null ? transform.parent.TransformDirection(lastMoveDir) : lastMoveDir;
            float sign = movingForward || Vector3.Dot(dir, transform.up) >= 0 ? 1f : -1f;
            pitchVel += sign * SkinConfig.Instance.brakeStrength * strength;
        }

        /// <summary>发车道第一排：待机抖动 + 尾气。移动中/离开车道时自动停止。</summary>
        public void SetIdle(bool on) => idleWanted = on;

        bool IdleActive => idleWanted && state == CarState.InLane && pathIndex >= path.Count;

        void UpdateIdle(float dt)
        {
            bool on = IdleActive;
            if (on) idleT += dt;
            var skin = SkinConfig.Instance;
            if (skin.idleExhaustFx != null)
            {
                if (on && idleExhaustObject == null) BuildConfiguredExhaust(skin);
                if (idleExhaustObject != null)
                {
                    SetConfiguredExhaustPlaying(on);
                    return;
                }
            }
            if (on && skin.exhaustRate > 0 && exhaust == null) BuildExhaust();
            if (exhaust == null) return;
            if (on && skin.exhaustRate > 0)
            {
                if (!exhaust.isEmitting) exhaust.Play(true);
            }
            else if (exhaust.isEmitting) exhaust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void BuildConfiguredExhaust(SkinConfig skin)
        {
            // Parent under Visual so the authored effect follows the car's visual scale and depth.
            idleExhaustObject = Instantiate(skin.idleExhaustFx, visual);
            idleExhaustObject.name = "IdleExhaust";
            idleExhaustObject.transform.localPosition = new Vector3(skin.exhaustOffset.x, skin.exhaustOffset.y, -2f);
            idleExhaustObject.transform.localRotation = Quaternion.identity;
            idleExhaustSystems = idleExhaustObject.GetComponentsInChildren<ParticleSystem>(true);
            SetConfiguredExhaustPlaying(false);
        }

        void SetConfiguredExhaustPlaying(bool on)
        {
            if (idleExhaustSystems == null) return;
            foreach (var particle in idleExhaustSystems)
            {
                if (particle == null) continue;
                if (on) particle.Play(true);
                else particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void BuildExhaust()
        {
            var skin = SkinConfig.Instance;
            float w = visualBase;
            var go = new GameObject("Exhaust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(skin.exhaustOffset.x * w, skin.exhaustOffset.y * w, -2f);
            exhaust = go.AddComponent<ParticleSystem>();
            exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = exhaust.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // 车开走后烟团留在原地散开
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.32f * w, 0.44f * w);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var c = skin.exhaustColor;
            main.startColor = new ParticleSystem.MinMaxGradient(c, Color.Lerp(c, new Color(0.8f, 0.82f, 0.9f, c.a), 0.5f));
            main.maxParticles = 16;

            var em = exhaust.emission;
            em.rateOverTime = 0f;
            // 一团一团地冒：周期性 burst，比连续发射更像卡通尾气
            float interval = 1f / Mathf.Max(0.1f, skin.exhaustRate);
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 2, 1000, interval) });

            var shape = exhaust.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.03f * w;

            // 向车尾方向喷出，稍微偏向排气口一侧，并逐渐减速
            var vel = exhaust.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f * w, 0.4f * w);
            vel.y = new ParticleSystem.MinMaxCurve(-0.9f * w, -0.5f * w);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.speedModifier = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.5f, 0.35f), new Keyframe(1f, 0.15f)));

            var sol = exhaust.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.45f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.5f)));

            var col = exhaust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var rot = exhaust.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = ExhaustMaterial(skin);
            r.sortingOrder = 26;
        }

        static Material ExhaustMaterial(SkinConfig skin)
        {
            if (exhaustMat != null) return exhaustMat;
            var sp = skin.exhaustSprite != null ? skin.exhaustSprite : SpriteFactory.Puff();
            exhaustMat = new Material(SpriteFactory.SpriteMaterial) { name = "ExhaustPuff" };
            exhaustMat.mainTexture = sp.texture;
            return exhaustMat;
        }

        public void StopMove()
        {
            tireTrail?.Stop();
            path.Clear();
            // Reset the cursor with the path so a later command starts cleanly.
            pathIndex = 0;
            onArrive = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var skin = SkinConfig.Instance;
            float targetViewTilt = state == CarState.InLane || state == CarState.Ejecting
                ? skin.departureCarViewTilt
                : skin.carViewTilt;
            if (!viewTiltInitialized)
            {
                viewTilt = targetViewTilt;
                viewTiltInitialized = true;
            }
            else
            {
                viewTilt = Mathf.SmoothDampAngle(viewTilt, targetViewTilt, ref viewTiltVelocity,
                    Mathf.Max(0.01f, skin.carViewTiltTransitionTime), Mathf.Infinity, dt);
            }
            if (pathIndex < path.Count)
            {
                var pos = transform.localPosition;
                // Consume only the time available after the logical movement phase.
                float pathDt = Mathf.Min(dt, firstPathFrameTime);
                firstPathFrameTime = float.PositiveInfinity;
                float step = moveSpeed * pathDt;
                if (departureDuration > 0f)
                {
                    float nextElapsed = departureElapsed + pathDt;
                    step = nextElapsed >= departureDuration ? float.PositiveInfinity :
                        DepartureDistance(nextElapsed, departureRamp, moveSpeed) - DepartureDistance(departureElapsed, departureRamp, moveSpeed);
                    departureElapsed = nextElapsed;
                }
                else if (parkingAcceleration > 0f)
                {
                    float stoppingSpeed = Mathf.Sqrt(2f * parkingAcceleration * parkingDistance);
                    parkingSpeed = Mathf.Min(stoppingSpeed, Mathf.MoveTowards(parkingSpeed, moveSpeed, parkingAcceleration * pathDt));
                    step = pathDt > 0f && parkingDistance < 0.002f ? 0.003f : parkingSpeed * pathDt;
                    if (parkingLeader != null && parkingLeader.state == CarState.Entering && pathDt > 0f)
                    {
                        step = ParkingFollowStep(step, pos.y, parkingLeader.transform.localPosition.y, parkingGap);
                        parkingSpeed = Mathf.Min(parkingSpeed, step / pathDt);
                    }
                    parkingDistance = Mathf.Max(0f, parkingDistance - step);
                }
                while (step > 0 && pathIndex < path.Count)
                {
                    // Carry leftover distance across short samples instead of stopping at each point.
                    var to = path[pathIndex];
                    var delta = to - pos;
                    float dist = delta.magnitude;
                    if (dist > 1e-4f) lastMoveDir = delta / dist;
                    if (dist > 1e-4f && faceMovement)
                        targetAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
                    if (dist <= step)
                    {
                        pos = to;
                        step -= dist;
                        pathIndex++;
                    }
                    else
                    {
                        pos += delta / dist * step;
                        step = 0;
                    }
                }
                transform.localPosition = pos;
                if (pathIndex >= path.Count)
                {
                    // Release the sampled path only after arrival; Moving remains deterministic until then.
                    path.Clear();
                    pathIndex = 0;
                    tireTrail?.Stop();
                    if (brakeOnArrive) Brake();
                    var cb = onArrive;
                    onArrive = null;
                    cb?.Invoke();
                }
            }

            if (qRoot.gameObject.activeSelf) qRoot.rotation = Quaternion.identity;
            UpdateBadges();
            UpdateDirectional();
            UpdateIdle(dt);

            // 刹车前倾弹簧（固定小步长积分，掉帧时也稳定）
            float rest = Mathf.Min(dt, 0.1f);
            while (rest > 1e-5f)
            {
                float h = Mathf.Min(rest, 1f / 120f);
                pitchVel += (-pitch * 420f - pitchVel * 16f) * h;
                pitch += pitchVel * h;
                rest -= h;
            }
            // 待机：发动机怠速的轻微前后点头
            float shakePitch = 0, idleSquash = 0;
            if (IdleActive && SkinConfig.Instance.idleShake > 0)
            {
                float a = SkinConfig.Instance.idleShake;
                shakePitch = Mathf.Sin(idleT * 2f * Mathf.PI * 3.5f + 1f) * a * 0.6f;
                idleSquash = Mathf.Sin(idleT * 2f * Mathf.PI * 3.5f) * 0.012f * a;
            }

            // 平滑转向
            float cur = transform.eulerAngles.z;
            float next = Mathf.LerpAngle(cur, targetAngle, 1f - Mathf.Exp(-14f * dt));
            transform.rotation = Quaternion.Euler(0, 0, next);
            // 3D 模型绕屏幕水平轴倾斜（与车头朝向无关），模拟斜俯视相机，能看到车头/车尾侧面；
            // 再叠加车身自身的前倾（绕车的左右轴 X，正 = 车头向下）和待机点头
            if (model != null)
                model.rotation = Quaternion.AngleAxis(viewTilt, Vector3.right) * transform.rotation
                                 * Quaternion.Euler(pitch + shakePitch, 0f, 0f);
            else if (visual != null)
            {
                // 2D 车图：用沿车头方向的位移 + 纵向压缩近似前倾
                visual.localPosition = new Vector3(0, pitch * 0.004f * visualBase, 0);
            }

            // 弹跳（消除动画期间由 GameController 控制缩放）
            if (state == CarState.Clearing) { }
            else if (punch > 0.001f)
            {
                punch = Mathf.MoveTowards(punch, 0, dt * 1.6f);
                float k = 1 + Mathf.Sin(punch * 30f) * punch * 0.5f;
                visual.localScale = new Vector3(visualBase * k, visualBase / Mathf.Max(0.6f, k), visualBase);
            }
            else
            {
                float sy = 1f + idleSquash - (model == null ? Mathf.Abs(pitch) * 0.006f : 0f);
                float sx = 1f - idleSquash * 0.5f;
                var want = new Vector3(visualBase * sx, visualBase * sy, visualBase);
                if (visual.localScale != want) visual.localScale = want;
            }
        }

        float visualBase = 1f;

        public void SetScale(float width)
        {
            visualBase = width;
            visual.localScale = Vector3.one * width;
        }

        public float Width => visualBase;

        public Transform Visual => visual;

        /// <summary>刷新钥匙/锁的标记。</summary>
        public void RefreshBadges()
        {
            if (hasKey && keyIcon == null) keyIcon = Badge("Key", SpriteFactory.KeyIcon(), new Color(1f, 0.82f, 0.2f));
            if (locked && lockIcon == null) lockIcon = Badge("Lock", SpriteFactory.LockIcon(), new Color(0.85f, 0.87f, 0.92f));
            if (keyIcon != null) keyIcon.gameObject.SetActive(hasKey);
            if (lockIcon != null) lockIcon.gameObject.SetActive(locked);
            UpdateBadges();
        }

        SpriteRenderer Badge(string n, Sprite sp, Color c)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.color = c;
            sr.sortingOrder = 28;
            return sr;
        }

        void UpdateBadges()
        {
            float w = visualBase;
            if (keyIcon != null && keyIcon.gameObject.activeSelf)
            {
                keyIcon.transform.rotation = Quaternion.identity;
                keyIcon.transform.position = transform.position + new Vector3(0.28f * w, 0.3f * w, -2.6f);
                keyIcon.transform.localScale = Vector3.one * 0.55f * w;
            }
            if (lockIcon != null && lockIcon.gameObject.activeSelf)
            {
                lockIcon.transform.rotation = Quaternion.identity;
                lockIcon.transform.position = transform.position + new Vector3(0, 0, -2.6f);
                lockIcon.transform.localScale = Vector3.one * 0.8f * w;
            }
        }

        /// <summary>多方向序列帧：图不旋转，按车头朝向选帧。</summary>
        void UpdateDirectional()
        {
            var frames = SkinConfig.Instance.carDirectionalSprites;
            if (frames == null || frames.Length == 0 || body == null) return;
            float ang = Mathf.Repeat(transform.eulerAngles.z, 360f);
            int idx = Mathf.RoundToInt(ang / (360f / frames.Length)) % frames.Length;
            visual.rotation = Quaternion.identity;
            if (frames[idx] != null) body.sprite = frames[idx];
            var det = SkinConfig.Instance.carDirectionalDetails;
            if (detail != null && det != null && idx < det.Length && det[idx] != null) { detail.sprite = det[idx]; detail.enabled = true; }
            else if (detail != null && det != null && det.Length > 0) detail.enabled = false;
            if (under != null && SkinConfig.Instance.carPrefab == null) under.enabled = SkinConfig.Instance.carUnder != null;
        }
    }
}
