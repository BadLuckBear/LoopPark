using System.IO;
using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    /// <summary>
    /// 临时验证：不进入 Play，按发车道真实布局（3 列 × 3 行、列距 1.69、行距 1.05、朝向一致）
    /// 实例化绑带车并离线渲染，检查软管对接、吸盘贴位与配色。
    /// 通过命令行 `-executeMethod LoopPark.EditorTools.StrapShot.RenderLane -quit` 调用。
    /// </summary>
    public static class StrapShot
    {
        [MenuItem("LoopPark/Probe/离线渲染发车道绑带")]
        public static void RenderLane()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LoopPark/Resources/Model/CarModel.prefab");
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>("Assets/LoopPark/Resources/LoopParkSkin.asset");
            if (prefab == null || config == null) { Debug.LogError("[Shot] prefab/config missing"); return; }

            const int cols = 3, rows = 3;
            const float colGap = 1.69f, rowGap = 1.05f;
            int[,] paletteIndex = { { 0, 7, 3 }, { 15, 12, 15 }, { 1, 2, 11 } };
            int[,] pairs = { { 0, 1 }, { -1, -1 }, { 1, 2 } };

            var preview = new PreviewRenderUtility();
            Texture2D image = null;
            var cars = new CarSkin3D[cols, rows];
            var sb = new System.Text.StringBuilder();
            try
            {
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = 3.9f;
                preview.camera.transform.position = new Vector3(0, 0, -10);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.36f, 0.37f, 0.47f);
                preview.lights[0].intensity = config.carKeyLightIntensity * 1.6f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, -30, 0);
                preview.ambientColor = config.carAmbientLight * 1.5f;

                Quaternion viewRot = Quaternion.AngleAxis(config.carViewTilt, Vector3.right);
                Vector3 scale = config.carModelScale * 0.78f;
                float x0 = -(cols - 1) * colGap * 0.5f;
                float y0 = (rows - 1) * rowGap * 0.5f;

                for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                    {
                        int idx = paletteIndex[c, r];
                        var pos = new Vector3(x0 + c * colGap, y0 - r * rowGap, 0f);
                        var go = Object.Instantiate(prefab);
                        go.transform.position = pos;
                        go.transform.rotation = viewRot;
                        go.transform.localScale = scale;
                        var skin = go.GetComponent<CarSkin3D>();
                        skin.SetCarColor(Palette.Get(idx), false, idx);
                        cars[c, r] = skin;
                        ForceDepth(go, -2.06f);
                        preview.AddSingleGO(go);
                    }

                for (int c = 0; c < cols; c++)
                {
                    int a = pairs[c, 0], b = pairs[c, 1];
                    if (a < 0 || b < 0) continue;
                    cars[c, a].SetLinked(true, cars[c, b].strap);
                    cars[c, b].SetLinked(true, cars[c, a].strap);
                    var hoseA = cars[c, a].strap.Hose;
                    var hoseB = cars[c, b].strap.Hose;
                    sb.AppendLine($"col{c} row{a}<->row{b} mid={(hoseA.position + hoseB.position) * 0.5f:F2}");
                    Describe(sb, "A", hoseA);
                    Describe(sb, "B", hoseB);
                }

                preview.BeginStaticPreview(new Rect(0, 0, 1000, 1100));
                preview.Render();
                image = preview.EndStaticPreview();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/strap_lane.png", image.EncodeToPNG());
                File.WriteAllText("Logs/strap_lane.txt", sb.ToString());
                Debug.Log("[Shot] lane rendered");
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                preview.Cleanup();
            }
        }

        /// <summary>输出软管的世界朝向、缩放与渲染包围盒，用来判断它到底伸到了哪里。</summary>
        static void Describe(System.Text.StringBuilder sb, string tag, Transform hose)
        {
            if (hose == null) { sb.AppendLine($"   {tag} 无软管节点"); return; }
            var r = hose.GetComponent<Renderer>();
            string bounds = r != null
                ? $"bounds中心={r.bounds.center:F2} 尺寸={r.bounds.size:F2}"
                : "无渲染器";
            sb.AppendLine($"   {tag} root={hose.position:F2} 轴向={hose.TransformDirection(Vector3.back):F2} " +
                          $"scale={hose.localScale:F2} {bounds}");
        }

        /// <summary>预览不走 CarLayerDepth，手动把附件压到车身之上，避免被车顶 z-fighting 吃掉。</summary>
        static void ForceDepth(GameObject car, float depth)
        {
            var block = new MaterialPropertyBlock();
            foreach (var strap in car.GetComponentsInChildren<CarStrap>(true))
                foreach (var r in strap.GetComponentsInChildren<Renderer>(true))
                    for (int slot = 0; slot < r.sharedMaterials.Length; slot++)
                    {
                        r.GetPropertyBlock(block, slot);
                        block.SetFloat("_CarDepth", depth);
                        r.SetPropertyBlock(block, slot);
                    }
        }
    }
}
