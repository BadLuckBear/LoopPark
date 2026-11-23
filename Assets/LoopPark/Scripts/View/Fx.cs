using UnityEngine;

namespace LoopPark
{
    /// <summary>简单粒子：方块碎片飞散 + 渐隐。</summary>
    public class FxParticle : MonoBehaviour
    {
        Vector3 vel;
        float life, maxLife, spin;
        SpriteRenderer sr;
        Color baseColor;

        public static void Burst(Transform parent, Vector3 pos, Color color, int count, float size, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("fx");
                go.transform.SetParent(parent, false);
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * size * Random.Range(0.6f, 1.2f);
                go.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0, 360f));
                var p = go.AddComponent<FxParticle>();
                p.sr = go.AddComponent<SpriteRenderer>();
                p.sr.sprite = Random.value < 0.5f ? SpriteFactory.Square() : SpriteFactory.Circle();
                p.sr.sortingOrder = 60;
                p.baseColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.4f));
                p.sr.color = p.baseColor;
                float ang = Random.Range(0, Mathf.PI * 2);
                p.vel = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang)) * speed * Random.Range(0.4f, 1.1f);
                p.maxLife = p.life = Random.Range(0.45f, 0.8f);
                p.spin = Random.Range(-540f, 540f);
            }
        }

        public static void Flash(Transform parent, Vector3 pos, Vector2 size, Color color, float duration)
        {
            var go = new GameObject("flash");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var p = go.AddComponent<FxParticle>();
            p.sr = go.AddComponent<SpriteRenderer>();
            p.sr.sprite = SpriteFactory.Square();
            p.sr.sortingOrder = 55;
            p.baseColor = color;
            p.sr.color = color;
            p.maxLife = p.life = duration;
        }

        public static void WheelSmoke(Transform parent, CarView car)
        {
            float w = car.Width;
            for (int side = -1; side <= 1; side += 2)
            {
                var pos = car.transform.TransformPoint(new Vector3(side * 0.32f * w, -0.48f * w, 0));
                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject("WheelSmoke");
                    go.transform.SetParent(parent, false);
                    go.transform.position = pos + new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(-0.06f, 0.06f), -1f) * w;
                    go.transform.localScale = Vector3.one * w * Random.Range(0.20f, 0.32f);
                    var p = go.AddComponent<FxParticle>();
                    p.sr = go.AddComponent<SpriteRenderer>();
                    p.sr.sprite = SpriteFactory.Puff();
                    p.sr.sortingOrder = 29;
                    p.baseColor = new Color(0.85f, 0.86f, 0.9f, 0.75f);
                    p.sr.color = p.baseColor;
                    p.vel = car.transform.TransformDirection(new Vector3(side * Random.Range(0.15f, 0.35f), -Random.Range(0.1f, 0.3f))) * w;
                    p.maxLife = p.life = Random.Range(0.35f, 1f);
                }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0) { Destroy(gameObject); return; }
            vel *= Mathf.Exp(-2.5f * dt);
            transform.position += vel * dt;
            transform.Rotate(0, 0, spin * dt);
            var c = baseColor;
            c.a = baseColor.a * Mathf.Clamp01(life / maxLife * 1.5f);
            sr.color = c;
        }
    }

    /// <summary>记录移动中两只后轮的轨迹，停车后原地淡出。</summary>
    public class TireTrail : MonoBehaviour
    {
        CarView car;
        readonly LineRenderer[] lines = new LineRenderer[2];
        readonly GameObject[] vestigeObjects = new GameObject[2];
        readonly TrailRenderer[] vestigeTrails = new TrailRenderer[2];
        readonly Vector3[] last = new Vector3[2];
        float width;
        float fade = 2f;
        bool finished;
        bool usingVestige;

        public static TireTrail Begin(CarView car, Transform parent)
        {
            Debug.Assert(car != null && parent != null && car.Moving && car.Width > 0f);
            var go = new GameObject("TireTrail");
            go.transform.SetParent(parent, false);
            var trail = go.AddComponent<TireTrail>();
            trail.car = car;
            trail.width = car.Width;
            var vestigeFx = SkinConfig.Instance.tireTrailFx;
            if (vestigeFx != null && vestigeFx.GetComponentInChildren<TrailRenderer>(true) != null)
            {
                // Use two authored trails so each rear wheel keeps its own contact path.
                trail.usingVestige = true;
                for (int i = 0; i < 2; i++)
                {
                    var effect = Instantiate(vestigeFx, go.transform);
                    effect.name = i == 0 ? "Vestige_Left" : "Vestige_Right";
                    trail.vestigeObjects[i] = effect;
                    trail.vestigeTrails[i] = effect.GetComponentInChildren<TrailRenderer>(true);
                    trail.SetVestigePosition(i, trail.WheelPosition(i));
                    trail.vestigeTrails[i].emitting = true;
                }
                return trail;
            }

            for (int i = 0; i < 2; i++)
            {
                var wheel = new GameObject(i == 0 ? "Left" : "Right");
                wheel.transform.SetParent(go.transform, false);
                var line = wheel.AddComponent<LineRenderer>();
                line.sharedMaterial = SpriteFactory.SpriteMaterial;
                line.useWorldSpace = true;
                line.widthMultiplier = 0.10f * trail.width;
                line.numCornerVertices = 2;
                line.numCapVertices = 2;
                line.sortingOrder = 10;
                trail.lines[i] = line;
                trail.last[i] = trail.WheelPosition(i);
                line.positionCount = 1;
                line.SetPosition(0, trail.last[i]);
            }
            trail.SetAlpha(0.32f);
            return trail;
        }

        public void Stop()
        {
            if (finished) return;
            if (usingVestige)
            {
                foreach (var trail in vestigeTrails)
                    if (trail != null) trail.emitting = false;
                fade = 0f;
                foreach (var trail in vestigeTrails)
                    if (trail != null) fade = Mathf.Max(fade, trail.time);
                fade += 0.05f;
                finished = true;
                return;
            }

            if (car != null)
                for (int i = 0; i < 2; i++)
                {
                    lines[i].positionCount++;
                    lines[i].SetPosition(lines[i].positionCount - 1, WheelPosition(i));
                }
            finished = true;
        }

        Vector3 WheelPosition(int side)
        {
            var p = car.transform.TransformPoint(new Vector3((side == 0 ? -0.32f : 0.32f) * width, -0.48f * width));
            p.z = -0.1f;
            return p;
        }

        void LateUpdate()
        {
            if (!finished && car != null)
            {
                if (usingVestige)
                {
                    for (int i = 0; i < 2; i++) SetVestigePosition(i, WheelPosition(i));
                    if (!car.Moving) Stop();
                }
                else
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var p = WheelPosition(i);
                        if ((p - last[i]).sqrMagnitude < width * width * 0.0036f && car.Moving) continue;
                        lines[i].positionCount++;
                        lines[i].SetPosition(lines[i].positionCount - 1, p);
                        last[i] = p;
                    }
                    if (!car.Moving) Stop();
                }
            }
            else finished = true;

            if (!finished) return;
            fade -= Time.deltaTime;
            if (fade <= 0f) { Destroy(gameObject); return; }
            if (!usingVestige) SetAlpha(0.32f * fade / 2f);
        }

        void SetAlpha(float alpha)
        {
            var color = new Color(0.08f, 0.09f, 0.13f, alpha);
            foreach (var line in lines) line.startColor = line.endColor = color;
        }

        void SetVestigePosition(int side, Vector3 position)
        {
            if (vestigeObjects[side] != null) vestigeObjects[side].transform.position = position;
        }
    }
}
