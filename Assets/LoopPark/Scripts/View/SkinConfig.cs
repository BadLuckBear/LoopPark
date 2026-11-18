using UnityEngine;

namespace LoopPark
{
    /// <summary>一个颜色编号的独立材质参数；各颜色持有独立副本。</summary>
    [System.Serializable]
    public sealed class CarColorAppearance
    {
        [Tooltip("0=粗糙哑光，1=光滑亮面。建议卡通车使用 0.1～0.3；播放时可调整。")]
        [Range(0, 1)] public float carSmoothness = 0.18f;
        [Tooltip("反射高光的强度：0=关闭高光，1=强高光。默认 0.08，降低写实金属感。")]
        [Range(0, 1)] public float carSpecular = 0.08f;
        [Tooltip("车窗的固定颜色，不随车漆变化；建议使用较暗的蓝灰色，避免玻璃泛橙。")]
        public Color carWindowColor = new Color(0.20f, 0.27f, 0.36f);
        [Tooltip("窗框、侧裙、边条、前铲内部等装饰件的固定颜色。")]
        public Color carTrimColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("前车灯及旁边灯条的颜色；默认冷白色，不启用自发光。")]
        public Color carHeadlightColor = new Color(0.90f, 0.95f, 1f);
        [Tooltip("后尾灯的固定颜色，默认暗红色。")]
        public Color carTaillightColor = new Color(0.75f, 0.04f, 0.05f);
        [Tooltip("轮毂的固定颜色，默认中性灰色。")]
        public Color carWheelColor = new Color(0.25f, 0.28f, 0.33f);
        [Tooltip("轮胎的固定颜色，默认接近黑色；与轮毂分开调节。")]
        public Color carTireColor = new Color(0.055f, 0.065f, 0.08f);
        /// <summary>复制值字段，避免不同车色引用同一组可变参数。</summary>
        public CarColorAppearance Copy() => (CarColorAppearance)MemberwiseClone();
    }

    /// <summary>
    /// 美术皮肤配置。放在 Resources/LoopParkSkin.asset。
    /// 所有字段为空时使用程序生成的占位图；美术到位后直接把资源拖进来即可，代码无需修改。
    /// </summary>
    [CreateAssetMenu(fileName = "LoopParkSkin", menuName = "LoopPark/Skin Config")]
    public class SkinConfig : ScriptableObject
    {
        [Header("车辆（俯视角，车头朝上，建议 4:5，如 256x320）")]
        [Tooltip("按颜色索引的整车图（优先级最高）。留空则用 carBody 染色。")]
        public Sprite[] carSpritesByColor;
        [Tooltip("白色车身底图，会按颜色染色")]
        public Sprite carBody;
        [Tooltip("不染色的细节层：车窗、车灯等，叠在车身上面")]
        public Sprite carDetail;
        [Tooltip("车身下面的层：轮子、阴影")]
        public Sprite carUnder;
        [Tooltip("问号车的图标")]
        public Sprite hiddenIcon;
        [Tooltip("可选：多方向序列帧（白色车身，会染色）。第 0 帧车头朝上，之后按逆时针等分角度排列（如 16 帧 = 每 22.5°）。设置后车图不旋转，而是按行驶方向切帧，可表现侧面。")]
        public Sprite[] carDirectionalSprites;
        [Tooltip("与 carDirectionalSprites 对应的不染色细节层（可选，帧数相同）")]
        public Sprite[] carDirectionalDetails;
        [Tooltip("可选：整车 prefab（可包含 Spine / 3D 模型），会替换以上所有 sprite。3D 模型请让车头朝 +Y、车顶朝 -Z（朝向相机）；挂一个实现 ICarSkin 的组件即可接收颜色和动画事件。")]
        public GameObject carPrefab;

        [Header("颜色表（15 个，顺序与关卡颜色索引一致）")]
        [Tooltip("15 种车身颜色，数组下标对应关卡中的颜色编号；播放时调整会刷新已有车辆。")]
        public Color[] carColors;
        [Tooltip("隐藏颜色的问号车统一使用此颜色，揭示后恢复对应的车身颜色。")]
        public Color hiddenCarColor = new Color(0.45f, 0.34f, 0.80f);
        [Tooltip("3D 车模型的俯视倾角（度）：模型绕屏幕水平轴倾斜，能看到车头/车尾侧面，0 = 正俯视")]
        public float carViewTilt = 36f;

        [Header("3D 车辆外观（播放时修改立即生效）")]
        [Tooltip("相对标准车模型的横向/纵向缩放，默认 X=1.4、Y=1.2；只改变外观，不改变逻辑车位或碰撞间距。")]
        public Vector2 carModelScale = new Vector2(1.4f, 1.2f);
        [Tooltip("0=粗糙哑光，1=光滑亮面。建议卡通车使用 0.1～0.3；播放时可调整。")]
        [SerializeField, HideInInspector] float carSmoothness = 0.18f;
        [Tooltip("反射高光的强度：0=关闭高光，1=强高光。默认 0.08，降低写实金属感。")]
        [SerializeField, HideInInspector] float carSpecular = 0.08f;
        [Tooltip("车窗的固定颜色，不随车漆变化；建议使用较暗的蓝灰色，避免玻璃泛橙。")]
        [SerializeField, HideInInspector] Color carWindowColor = new Color(0.20f, 0.27f, 0.36f);
        [Tooltip("窗框、侧裙、边条、前铲内部等装饰件的固定颜色。")]
        [SerializeField, HideInInspector] Color carTrimColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("前车灯及旁边灯条的颜色；默认冷白色，不启用自发光。")]
        [SerializeField, HideInInspector] Color carHeadlightColor = new Color(0.90f, 0.95f, 1f);
        [Tooltip("后尾灯的固定颜色，默认暗红色。")]
        [SerializeField, HideInInspector] Color carTaillightColor = new Color(0.75f, 0.04f, 0.05f);
        [Tooltip("轮毂的固定颜色，默认中性灰色。")]
        [SerializeField, HideInInspector] Color carWheelColor = new Color(0.25f, 0.28f, 0.33f);
        [Tooltip("轮胎的固定颜色，默认接近黑色；与轮毂分开调节。")]
        [SerializeField, HideInInspector] Color carTireColor = new Color(0.055f, 0.065f, 0.08f);

        [Tooltip("按关卡颜色编号分别配置光泽、车窗、饰条、车灯和车轮；每项独立，运行时修改立即刷新。")]
        public CarColorAppearance[] carAppearanceByColor;
        [Tooltip("问号车独立使用此材质配置，避免部件颜色暴露隐藏车色。")]
        public CarColorAppearance hiddenCarAppearance;

        [SerializeField, HideInInspector] int carAppearanceVersion;

        /// <summary>旧配置首次迁移时复制当前值，保留已经调好的外观。</summary>
        public void EnsureCarAppearances()
        {
            var defaults = new CarColorAppearance
            {
                carSmoothness = carSmoothness,
                carSpecular = carSpecular,
                carWindowColor = carWindowColor,
                carTrimColor = carTrimColor,
                carHeadlightColor = carHeadlightColor,
                carTaillightColor = carTaillightColor,
                carWheelColor = carWheelColor,
                carTireColor = carTireColor,
            };
            // Unity 可能把缺失的嵌套对象构造成默认值，首次升级必须显式复制旧参数。
            if (carAppearanceVersion == 0)
            {
                carAppearanceByColor = new CarColorAppearance[Palette.Count];
                hiddenCarAppearance = defaults.Copy();
                carAppearanceVersion = 1;
            }
            if (carAppearanceByColor == null || carAppearanceByColor.Length < Palette.Count)
                System.Array.Resize(ref carAppearanceByColor, Palette.Count);
            for (int i = 0; i < carAppearanceByColor.Length; i++)
                if (carAppearanceByColor[i] == null) carAppearanceByColor[i] = defaults.Copy();
            if (hiddenCarAppearance == null) hiddenCarAppearance = defaults.Copy();
        }

        /// <summary>按稳定编号取参数，两个车漆相同也不会串色；负数表示问号车。</summary>
        public CarColorAppearance GetCarAppearance(int colorIndex)
        {
            if (carAppearanceVersion == 0 || carAppearanceByColor == null || carAppearanceByColor.Length < Palette.Count || hiddenCarAppearance == null)
                EnsureCarAppearances();
            if (colorIndex < 0) return hiddenCarAppearance;
            int index = colorIndex % Palette.Count;
            if (carAppearanceByColor[index] == null) EnsureCarAppearances();
            return carAppearanceByColor[index];
        }
        /// <summary>配置变化时通知当前车辆，避免逐帧轮询所有外观字段。</summary>
        public event System.Action CarAppearanceChanged;

        /// <summary>让所有订阅车辆立即应用颜色、材质和缩放；运行时修改配置后调用。</summary>
        public void RefreshCarAppearance() => CarAppearanceChanged?.Invoke();

        /// <summary>约束缩放范围，并将 Inspector 的修改延迟到编辑器主线程分发。</summary>
        void OnValidate()
        {
            EnsureCarAppearances();
            carModelScale.x = Mathf.Max(0.1f, carModelScale.x);
            carModelScale.y = Mathf.Max(0.1f, carModelScale.y);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= RefreshCarAppearance;
            UnityEditor.EditorApplication.delayCall += RefreshCarAppearance;
#endif
        }

        [Header("车辆动画")]
        [Tooltip("刹车前倾力度（停车时车头向下点一下再回弹），0 = 关闭")]
        public float brakeStrength = 300f;
        [Tooltip("发车道第一排的待机抖动幅度（度），0 = 关闭")]
        public float idleShake = 1.2f;
        [Header("尾气烟团图")]
        [Tooltip("尾气烟团图（留空用程序生成的云朵）")]
        public Sprite exhaustSprite;
        public Color exhaustColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("尾气口位置（车宽单位，车头朝上：x 向右，y 向前；车尾约 -0.5）")]
        public Vector2 exhaustOffset = new Vector2(0.24f, -0.5f);
        [Tooltip("每秒冒几团尾气，0 = 关闭")]
        public float exhaustRate = 5f;

        [Header("场景")]
        public Color background = new Color(0.47f, 0.49f, 0.72f);
        public Color trackColor = new Color(0.36f, 0.37f, 0.52f);
        public Color trackEdge = new Color(0.86f, 0.87f, 0.96f);
        public Color lotColor = new Color(0.30f, 0.31f, 0.45f);
        public Color columnColor = new Color(0.40f, 0.41f, 0.57f);
        public Color grassColor = new Color(0.55f, 0.80f, 0.25f);
        public Sprite backgroundSprite;

        [Header("UI")]
        public Font uiFont;
        public Sprite buttonSprite;
        public Sprite panelSprite;
        public Color primaryButton = new Color(0.30f, 0.78f, 0.30f);
        public Color secondaryButton = new Color(0.22f, 0.52f, 0.95f);
        public Color dangerButton = new Color(0.92f, 0.33f, 0.30f);
        public Color panelColor = new Color(1f, 0.97f, 0.90f);

        [Header("音效（留空使用程序合成音）")]
        public AudioClip sfxTap, sfxPark, sfxPush, sfxClear, sfxWin, sfxFail, sfxButton;
        public AudioClip music;

        static SkinConfig instance;
        static bool loaded;

        public static SkinConfig Instance
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    instance = Resources.Load<SkinConfig>("LoopParkSkin");
                    if (instance == null) instance = CreateInstance<SkinConfig>();
                }
                return instance;
            }
        }
    }
}
