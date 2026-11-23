using UnityEngine;

namespace LoopPark
{
    internal static class BoardArtwork
    {
        internal static readonly Color32 Background = new Color32(124, 142, 184, 255);

        public static SpriteRenderer Piece(Transform parent, string name, string art, Vector2 center, Vector2 size, int order)
        {
            var sprite = UIKit.Art(art);
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            go.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1);
            return renderer;
        }

        public static void Ground(Transform parent, string name, string art, Vector2 center, Vector2 half, float radius, int order)
        {
            var go = ShapeMesh.RoundedRect(name, parent, center, half, radius, Color.white, order, 24);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var vertices = mesh.vertices;
            var uv = new Vector2[vertices.Length];
            var start = center - half;
            for (int i = 0; i < uv.Length; i++) uv[i] = ((Vector2)vertices[i] - start) / (2 * half);
            mesh.uv = uv;
            Texture(go, art);
        }

        public static Mesh Curb(Transform parent, string name, TrackPath path, float inset, float depth,
            float start, float end, int order, bool inner = false)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt((end - start) / 0.05f));
            var vertices = new Vector3[2 * (count + 1)];
            var uv = new Vector2[vertices.Length];
            var colors = new Color[vertices.Length];
            var triangles = new int[6 * count];
            for (int i = 0; i <= count; i++)
            {
                float distance = (end - start) * i / count;
                var point = path.Eval(start + distance, out var direction);
                var normal = new Vector2(direction.y, -direction.x);
                float t = inner ? -1 : 1;
                // Round the exposed ends where the curb opens into the queue and the lot.
                float cap = Mathf.Min(1f, Mathf.Min(i, count - i) * 0.4f + 0.25f);
                int v = 2 * i;
                float halfWidth = t * depth * cap * .5f;
                vertices[v] = point + normal * (inset - halfWidth);
                vertices[v + 1] = point + normal * (inset + halfWidth);
                float textureU = distance / 2.4f;
                uv[v] = new Vector2(textureU, 0);
                uv[v + 1] = new Vector2(textureU, 1);
                colors[v] = colors[v + 1] = Color.white;
                if (i == count) continue;
                int j = 6 * i;
                triangles[j] = v;
                triangles[j + 1] = v + (inner ? 1 : 2);
                triangles[j + 2] = v + (inner ? 2 : 1);
                triangles[j + 3] = v + 1;
                triangles[j + 4] = v + (inner ? 3 : 2);
                triangles[j + 5] = v + (inner ? 2 : 3);
            }

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { vertices = vertices, uv = uv, colors = colors, triangles = triangles };
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sortingOrder = order;
            Texture(go, "road_curb");
            return mesh;
        }

        static void Texture(GameObject go, string art)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = SpriteFactory.SpriteMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_MainTex", UIKit.Art(art).texture);
            renderer.SetPropertyBlock(properties);
        }

    }
}
