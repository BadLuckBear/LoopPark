using UnityEngine;

namespace LoopPark
{
    /// <summary>一个颜色编号的独立材质参数；各颜色持有独立副本。</summary>
    [System.Serializable]
    public sealed class CarColorAppearance
    {
        [Header("独立材质（指定后以材质参数为准，旧调色仅作回退）")]
        public Material paintMaterial;
        public Material windowMaterial;
        public Material windowFrameMaterial;
        public Material trimMaterial;
        public Material frontSplitterMaterial;
        public Material headlightMaterial;
        public Material lightStripMaterial;
        public Material taillightMaterial;
        public Material wheelMaterial;
        public Material tireMaterial;
        [Tooltip("0=粗糙哑光，1=光滑亮面。建议卡通车使用 0.1～0.3；播放时可调整。")]
        [Range(0, 1)] public float carSmoothness = 0.18f;
        [Tooltip("反射高光的强度：0=关闭高光，1=强高光。默认 0.08，降低写实金属感。")]
        [Range(0, 1)] public float carSpecular = 0.08f;
        [Tooltip("车窗的固定颜色，不随车漆变化；建议使用较暗的蓝灰色，避免玻璃泛橙。")]
        public Color carWindowColor = new Color(0.20f, 0.27f, 0.36f);
        [Tooltip("车身饰条的颜色。")]
        public Color carTrimColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("车窗框与黑色结构件的颜色。")]
        public Color carWindowFrameColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("前铲内部与下摆的颜色。")]
        public Color carFrontSplitterColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("前车灯及旁边灯条的颜色；默认冷白色，不启用自发光。")]
        public Color carHeadlightColor = new Color(0.90f, 0.95f, 1f);
        [Tooltip("前灯旁灯条的颜色。")]
        public Color carLightStripColor = new Color(0.90f, 0.95f, 1f);
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

        [Header("机关 3D 模型")]
        [Tooltip("集装箱外壳预制体（Container3D）。留空时关卡里的集装箱沿用 2D 占位卷帘门，便于回退。")]
        public GameObject containerPrefab;

        [Header("集装箱外壳：尺寸与深度（改完立即对新生成的集装箱生效）")]
        [Tooltip("相对列宽的放大倍率：1.20 = 左右各外扩 10%，让箱体贴住车列")]
        [Range(0.8f, 1.6f)] public float containerWidthScale = 1.20f;
        [Tooltip("相对列高额外增加的高度（单位 cell）：0.06 ≈ 只补一层薄边，顶部不冒头")]
        [Range(0f, 0.6f)] public float containerHeightExtraCells = 0.06f;
        [Tooltip("背板落在列内所有车之后的深度余量（世界单位）：太小会挡车或与车 Z-fighting")]
        [Range(0.05f, 1f)] public float containerBackMargin = 0.30f;
        [Tooltip("正面（顶盖）落在列内最前一辆车/问号徽章之前的深度余量：太小会露出车顶")]
        [Range(0.05f, 1f)] public float containerFrontMargin = 0.30f;

        [Header("集装箱外壳：开箱动画时长（秒，可热调）")]
        [Tooltip("阶段一：解锁提示物弹起缩小的时长")]
        [Min(0f)] public float containerIconHideDuration = 0.12f;
        [Tooltip("顶盖开始上移前的延迟")]
        [Min(0f)] public float containerRoofDelay = 0.06f;
        [Tooltip("阶段二：顶盖卷起（上移 + 顶边裁切）的时长")]
        [Min(0.01f)] public float containerRoofRollDuration = 0.38f;
        [Tooltip("阶段三：外框沿 +Y 收进顶边的时长")]
        [Min(0f)] public float containerFrameHideDuration = 0.14f;
        [Tooltip("上移距离倍率（相对箱体高度）：>1 保证完全移出裁切线")]
        [Range(0.5f, 2f)] public float containerTravelMultiplier = 1.06f;

        [Header("集装箱外壳：解锁提示物")]
        [Tooltip("提示物尺寸（单位 cell）")]
        [Range(0.2f, 2f)] public float containerIconSizeCells = 0.88f;
        [Tooltip("提示物相对顶盖平面的前置距离（避免与顶盖同深度打架）")]
        [Min(0f)] public float containerIconFrontOffset = 0.10f;
        [Tooltip("任意颜色集装箱（containerColor < 0）用的图标；留空回退到程序锁图标")]
        public Sprite containerAnyColorIcon;
        [Tooltip("颜色 0（原始 1）：红")] public Sprite containerIconRed;
        [Tooltip("颜色 1（原始 2）：黄")] public Sprite containerIconYellow;
        [Tooltip("颜色 2（原始 3）：青绿")] public Sprite containerIconTeal;
        [Tooltip("颜色 3（原始 4）：浅蓝")] public Sprite containerIconLightBlue;
        [Tooltip("颜色 4（原始 5）：绿")] public Sprite containerIconGreen;
        [Tooltip("颜色 5（原始 6）：棕")] public Sprite containerIconBrown;
        [Tooltip("颜色 6（原始 7）：淡紫")] public Sprite containerIconLilac;
        [Tooltip("颜色 7（原始 8）：粉")] public Sprite containerIconPink;
        [Tooltip("颜色 8（原始 9）：橙")] public Sprite containerIconOrange;
        [Tooltip("颜色 9（原始 10）：黑")] public Sprite containerIconBlack;
        [Tooltip("颜色 10（原始 11）：紫")] public Sprite containerIconPurple;
        [Tooltip("颜色 11（原始 12）：品红")] public Sprite containerIconMagenta;
        [Tooltip("颜色 12（原始 13）：白")] public Sprite containerIconWhite;
        [Tooltip("颜色 13（原始 14）：奶油")] public Sprite containerIconCream;
        [Tooltip("颜色 14（原始 15）：蓝")] public Sprite containerIconBlue;

        /// <summary>
        /// 按关卡颜色索引取解锁图标（索引顺序与 Palette.Names 一致：0=红 … 14=蓝）。
        /// 未配置返回 null，由 ContainerColorIcon 继续回退到预制体/程序锁。
        /// </summary>
        public Sprite GetContainerColorIcon(int colorIndex)
        {
            switch (colorIndex)
            {
                case 0: return containerIconRed;
                case 1: return containerIconYellow;
                case 2: return containerIconTeal;
                case 3: return containerIconLightBlue;
                case 4: return containerIconGreen;
                case 5: return containerIconBrown;
                case 6: return containerIconLilac;
                case 7: return containerIconPink;
                case 8: return containerIconOrange;
                case 9: return containerIconBlack;
                case 10: return containerIconPurple;
                case 11: return containerIconMagenta;
                case 12: return containerIconWhite;
                case 13: return containerIconCream;
                case 14: return containerIconBlue;
                default: return null;
            }
        }

        [Header("颜色表（15 个，顺序与关卡颜色索引一致）")]
        [Tooltip("15 种车身颜色，数组下标对应关卡中的颜色编号；播放时调整会刷新已有车辆。")]
        public Color[] carColors;
        [Tooltip("隐藏颜色的问号车统一使用此颜色，揭示后恢复对应的车身颜色。")]
        public Color hiddenCarColor = new Color(0.45f, 0.34f, 0.80f);
        [Header("3D 车辆区域视角")]
        [Tooltip("上方停车区的 3D 车模型俯视倾角（度）；0 = 正俯视。")]
        public float carViewTilt = 36f;
        [Tooltip("下方发车区的 3D 车模型俯视倾角（度）；发车后会平滑过渡到停车区角度。")]
        public float departureCarViewTilt = 24f;
        [Min(0.01f), Tooltip("发车区与停车区之间的视角过渡时长（秒）。")]
        public float carViewTiltTransitionTime = 0.65f;

        [Header("3D 车辆外观（播放时修改立即生效）")]
        [Tooltip("相对标准车模型的 X/Y/Z 缩放，默认 X=1.4、Y=1.2、Z=1；只改变外观，不改变逻辑车位或碰撞间距。")]
        public Vector3 carModelScale = new Vector3(1.4f, 1.2f, 1f);
        [Header("3D 车辆光照（启动关卡时应用，预览使用同一组参数）")]
        [Min(0)] public float carKeyLightIntensity = 1.1f;
        [Tooltip("光线照射方向，Z 应大于 0。")]
        public Vector3 carKeyLightDirection = new Vector3(0.35f, -1.4f, 1f);
        public Color carAmbientLight = new Color(0.30f, 0.30f, 0.34f);
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
        [SerializeField, HideInInspector] public bool carMaterialsInitialized;

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
                carWindowFrameColor = carTrimColor,
                carFrontSplitterColor = carTrimColor,
                carHeadlightColor = carHeadlightColor,
                carLightStripColor = carHeadlightColor,
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
            if (carAppearanceVersion == 1)
            {
                if (carAppearanceByColor != null)
                    foreach (var profile in carAppearanceByColor)
                        if (profile != null) MigrateCarParts(profile);
                if (hiddenCarAppearance != null) MigrateCarParts(hiddenCarAppearance);
                carAppearanceVersion = 2;
            }
            if (carAppearanceByColor == null || carAppearanceByColor.Length < Palette.Count)
                System.Array.Resize(ref carAppearanceByColor, Palette.Count);
            for (int i = 0; i < carAppearanceByColor.Length; i++)
                if (carAppearanceByColor[i] == null) carAppearanceByColor[i] = defaults.Copy();
            if (hiddenCarAppearance == null) hiddenCarAppearance = defaults.Copy();
        }

        static void MigrateCarParts(CarColorAppearance profile)
        {
            profile.carWindowFrameColor = profile.carTrimColor;
            profile.carFrontSplitterColor = profile.carTrimColor;
            profile.carLightStripColor = profile.carHeadlightColor;
        }

        /// <summary>按稳定编号取参数，两个车漆相同也不会串色；负数表示问号车。</summary>
        public CarColorAppearance GetCarAppearance(int colorIndex)
        {
            if (carAppearanceVersion < 2 || carAppearanceByColor == null || carAppearanceByColor.Length < Palette.Count || hiddenCarAppearance == null)
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
            carModelScale.z = Mathf.Max(0.1f, carModelScale.z);
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
        [Tooltip("发车区第一排待机时使用的特效预制体，例如 Fx_Car_fire1。挂载后优先使用该预制体。")]
        public GameObject idleExhaustFx;
        [Tooltip("车辆移动时使用的刹车痕迹特效预制体，例如 Fx_Car_vestige。挂载后优先使用该预制体。")]
        public GameObject tireTrailFx;
        [Header("车辆移动特效")]
        [Tooltip("车辆开始移动时播放的一次性加速特效，例如 Fx_Car_fire2。挂载后优先使用该预制体。")]
        public GameObject accelerationFx;
        [Tooltip("车辆第一次过弯和进入停车区时播放的一次性火花特效，例如 Fx_Car_spark。")]
        public GameObject sparkFx;
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
