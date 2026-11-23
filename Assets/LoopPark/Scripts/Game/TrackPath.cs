using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 环路中心线：圆角矩形，逆时针行驶。s=0 在下边中点，先向右 → 右侧向上 → 上边向左 → 左侧向下 → 回到下边。
    /// </summary>
    public class TrackPath
    {
        public readonly Vector2 center;
        public readonly float a, b, r; // 半宽、半高、圆角
        readonly float[] segLen = new float[9];
        public readonly float Length;

        public TrackPath(Vector2 center, float halfW, float halfH, float radius)
        {
            this.center = center;
            a = halfW;
            b = halfH;
            r = Mathf.Min(radius, Mathf.Min(a, b) - 0.01f);
            float arc = Mathf.PI * r / 2f;
            segLen[0] = a - r;
            segLen[1] = arc;
            segLen[2] = 2 * (b - r);
            segLen[3] = arc;
            segLen[4] = 2 * (a - r);
            segLen[5] = arc;
            segLen[6] = 2 * (b - r);
            segLen[7] = arc;
            segLen[8] = a - r;
            foreach (var l in segLen) Length += l;
        }

        public float Wrap(float s)
        {
            s %= Length;
            if (s < 0) s += Length;
            return s;
        }

        /// <summary>返回位置与前进方向。</summary>
        public Vector2 Eval(float s, out Vector2 dir)
        {
            s = Wrap(s);
            float cx = center.x, cy = center.y;
            int seg = 0;
            while (seg < 8 && s > segLen[seg]) { s -= segLen[seg]; seg++; }
            switch (seg)
            {
                case 0: dir = Vector2.right; return new Vector2(cx + s, cy - b);
                case 1: return Arc(new Vector2(cx + a - r, cy - b + r), -90f, s, out dir);
                case 2: dir = Vector2.up; return new Vector2(cx + a, cy - b + r + s);
                case 3: return Arc(new Vector2(cx + a - r, cy + b - r), 0f, s, out dir);
                case 4: dir = Vector2.left; return new Vector2(cx + a - r - s, cy + b);
                case 5: return Arc(new Vector2(cx - a + r, cy + b - r), 90f, s, out dir);
                case 6: dir = Vector2.down; return new Vector2(cx - a, cy + b - r - s);
                case 7: return Arc(new Vector2(cx - a + r, cy - b + r), 180f, s, out dir);
                default: dir = Vector2.right; return new Vector2(cx - a + r + s, cy - b);
            }
        }

        Vector2 Arc(Vector2 c, float startDeg, float s, out Vector2 dir)
        {
            float ang = (startDeg + s / r * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            dir = new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang));
            return c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
        }

        /// <summary>上边（向左行驶）某个 x 处的 s。</summary>
        public float SAtTop(float x)
        {
            float s = segLen[0] + segLen[1] + segLen[2] + segLen[3];
            return s + (center.x + a - r - x);
        }

        public Vector3[] ParkingPath(float fromS, Vector3 slot, float turnDepth)
        {
            var start = Eval(fromS, out var tangent);
            var end = new Vector2(slot.x, TopY - turnDepth);
            var p1 = start + tangent * ((start.x - end.x) * 0.55f);
            var p2 = end + Vector2.up * (turnDepth * 0.55f);
            // More samples keep the cubic parking curve visually smooth at the car's speed.
            const int samples = 24;
            var points = new Vector3[samples + 1];
            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples, u = 1f - t;
                points[i - 1] = u * u * u * start + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * end;
            }
            points[samples] = slot;
            return points;
        }

        /// <summary>下边（向右行驶）某个 x 处的 s。</summary>
        public float SAtBottom(float x) => Wrap(x - center.x);

        public float DeparturePath(Vector3 start, float endS, Vector3[] points)
        {
            Vector3 end = Eval(endS, out var tangent);
            var p1 = start + Vector3.up * ((end.y - start.y) * 0.55f);
            // Ejection has little sideways room at the rightmost slot; give the final tangent more weight.
            float tangentWeight = end.y < start.y ? 0.9f : 0.6f;
            var p2 = end - (Vector3)tangent * ((end.x - start.x) * tangentWeight);
            float distance = 0f;
            var previous = start;
            for (int i = 0; i < points.Length; i++)
            {
                float t = (i + 1f) / points.Length, u = 1f - t;
                var point = u * u * u * start + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * end;
                points[i] = point;
                distance += Vector3.Distance(previous, point);
                previous = point;
            }
            return distance;
        }

        public float TopY => center.y + b;
        public float BottomY => center.y - b;

        /// <summary>沿行驶方向从 from 到 to 的距离（0..Length）。</summary>
        public float Forward(float from, float to) => Wrap(to - from);
    }
}
