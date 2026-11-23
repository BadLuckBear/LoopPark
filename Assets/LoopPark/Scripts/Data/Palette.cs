using UnityEngine;

namespace LoopPark
{
    /// <summary>车辆颜色表。颜色索引已经归一化为原始关卡颜色 1..15 对应的 0..14。</summary>
    public static class Palette
    {
        public const int DarkGreen = 12;
        public const int Beige = 13;
        public const int SkyBlue = 14;

        public static readonly Color[] Default =
        {
            Hex("F8211F"), // 0 原始 1：红
            Hex("E8D82A"), // 1 原始 2：黄
            Hex("71E5E4"), // 2 原始 3：青绿
            Hex("0788FA"), // 3 原始 4：浅蓝
            Hex("3AE61D"), // 4 原始 5：绿
            Hex("A6431B"), // 5 原始 6：棕
            Hex("007121"), // 6 原始 7：淡紫/深绿
            Hex("FE68FF"), // 7 原始 8：粉
            Hex("EB7300"), // 8 原始 9：橙
            Hex("353B48"), // 9 原始 10：黑
            Hex("9355EE"), // 10 原始 11：紫
            Hex("F06480"), // 11 原始 12：品红
            Hex("B4C1BF"), // 12 原始 13：白
            Hex("BAA05B"), // 13 原始 14：奶油
            Hex("191BEE"), // 14 原始 15：蓝
        };

        public static readonly string[] Names = { "红", "黄", "青绿", "浅蓝", "绿", "棕", "淡紫", "粉", "橙", "黑", "紫", "品红", "白", "奶油", "蓝" };

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
