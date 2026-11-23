using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LoopPark.EditorTools
{
    /// <summary>验证全部车色、所有网格材质及事件驱动刷新，测试结束恢复共享资源。</summary>
    public static class CarAppearanceChecks
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [MenuItem("LoopPark/Verify Car Materials (No Play Mode Needed)")]
        public static void CheckMaterialBindings()
        {
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>("Assets/LoopPark/Resources/LoopParkSkin.asset");
            Require(config != null && config.carPrefab != null, "Missing car config or prefab");
            var instance = UnityEngine.Object.Instantiate(config.carPrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var skin = instance.GetComponent<CarSkin3D>();
            Require(skin != null, "Prefab is missing CarSkin3D");
            Material temporary = null;
            var selected = config.GetCarAppearance(0);
            var original = selected.windowMaterial;
            try
            {
                // 先走旧覆盖，再切材质，检查旧属性块不会压住新材质。
                selected.windowMaterial = null;
                skin.SetCarColor(Palette.Get(0), false, 0);
                temporary = new Material(Shader.Find(CarMaterialColors.ShaderName));
                temporary.color = Color.cyan;
                temporary.SetFloat("_Glossiness", 0.91f);
                temporary.SetColor("_EmissionColor", Color.red);
                selected.windowMaterial = temporary;
                skin.SetCarColor(Palette.Get(0), false, 0);
                CheckMaterials(skin, Palette.Get(0), selected);
                Require(temporary.color == Color.cyan && Mathf.Approximately(temporary.GetFloat("_Glossiness"), 0.91f)
                    && temporary.GetColor("_EmissionColor") == Color.red, "Art material modified at runtime");
                selected.windowMaterial = original;
                for (int i = 0; i < Palette.Count; i++)
                {
                    skin.SetCarColor(Palette.Get(i), false, i);
                    CheckMaterials(skin, Palette.Get(i), config.GetCarAppearance(i));
                }
                skin.SetCarColor(config.hiddenCarColor, true, 0);
                CheckMaterials(skin, config.hiddenCarColor, config.GetCarAppearance(-1));
                Debug.Log("[CarMaterials] PASS: 15 colors and mystery car bound, legacy overrides cleaned, independent specular and emission, depth properties preserved");
            }
            finally
            {
                selected.windowMaterial = original;
                UnityEngine.Object.DestroyImmediate(instance);
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        /// <summary>验证渲染深度而非限制 XY 轮廓，允许参考图中紧贴车辆互相遮挡。</summary>
        [MenuItem("LoopPark/Verify Car Occlusion Layers")]
        public static void CheckDepth()
        {
            Require(Application.isPlaying && GameController.I != null, "Run a game level first");
            var cars = GameController.I.GetComponentsInChildren<CarView>();
            var block = new MaterialPropertyBlock();
            int count = 0;
            foreach (var car in cars)
            {
                var skin = car.GetComponentInChildren<CarSkin3D>();
                Require(skin.GetComponent<CarLayerDepth>() != null, "Car has no whole-car depth component");
                foreach (var renderer in skin.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.GetPropertyBlock(block, 0);
                    var p = skin.transform.position;
                    Require(block.GetVector("_CarOutlineCenter") == new Vector4(p.x, p.y, 0f, 1f), "Body shadow does not follow the model center");
                    foreach (var material in renderer.sharedMaterials)
                        Require(material != null && material.FindPass("CAR_OUTLINE") >= 0, "Car material missing the outline shadow pass");
                    float expected = -2f + car.transform.position.y * 0.05f;
                    if (car.state == CarState.Parked || car.state == CarState.InLane)
                        Require(Mathf.Approximately(block.GetFloat("_CarDepth"), expected), "Car parts do not share the same position depth");
                    count++;
                }
                if (car.state == CarState.Parked || car.state == CarState.InLane)
                {
                    // 临时切到移动层后恢复，验证现有 SetLayer 确实影响 3D 车身。
                    var depth = skin.GetComponent<CarLayerDepth>();
                    var renderer = skin.GetComponentInChildren<MeshRenderer>();
                    try
                    {
                        car.SetLayer(30);
                        depth.RefreshDepth();
                        renderer.GetPropertyBlock(block, 0);
                        Require(Mathf.Approximately(block.GetFloat("_CarDepth"), -4f + car.transform.position.y * 0.05f), "Moving car does not cover the parking layer");
                    }
                    finally { car.SetLayer(20); depth.RefreshDepth(); }
                }
            }
            Require(count > 0, "No car meshes to verify");
            Debug.Log("[CarDepth] PASS: " + count + " meshes use whole-car depth; moving-car layering switches correctly; tight spacing preserved");
        }

        [MenuItem("LoopPark/Verify Car Appearance and Live Tuning")]
        public static void Run()
        {
            Require(Application.isPlaying, "Verify car lifecycle and live refresh in Play mode");
            var config = SkinConfig.Instance;
            Require(config.carColors.Length == 15 && Palette.Count == 15 && Palette.Names.Length == 15, "Car colors must be 15");
            var root = new GameObject("CarAppearanceValidation");
            var original = config.carColors[14];
            var profile = config.GetCarAppearance(14).Copy();
            var otherPaint = config.carColors[0];
            var scale = config.carModelScale;
            try
            {
                var car = CarView.Create(root.transform, 14, false, 1);
                var skin = car.GetComponentInChildren<CarSkin3D>();
                Require(skin != null && skin.appearance == config, "Prefab has no appearance config bound");
                for (int i = 0; i < Palette.Count; i++)
                {
                    car.color = i;
                    car.Refresh();
                    CheckMaterials(skin, Palette.Get(i), config.GetCarAppearance(i));
                }
                car.color = 14;
                config.carColors[14] = Color.magenta;
                // 两个编号使用相同车漆，仍必须各自读取独立的部件和光泽参数。
                config.carColors[0] = Color.magenta;
                var selected = config.GetCarAppearance(14);
                Require(!ReferenceEquals(selected, config.GetCarAppearance(0)), "Car color configs share the same instance");
                selected.carSmoothness = 0.43f;
                selected.carSpecular = 0.23f;
                selected.carWindowColor = Color.cyan;
                selected.carTrimColor = Color.green;
                selected.carWindowFrameColor = Color.black;
                selected.carFrontSplitterColor = Color.gray;
                selected.carHeadlightColor = Color.yellow;
                selected.carLightStripColor = Color.cyan;
                selected.carTaillightColor = Color.blue;
                selected.carWheelColor = Color.red;
                selected.carTireColor = Color.white;
                config.carModelScale = new Vector3(1.6f, 1.3f, 1.1f);
                config.RefreshCarAppearance();
                CheckMaterials(skin, Color.magenta, selected);
                var other = CarView.Create(root.transform, 0, false, 1);
                CheckMaterials(other.GetComponentInChildren<CarSkin3D>(), Color.magenta, config.GetCarAppearance(0));
                other.hidden = true;
                other.Refresh();
                CheckMaterials(other.GetComponentInChildren<CarSkin3D>(), config.hiddenCarColor, config.GetCarAppearance(-1));
                Require(skin.transform.localScale == new Vector3(1.6f, 1.3f, 1.1f), "Runtime scale not refreshed");
                Debug.Log("[CarAppearanceChecks] PASS: 15 colors, all material slots, independent params per color id, mystery car, event-driven refresh");
            }
            finally
            {
                // 测试中修改的是共享 SO，必须恢复，避免改变关卡中其他车辆的外观。
                config.carColors[14] = original;
                config.carAppearanceByColor[14] = profile;
                config.carColors[0] = otherPaint;
                config.carModelScale = scale;
                UnityEngine.Object.DestroyImmediate(root);
                config.RefreshCarAppearance();
            }
        }

        /// <summary>车衣覆盖层与绑带车顶附件各自管理材质，不属于车身部件。</summary>
        static bool IsManagedOverlay(Renderer renderer) =>
            renderer.GetComponentInParent<CarRevealCover>(true) != null ||
            renderer.GetComponentInParent<CarStrap>(true) != null;

        /// <summary>检查实际 Renderer 属性块，保证配置确实进入渲染流程。</summary>
        static void CheckMaterials(CarSkin3D skin, Color paint, CarColorAppearance config)
        {
            var block = new MaterialPropertyBlock();
            // 车衣与绑带附件由各自组件管理材质（Buliao.mat / Strap.mat），不参与部件材质绑定检查。
            var sources = skin.appearance.carPrefab.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !IsManagedOverlay(r)).ToArray();
            var renderers = skin.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !IsManagedOverlay(r)).ToArray();
            Require(sources.Length == renderers.Length, "Car mesh count does not match the prefab");
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                var materials = renderer.sharedMaterials;
                var original = sources[r].sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    Require(material.shader.name == "LoopPark/Layered Car" && !ShaderUtil.ShaderHasError(material.shader), "Car material shader error");
                    renderer.GetPropertyBlock(block, i);
                    var replacement = CarMaterialColors.ResolveMaterial(original[i].name, config);
                    if (replacement != null)
                    {
                        Require(material == replacement, "Part not bound to the expected material: " + original[i].name);
                        // 车漆槽（car_base/car_jingzi）仍由运行时属性块写入当前车色，因为 15 种颜色共用同一份独立材质；
                        // 其余换材质槽必须完全以材质自身参数渲染，不再被属性块覆盖。
                        bool paintSlot = original[i].name == "car_base" || original[i].name == "car_jingzi";
                        var expectedPaint = paint;
                        expectedPaint.a = 1;
                        Require(paintSlot ? block.GetColor("_Color") == expectedPaint : !block.HasColor("_Color"),
                            "Material still overridden by runtime code: " + original[i].name + " → " + material.name);
                        Require(!block.HasFloat("_Glossiness") && !block.HasColor("_SpecColor") && !block.HasColor("_EmissionColor"),
                            "Material still overridden by runtime code: " + original[i].name + " → " + material.name);
                        Require(block.HasFloat("_CarDepth"), "Car depth lost after material swap");
                        continue;
                    }
                    // 车辆使用不透明材质，颜色选择器的 Alpha 不参与车身透明度。
                    var expectedColor = CarMaterialColors.Resolve(original[i].name, paint, config);
                    expectedColor.a = 1;
                    Require(block.GetColor("_Color") == expectedColor, "Material color not refreshed: " + material.name);
                    Require(block.GetColor("_EmissionColor") == Color.black, "Emission not cleared: " + material.name);
                    Require(Mathf.Approximately(block.GetFloat("_Glossiness"), config.carSmoothness), "Gloss not refreshed: " + material.name);
                    Require(block.GetColor("_SpecColor") == Color.white * config.carSpecular, "Specular not refreshed: " + material.name);
                }
            }
            Require(CarMaterialColors.Resolve("car_black", paint, config) == config.carWindowFrameColor, "Window frame color not independent");
            Require(CarMaterialColors.Resolve("car_biantiao", paint, config) == config.carTrimColor, "Trim color not independent");
            Require(CarMaterialColors.Resolve("car_cequn", paint, config) == config.carFrontSplitterColor, "Front splitter color not independent");
            Require(CarMaterialColors.Resolve("car_chedeng", paint, config) == config.carHeadlightColor, "Headlights still orange");
            Require(CarMaterialColors.Resolve("car_dengtiao", paint, config) == config.carLightStripColor, "Light strip color not independent");
        }

        [MenuItem("LoopPark/Capture Current Game Screen")]
        public static void Capture()
        {
            Directory.CreateDirectory("Logs");
            ScreenCapture.CaptureScreenshot("Logs/car_game.png");
        }
    }
}
