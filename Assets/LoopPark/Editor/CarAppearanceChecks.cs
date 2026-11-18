using System;
using System.IO;
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

        [MenuItem("LoopPark/验证独立车辆材质（无需运行）")]
        public static void CheckMaterialBindings()
        {
            var config = AssetDatabase.LoadAssetAtPath<SkinConfig>("Assets/LoopPark/Resources/LoopParkSkin.asset");
            Require(config != null && config.carPrefab != null, "缺少车辆配置或预制体");
            var instance = UnityEngine.Object.Instantiate(config.carPrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var skin = instance.GetComponent<CarSkin3D>();
            Require(skin != null, "预制体缺少 CarSkin3D");
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
                    && temporary.GetColor("_EmissionColor") == Color.red, "运行时修改了美术材质");
                selected.windowMaterial = original;
                for (int i = 0; i < Palette.Count; i++)
                {
                    skin.SetCarColor(Palette.Get(i), false, i);
                    CheckMaterials(skin, Palette.Get(i), config.GetCarAppearance(i));
                }
                skin.SetCarColor(config.hiddenCarColor, true, 0);
                CheckMaterials(skin, config.hiddenCarColor, config.GetCarAppearance(-1));
                Debug.Log("[CarMaterials] PASS: 15 色与问号车材质绑定、旧覆盖清理、独立高光及发光、深度属性保留");
            }
            finally
            {
                selected.windowMaterial = original;
                UnityEngine.Object.DestroyImmediate(instance);
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        /// <summary>验证渲染深度而非限制 XY 轮廓，允许参考图中紧贴车辆互相遮挡。</summary>
        [MenuItem("LoopPark/验证车辆遮挡层级")]
        public static void CheckDepth()
        {
            Require(Application.isPlaying && GameController.I != null, "请先运行游戏关卡");
            var cars = GameController.I.GetComponentsInChildren<CarView>();
            var block = new MaterialPropertyBlock();
            int count = 0;
            foreach (var car in cars)
            {
                var skin = car.GetComponentInChildren<CarSkin3D>();
                Require(skin.GetComponent<CarLayerDepth>() != null, "车辆没有整车深度组件");
                foreach (var renderer in skin.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.GetPropertyBlock(block, 0);
                    float expected = -2f + car.transform.position.y * 0.05f;
                    if (car.state == CarState.Parked || car.state == CarState.InLane)
                        Require(Mathf.Approximately(block.GetFloat("_CarDepth"), expected), "车辆部件未使用同一位置深度");
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
                        Require(Mathf.Approximately(block.GetFloat("_CarDepth"), -4f + car.transform.position.y * 0.05f), "移动车没有覆盖停车层");
                    }
                    finally { car.SetLayer(20); depth.RefreshDepth(); }
                }
            }
            Require(count > 0, "没有可验证的车辆网格");
            Debug.Log("[CarDepth] PASS: " + count + " 个网格使用整车遮挡深度，移动车层级切换正常，保留紧贴车距");
        }

        [MenuItem("LoopPark/验证车辆外观与实时调节")]
        public static void Run()
        {
            Require(Application.isPlaying, "请在播放模式验证车辆生命周期与实时刷新");
            var config = SkinConfig.Instance;
            Require(config.carColors.Length == 15 && Palette.Count == 15 && Palette.Names.Length == 15, "车辆颜色必须为 15 种");
            var root = new GameObject("CarAppearanceValidation");
            var original = config.carColors[14];
            var profile = config.GetCarAppearance(14).Copy();
            var otherPaint = config.carColors[0];
            var scale = config.carModelScale;
            try
            {
                var car = CarView.Create(root.transform, 14, false, 1);
                var skin = car.GetComponentInChildren<CarSkin3D>();
                Require(skin != null && skin.appearance == config, "预制体没有绑定外观配置");
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
                Require(!ReferenceEquals(selected, config.GetCarAppearance(0)), "车色配置共享同一实例");
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
                config.carModelScale = new Vector2(1.6f, 1.3f);
                config.RefreshCarAppearance();
                CheckMaterials(skin, Color.magenta, selected);
                var other = CarView.Create(root.transform, 0, false, 1);
                CheckMaterials(other.GetComponentInChildren<CarSkin3D>(), Color.magenta, config.GetCarAppearance(0));
                other.hidden = true;
                other.Refresh();
                CheckMaterials(other.GetComponentInChildren<CarSkin3D>(), config.hiddenCarColor, config.GetCarAppearance(-1));
                Require(skin.transform.localScale == new Vector3(1.6f, 1.3f, 1), "运行时缩放没有刷新");
                Debug.Log("[CarAppearanceChecks] PASS: 15 色、全部材质槽、同漆色不同编号独立参数、问号车、事件驱动刷新");
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

        /// <summary>检查实际 Renderer 属性块，保证配置确实进入渲染流程。</summary>
        static void CheckMaterials(CarSkin3D skin, Color paint, CarColorAppearance config)
        {
            var block = new MaterialPropertyBlock();
            var sources = skin.appearance.carPrefab.GetComponentsInChildren<MeshRenderer>(true);
            var renderers = skin.GetComponentsInChildren<MeshRenderer>(true);
            Require(sources.Length == renderers.Length, "车辆网格数量与预制体不一致");
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                var materials = renderer.sharedMaterials;
                var original = sources[r].sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    Require(material.shader.name == "LoopPark/Layered Car" && !ShaderUtil.ShaderHasError(material.shader), "车辆材质 Shader 错误");
                    renderer.GetPropertyBlock(block, i);
                    var replacement = CarMaterialColors.ResolveMaterial(original[i].name, config);
                    if (replacement != null)
                    {
                        Require(material == replacement, "部件没有绑定指定材质: " + original[i].name);
                        Require(!block.HasColor("_Color") && !block.HasFloat("_Glossiness") && !block.HasColor("_SpecColor") && !block.HasColor("_EmissionColor"), "材质仍被运行时代码覆盖");
                        Require(block.HasFloat("_CarDepth"), "换材质后丢失车辆深度");
                        continue;
                    }
                    // 车辆使用不透明材质，颜色选择器的 Alpha 不参与车身透明度。
                    var expectedColor = CarMaterialColors.Resolve(original[i].name, paint, config);
                    expectedColor.a = 1;
                    Require(block.GetColor("_Color") == expectedColor, "材质颜色未刷新: " + material.name);
                    Require(block.GetColor("_EmissionColor") == Color.black, "自发光未清除: " + material.name);
                    Require(Mathf.Approximately(block.GetFloat("_Glossiness"), config.carSmoothness), "光泽未刷新: " + material.name);
                    Require(block.GetColor("_SpecColor") == Color.white * config.carSpecular, "高光未刷新: " + material.name);
                }
            }
            Require(CarMaterialColors.Resolve("car_black", paint, config) == config.carWindowFrameColor, "窗框颜色未独立");
            Require(CarMaterialColors.Resolve("car_biantiao", paint, config) == config.carTrimColor, "饰条颜色未独立");
            Require(CarMaterialColors.Resolve("car_cequn", paint, config) == config.carFrontSplitterColor, "前铲颜色未独立");
            Require(CarMaterialColors.Resolve("car_chedeng", paint, config) == config.carHeadlightColor, "车灯残留橙色");
            Require(CarMaterialColors.Resolve("car_dengtiao", paint, config) == config.carLightStripColor, "灯条颜色未独立");
        }

        [MenuItem("LoopPark/截取当前游戏画面")]
        public static void Capture()
        {
            Directory.CreateDirectory("Logs");
            ScreenCapture.CaptureScreenshot("Logs/car_game.png");
        }
    }
}
