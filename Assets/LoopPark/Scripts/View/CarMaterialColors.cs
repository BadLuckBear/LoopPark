using UnityEngine;

namespace LoopPark
{
    /// <summary>按 FBX 材质名区分车漆与固定部件颜色，避免独立网格残留原始橙色。</summary>
    public static class CarMaterialColors
    {
        /// <summary>车身与后视镜跟随车色；玻璃、车灯、轮胎和装饰件使用配置中的固定颜色。</summary>
        public static Color Resolve(string materialName, Color paint, CarColorAppearance config)
        {
            switch (materialName)
            {
                case "car_base":
                case "car_jingzi": return paint;
                case "car_windows": return config.carWindowColor;
                case "car_chedeng":
                case "car_dengtiao": return config.carHeadlightColor;
                case "car_weideng": return config.carTaillightColor;
                case "car_lungu": return config.carWheelColor;
                case "car_luntai": return config.carTireColor;
                default: return config.carTrimColor;
            }
        }
    }
}
