using UnityEngine;

namespace LoopPark
{
    /// <summary>按 FBX 材质名区分车漆与固定部件颜色，避免独立网格残留原始橙色。</summary>
    public static class CarMaterialColors
    {
        public const string ShaderName = "LoopPark/Layered Car";

        /// <summary>根据原始 FBX 槽位匹配，替换材质改名也不会影响部件绑定。</summary>
        public static Material ResolveMaterial(string slot, CarColorAppearance config)
        {
            switch (slot)
            {
                case "car_base":
                case "car_jingzi": return config.paintMaterial;
                case "car_windows": return config.windowMaterial;
                case "car_black": return config.windowFrameMaterial;
                case "car_cequn":
                case "car_xiabai": return config.frontSplitterMaterial;
                case "car_chedeng": return config.headlightMaterial;
                case "car_dengtiao": return config.lightStripMaterial;
                case "car_weideng": return config.taillightMaterial;
                case "car_lungu": return config.wheelMaterial;
                case "car_luntai": return config.tireMaterial;
                default: return config.trimMaterial;
            }
        }

        /// <summary>车身与后视镜跟随车色；玻璃、车灯、轮胎和装饰件使用配置中的固定颜色。</summary>
        public static Color Resolve(string materialName, Color paint, CarColorAppearance config)
        {
            switch (materialName)
            {
                case "car_base":
                case "car_jingzi": return paint;
                case "car_windows": return config.carWindowColor;
                case "car_black": return config.carWindowFrameColor;
                case "car_biantiao": return config.carTrimColor;
                case "car_cequn":
                case "car_xiabai": return config.carFrontSplitterColor;
                case "car_chedeng": return config.carHeadlightColor;
                case "car_dengtiao": return config.carLightStripColor;
                case "car_weideng": return config.carTaillightColor;
                case "car_lungu": return config.carWheelColor;
                case "car_luntai": return config.carTireColor;
                default: return config.carTrimColor;
            }
        }
    }
}
