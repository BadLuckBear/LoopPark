using UnityEngine;

namespace LoopPark
{
    /// <summary>车辆颜色表（可在 SkinConfig 中覆盖）。</summary>
    public static class Palette
    {
        public static readonly Color[] Default =
        {
            Hex("F72529"), // 0 红
            Hex("F1D21D"), // 1 黄
            Hex("203AF0"), // 2 蓝
            Hex("14CC16"), // 3 绿
            Hex("8627F7"), // 4 紫
            Hex("FF7510"), // 5 橙
            Hex("FA78B4"), // 6 粉
            Hex("53C8CA"), // 7 青
            Hex("D7DCE6"), // 8 白
            Hex("101217"), // 9 黑
            Hex("984722"), // 10 棕
            Hex("EF58E4"), // 11 洋红
            Hex("18852D"), // 12 深绿
            Hex("EBD18B"), // 13 米黄
            Hex("008EFF"), // 14 天蓝
        };

        public static readonly string[] Names = { "红", "黄", "蓝", "绿", "紫", "橙", "粉", "青", "白", "黑", "棕", "洋红", "深绿", "米黄", "天蓝" };

        public static Color[] Colors
        {
            get
            {
                var skin = SkinConfig.Instance;
                if (skin != null && skin.carColors != null && skin.carColors.Length >= Default.Length) return skin.carColors;
                return Default;
            }
        }

        public static int Count => Default.Length;

        public static Color Get(int i)
        {
            var c = Colors;
            return c[((i % c.Length) + c.Length) % c.Length];
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
