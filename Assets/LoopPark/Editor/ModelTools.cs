using System.Text;
using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    public static class ModelTools
    {
        [MenuItem("LoopPark/检查车辆模型")]
        public static void Inspect()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LoopPark/Resources/Model/car.fbx");
            if (go == null) { Debug.Log("[LoopPark] model not found"); return; }
            var sb = new StringBuilder("[LoopPark] MODEL\n");
            Dump(go.transform, 0, sb);
            var b = new Bounds();
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                sb.Append($"renderer {r.name} {r.GetType().Name} bounds c={r.bounds.center} s={r.bounds.size}\n");
                foreach (var m in r.sharedMaterials)
                    sb.Append($"   mat {(m ? m.name : "null")} shader={(m ? m.shader.name : "-")} color={(m && m.HasProperty("_Color") ? m.color.ToString() : "-")} tex={(m && m.mainTexture ? m.mainTexture.name : "-")}\n");
            }
            sb.Append($"total bounds c={b.center} s={b.size}\n");
            var imp = AssetImporter.GetAtPath("Assets/LoopPark/Resources/Model/car.fbx") as ModelImporter;
            if (imp != null) sb.Append($"import scale={imp.globalScale} useFileScale={imp.useFileScale} materialImportMode={imp.materialImportMode} anims={imp.importAnimation}\n");
            foreach (var clip in AnimationUtility.GetAnimationClips(go)) sb.Append($"clip {clip.name} len={clip.length}\n");
            Debug.Log(sb.ToString());
        }

        static void Dump(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(new string(' ', depth * 2)).Append(t.name)
              .Append($" pos={t.localPosition} rot={t.localEulerAngles} scale={t.localScale}");
            foreach (var c in t.GetComponents<Component>()) if (!(c is Transform)) sb.Append(" [").Append(c.GetType().Name).Append(']');
            sb.Append('\n');
            foreach (Transform ch in t) Dump(ch, depth + 1, sb);
        }

        public static void InspectThenGenerate()
        {
            Inspect();
            ProjectSetup.GenerateFromArgs();
        }
    }
}
