using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    /// <summary>程序生成占位美术（圆角矩形、小车、圆形等）。美术资源到位后由 SkinConfig 覆盖。</summary>
    public static class SpriteFactory
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Material spriteMat;
        static Font font;

        public static Material SpriteMaterial
        {
            get
            {
                if (spriteMat == null)
                {
                    var go = new GameObject("tmp");
                    var sr = go.AddComponent<SpriteRenderer>();
                    spriteMat = sr.sharedMaterial;
                    Object.DestroyImmediate(go);
                }
                return spriteMat;
            }
        }

        public static Font Font
        {
            get
            {
                if (font == null)
                {
                    font = SkinConfig.Instance.uiFont;
                    if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                return font;
            }
        }

        // ---------- SDF 工具 ----------
        static float RoundBox(Vector2 p, Vector2 center, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - (half - new Vector2(r, r));
            var outside = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
        }

        static float Cov(float d) => Mathf.Clamp01(0.5f - d);

        static Color Over(Color dst, Color src, float a)
        {
            a *= src.a;
            float outA = a + dst.a * (1 - a);
            if (outA < 1e-5f) return new Color(0, 0, 0, 0);
            var c = (src * a + dst * dst.a * (1 - a)) / outA;
            c.a = outA;
            return c;
        }

        static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            return t;
        }

        static Sprite Make(string key, Texture2D tex, float ppu, Vector4 border = default)
        {
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect, border);
            s.name = key;
            cache[key] = s;
            return s;
        }

        /// <summary>白色圆角矩形，可 9 宫格拉伸（UI 用）。</summary>
        public static Sprite RoundedRect(int size = 64, int radius = 24, int borderPx = 0)
        {
            string key = $"rr{size}_{radius}_{borderPx}";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            var c = new Vector2(size / 2f, size / 2f);
            var half = new Vector2(size / 2f, size / 2f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = RoundBox(p, c, half, radius);
                float a = Cov(d);
                var col = new Color(1, 1, 1, a);
                if (borderPx > 0)
                {
                    // 下方阴影边：底部 borderPx 像素变暗，做出“按钮厚度”
                    float inner = RoundBox(p + new Vector2(0, -borderPx), c, half, radius);
                    float t = Cov(inner);
                    col = Color.Lerp(new Color(0.75f, 0.75f, 0.75f, a), new Color(1, 1, 1, a), t);
                }
                px[y * size + x] = col;
            }
            tex.SetPixels(px);
            float b = radius + 2;
            return Make(key, tex, 100, new Vector4(b, b + borderPx, b, b));
        }

        /// <summary>横向胶囊（两端半圆），左右 9 宫格边框 = 半圆，中间有可拉伸的直段（绑带用）。</summary>
        public static Sprite Capsule()
        {
            const string key = "capsule";
            if (cache.TryGetValue(key, out var s)) return s;
            int w = 96, h = 64;
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            var c = new Vector2(w / 2f, h / 2f);
            var half = new Vector2(w / 2f, h / 2f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = RoundBox(new Vector2(x + 0.5f, y + 0.5f), c, half, h / 2f);
                px[y * w + x] = new Color(1, 1, 1, Cov(d));
            }
            tex.SetPixels(px);
            return Make(key, tex, 100, new Vector4(33, 0, 33, 0));
        }

        public static Sprite Circle(int size = 64)
        {
            string key = "circle" + size;
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude - (r - 1);
                px[y * size + x] = new Color(1, 1, 1, Cov(d));
            }
            tex.SetPixels(px);
            return Make(key, tex, size);
        }

        /// <summary>尾气烟团：几个圆叠成的卡通云朵，下半部略灰，边缘柔和。</summary>
        public static Sprite Puff()
        {
            const string key = "puff";
            if (cache.TryGetValue(key, out var s)) return s;
            const int size = 64;
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            // (中心x, 中心y, 半径)，单位为像素
            Vector3[] blobs = { new Vector3(26, 28, 15), new Vector3(40, 30, 13), new Vector3(33, 40, 13), new Vector3(22, 38, 10), new Vector3(42, 22, 9) };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = float.MaxValue;
                foreach (var b in blobs) d = Mathf.Min(d, (p - new Vector2(b.x, b.y)).magnitude - b.z);
                float a = Mathf.Clamp01(0.5f - d / 2.5f);   // 2.5px 柔边
                float shade = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01((y - 14f) / 30f));
                px[y * size + x] = new Color(shade, shade, Mathf.Min(1f, shade + 0.04f), a);
            }
            tex.SetPixels(px);
            return Make(key, tex, size);
        }

        /// <summary>1x1 单位的白色方块（世界空间 1 单位）。</summary>
        public static Sprite Square()
        {
            const string key = "square";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(4, 4);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Point;
            return Make(key, tex, 4);
        }

        /// <summary>黄黑警示条纹（栅栏）。</summary>
        public static Sprite Hazard()
        {
            const string key = "hazard";
            if (cache.TryGetValue(key, out var s)) return s;
            int w = 128, h = 32;
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            var cc = new Vector2(w / 2f, h / 2f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = Cov(RoundBox(new Vector2(x + .5f, y + .5f), cc, cc, 6));
                bool stripe = ((x + y) / 14) % 2 == 0;
                var col = stripe ? new Color(0.98f, 0.80f, 0.15f) : new Color(0.15f, 0.15f, 0.18f);
                col.a = a;
                px[y * w + x] = col;
            }
            tex.SetPixels(px);
            return Make(key, tex, w);
        }

        // ---------- 小车 ----------
        // 贴图 96x120，车头朝上，PPU=96 → 世界宽 1 单位，高 1.25 单位
        const int CW = 96, CH = 120;

        public static Sprite CarBody()
        {
            if (SkinConfig.Instance.carBody != null) return SkinConfig.Instance.carBody;
            const string key = "carBody";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(CW, CH);
            var px = new Color[CW * CH];
            var c = new Vector2(CW / 2f, CH / 2f);
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                float d = RoundBox(p, c, new Vector2(40, 54), 24);
                float a = Cov(d);
                // 边缘稍暗，做出体积感（染色后仍保留明暗）
                float shade = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(-d / 9f));
                // 上方受光
                shade *= Mathf.Lerp(0.92f, 1.04f, y / (float)CH);
                px[y * CW + x] = new Color(shade, shade, shade, a);
            }
            tex.SetPixels(px);
            return Make(key, tex, CW);
        }

        public static Sprite CarUnder()
        {
            if (SkinConfig.Instance.carUnder != null) return SkinConfig.Instance.carUnder;
            const string key = "carUnder";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(CW, CH);
            var px = new Color[CW * CH];
            var c = new Vector2(CW / 2f, CH / 2f);
            var wheel = new Color(0.12f, 0.12f, 0.15f, 1);
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                var col = new Color(0, 0, 0, 0);
                // 阴影
                float sh = RoundBox(p, c + new Vector2(0, -4), new Vector2(43, 56), 26);
                col = Over(col, new Color(0, 0, 0, 0.28f), Mathf.Clamp01(0.5f - sh / 5f));
                // 四个轮子
                foreach (var wp in new[] { new Vector2(-38, 30), new Vector2(38, 30), new Vector2(-38, -30), new Vector2(38, -30) })
                    col = Over(col, wheel, Cov(RoundBox(p, c + wp, new Vector2(8, 14), 5)));
                px[y * CW + x] = col;
            }
            tex.SetPixels(px);
            return Make(key, tex, CW);
        }

        /// <summary>钥匙图标（白色，可染色）。</summary>
        public static Sprite KeyIcon()
        {
            const string key = "keyIcon";
            if (cache.TryGetValue(key, out var s)) return s;
            int w = 64;
            var tex = NewTex(w, w);
            var px = new Color[w * w];
            var outline = new Color(0.35f, 0.22f, 0.02f, 1);
            for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                float ring = Mathf.Abs((p - new Vector2(18, 32)).magnitude - 10) - 4;
                float shaft = RoundBox(p, new Vector2(42, 32), new Vector2(16, 3.5f), 1.5f);
                float t1 = RoundBox(p, new Vector2(50, 25), new Vector2(2.5f, 5), 1);
                float t2 = RoundBox(p, new Vector2(57, 25), new Vector2(2.5f, 5), 1);
                float d = Mathf.Min(Mathf.Min(ring, shaft), Mathf.Min(t1, t2));
                var col = new Color(0, 0, 0, 0);
                col = Over(col, outline, Cov(d - 2.5f));
                col = Over(col, Color.white, Cov(d));
                px[y * w + x] = col;
            }
            tex.SetPixels(px);
            return Make(key, tex, w);
        }

        /// <summary>挂锁图标（白色，可染色）。</summary>
        public static Sprite LockIcon()
        {
            const string key = "lockIcon";
            if (cache.TryGetValue(key, out var s)) return s;
            int w = 64;
            var tex = NewTex(w, w);
            var px = new Color[w * w];
            var outline = new Color(0.15f, 0.15f, 0.2f, 1);
            for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                float body = RoundBox(p, new Vector2(32, 22), new Vector2(20, 16), 5);
                float arc = Mathf.Abs((p - new Vector2(32, 38)).magnitude - 12) - 3.5f;
                if (p.y < 36) arc = 99;
                float hole = RoundBox(p, new Vector2(32, 21), new Vector2(3, 6), 2);
                float d = Mathf.Min(body, arc);
                var col = new Color(0, 0, 0, 0);
                col = Over(col, outline, Cov(d - 2.5f));
                col = Over(col, Color.white, Cov(d));
                col = Over(col, outline, Cov(hole));
                px[y * w + x] = col;
            }
            tex.SetPixels(px);
            return Make(key, tex, w);
        }

        public static Sprite CarDetail()
        {
            if (SkinConfig.Instance.carDetail != null) return SkinConfig.Instance.carDetail;
            const string key = "carDetail";
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(CW, CH);
            var px = new Color[CW * CH];
            var c = new Vector2(CW / 2f, CH / 2f);
            var glass = new Color(0.16f, 0.22f, 0.34f, 0.88f);
            for (int y = 0; y < CH; y++)
            for (int x = 0; x < CW; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                var col = new Color(0, 0, 0, 0);
                // 前挡风玻璃、后窗
                col = Over(col, glass, Cov(RoundBox(p, c + new Vector2(0, 20), new Vector2(29, 10), 7)));
                col = Over(col, glass, Cov(RoundBox(p, c + new Vector2(0, -32), new Vector2(26, 7), 5)));
                // 车顶高光
                col = Over(col, new Color(1, 1, 1, 0.22f), Cov(RoundBox(p, c + new Vector2(0, -6), new Vector2(26, 15), 10)));
                // 挡风玻璃反光
                col = Over(col, new Color(1, 1, 1, 0.35f), Cov(RoundBox(p, c + new Vector2(-12, 22), new Vector2(6, 4), 3)));
                // 车灯
                col = Over(col, new Color(1f, 0.97f, 0.75f, 1), Cov(new Vector2(p.x - c.x - 24, p.y - c.y - 48).magnitude - 6));
                col = Over(col, new Color(1f, 0.97f, 0.75f, 1), Cov(new Vector2(p.x - c.x + 24, p.y - c.y - 48).magnitude - 6));
                // 尾灯
                col = Over(col, new Color(0.9f, 0.15f, 0.15f, 0.9f), Cov(RoundBox(p, c + new Vector2(-25, -52), new Vector2(7, 3), 2)));
                col = Over(col, new Color(0.9f, 0.15f, 0.15f, 0.9f), Cov(RoundBox(p, c + new Vector2(25, -52), new Vector2(7, 3), 2)));
                px[y * CW + x] = col;
            }
            tex.SetPixels(px);
            return Make(key, tex, CW);
        }

        /// <summary>手指提示（圆点 + 圈），新手引导用。</summary>
        public static Sprite Ring(int size = 128, float thickness = 10)
        {
            string key = "ring" + size + "_" + thickness;
            if (cache.TryGetValue(key, out var s)) return s;
            var tex = NewTex(size, size);
            var px = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Abs(new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude - (r - thickness)) - thickness / 2f;
                px[y * size + x] = new Color(1, 1, 1, Cov(d));
            }
            tex.SetPixels(px);
            return Make(key, tex, size);
        }

        /// <summary>手指图标：圆形指尖 + 手掌，朝上。</summary>
        public static Sprite Hand()
        {
            const string key = "hand";
            if (cache.TryGetValue(key, out var s)) return s;
            int w = 96, h = 128;
            var tex = NewTex(w, h);
            var px = new Color[w * h];
            var outline = new Color(0.2f, 0.2f, 0.25f, 1);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                float finger = RoundBox(p, new Vector2(40, 88), new Vector2(12, 36), 12);
                float palm = RoundBox(p, new Vector2(52, 38), new Vector2(34, 32), 18);
                float d = Mathf.Min(finger, palm);
                var col = new Color(0, 0, 0, 0);
                col = Over(col, outline, Cov(d - 4));
                col = Over(col, Color.white, Cov(d));
                px[y * w + x] = col;
            }
            tex.SetPixels(px);
            return Make(key, tex, w);
        }
    }

    /// <summary>圆角矩形网格（赛道、停车场底板），使用顶点色。</summary>
    public static class ShapeMesh
    {
        public static GameObject RoundedRect(string name, Transform parent, Vector2 center, Vector2 half, float radius, Color color, int order, int seg = 10)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SpriteFactory.SpriteMaterial;
            mr.sortingOrder = order;
            mf.sharedMesh = BuildRoundedRect(center, half, radius, color, seg);
            return go;
        }

        public static Mesh BuildRoundedRect(Vector2 center, Vector2 half, float radius, Color color, int seg)
        {
            radius = Mathf.Clamp(radius, 0.001f, Mathf.Min(half.x, half.y));
            var verts = new List<Vector3> { center };
            var corners = new[]
            {
                new Vector2(half.x - radius, half.y - radius),
                new Vector2(-half.x + radius, half.y - radius),
                new Vector2(-half.x + radius, -half.y + radius),
                new Vector2(half.x - radius, -half.y + radius),
            };
            for (int k = 0; k < 4; k++)
            {
                for (int i = 0; i <= seg; i++)
                {
                    float ang = (k * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                    verts.Add(center + corners[k] + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius);
                }
            }
            var tris = new List<int>();
            int n = verts.Count - 1;
            for (int i = 1; i <= n; i++)
            {
                tris.Add(0);
                tris.Add(i == n ? 1 : i + 1);
                tris.Add(i);
            }
            var cols = new Color[verts.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = color;
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.colors = cols;
            m.RecalculateBounds();
            return m;
        }
    }
}
