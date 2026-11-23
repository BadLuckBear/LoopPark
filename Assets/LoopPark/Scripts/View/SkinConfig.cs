using UnityEngine;

namespace LoopPark
{
    /// <summary>一个颜色编号的独立材质参数；各颜色持有独立副本。</summary>
    [System.Serializable]
    public sealed class CarColorAppearance
    {
        [Header("Independent materials (once assigned, material params win; legacy tinting is only a fallback)")]
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
        [Tooltip("0 = rough matte, 1 = glossy. 0.1-0.3 recommended for cartoon cars; adjustable while playing.")]
        [Range(0, 1)] public float carSmoothness = 0.18f;
        [Tooltip("Specular intensity: 0 = no highlight, 1 = strong. Default 0.08 for a less metallic look.")]
        [Range(0, 1)] public float carSpecular = 0.08f;
        [Tooltip("Fixed window color; not affected by paint. A darker blue-gray is recommended to avoid orange glass.")]
        public Color carWindowColor = new Color(0.20f, 0.27f, 0.36f);
        [Tooltip("Color of the body trim.")]
        public Color carTrimColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("Color of window frames and black structural parts.")]
        public Color carWindowFrameColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("Color of the front splitter interior and skirt.")]
        public Color carFrontSplitterColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("Color of the headlights and the adjacent light strip; cold white by default, no emission.")]
        public Color carHeadlightColor = new Color(0.90f, 0.95f, 1f);
        [Tooltip("Color of the light strip beside the headlights.")]
        public Color carLightStripColor = new Color(0.90f, 0.95f, 1f);
        [Tooltip("Fixed taillight color, dark red by default.")]
        public Color carTaillightColor = new Color(0.75f, 0.04f, 0.05f);
        [Tooltip("Fixed wheel color, neutral gray by default.")]
        public Color carWheelColor = new Color(0.25f, 0.28f, 0.33f);
        [Tooltip("Fixed tire color, near black by default; tuned separately from wheels.")]
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
        [Header("Car (top-down, nose up, 4:5 recommended e.g. 256x320)")]
        [Tooltip("Whole-car sprite indexed by color (highest priority). Empty falls back to tinting carBody.")]
        public Sprite[] carSpritesByColor;
        [Tooltip("White body base sprite, tinted by color")]
        public Sprite carBody;
        [Tooltip("Untinted detail layer: windows, lights, drawn above the body")]
        public Sprite carDetail;
        [Tooltip("Layer under the body: wheels, shadow")]
        public Sprite carUnder;
        [Tooltip("Mystery car icon")]
        public Sprite hiddenIcon;
        [Tooltip("Optional: multi-direction frame sequence (white body, gets tinted). Frame 0 faces up, the rest arranged counter-clockwise in equal steps (16 frames = 22.5° each). When set, the car sprite stops rotating and frames are picked by heading, so side views are possible.")]
        public Sprite[] carDirectionalSprites;
        [Tooltip("Untinted detail layer matching carDirectionalSprites (optional, same frame count)")]
        public Sprite[] carDirectionalDetails;
        [Tooltip("Optional: whole-car prefab (may contain Spine / 3D model) replacing all sprites above. For 3D models keep the nose toward +Y and the roof toward -Z (toward the camera); add a component implementing ICarSkin to receive color and animation events.")]
        public GameObject carPrefab;

        [Header("Mechanism 3D models")]
        [Tooltip("Container shell prefab (Container3D). When empty, levels keep the 2D placeholder shutter for easy rollback.")]
        public GameObject containerPrefab;

        [Header("Container shell: size and depth (applies to newly created containers immediately)")]
        [Tooltip("Scale relative to column width: 1.20 = 10% wider on each side so the box hugs the column")]
        [Range(0.8f, 1.6f)] public float containerWidthScale = 1.20f;
        [Tooltip("Extra height relative to column height (in cells): 0.06 = thin edge only, nothing poking above the top")]
        [Range(0f, 0.6f)] public float containerHeightExtraCells = 0.06f;
        [Tooltip("Depth margin for the back panel behind every car in the column (world units): too small blocks cars or z-fights")]
        [Range(0.05f, 1f)] public float containerBackMargin = 0.30f;
        [Tooltip("Depth margin for the front (roof) ahead of the frontmost car / mystery badge in the column: too small exposes the roof")]
        [Range(0.05f, 1f)] public float containerFrontMargin = 0.30f;

        [Header("Container shell: open animation durations (seconds, tunable live)")]
        [Tooltip("Phase one: duration of the prompt prop popping up and shrinking")]
        [Min(0f)] public float containerIconHideDuration = 0.12f;
        [Tooltip("Delay before the roof starts moving up")]
        [Min(0f)] public float containerRoofDelay = 0.06f;
        [Tooltip("Phase two: duration of the roof rolling up (move up + top edge clip)")]
        [Min(0.01f)] public float containerRoofRollDuration = 0.38f;
        [Tooltip("Phase three: duration of the frame retracting along +Y into the top edge")]
        [Min(0f)] public float containerFrameHideDuration = 0.14f;
        [Tooltip("Vertical travel multiplier (relative to container height): above 1 fully clears the clip line")]
        [Range(0.5f, 2f)] public float containerTravelMultiplier = 1.06f;

        [Header("Container shell: unlock prompt prop")]
        [Tooltip("Prompt prop size (in cells)")]
        [Range(0.2f, 2f)] public float containerIconSizeCells = 0.88f;
        [Tooltip("Distance the prompt prop sits in front of the roof plane (avoids z-fighting with the roof)")]
        [Min(0f)] public float containerIconFrontOffset = 0.10f;
        [Tooltip("Icon used for any-color containers (containerColor < 0); empty falls back to the procedural lock icon")]
        public Sprite containerAnyColorIcon;
        [Tooltip("Color 0 (raw 1): red")] public Sprite containerIconRed;
        [Tooltip("Color 1 (raw 2): yellow")] public Sprite containerIconYellow;
        [Tooltip("Color 2 (raw 3): teal")] public Sprite containerIconTeal;
        [Tooltip("Color 3 (raw 4): light blue")] public Sprite containerIconLightBlue;
        [Tooltip("Color 4 (raw 5): green")] public Sprite containerIconGreen;
        [Tooltip("Color 5 (raw 6): brown")] public Sprite containerIconBrown;
        [Tooltip("Color 6 (raw 7): lilac")] public Sprite containerIconLilac;
        [Tooltip("Color 7 (raw 8): pink")] public Sprite containerIconPink;
        [Tooltip("Color 8 (raw 9): orange")] public Sprite containerIconOrange;
        [Tooltip("Color 9 (raw 10): black")] public Sprite containerIconBlack;
        [Tooltip("Color 10 (raw 11): purple")] public Sprite containerIconPurple;
        [Tooltip("Color 11 (raw 12): magenta")] public Sprite containerIconMagenta;
        [Tooltip("Color 12 (raw 13): white")] public Sprite containerIconWhite;
        [Tooltip("Color 13 (raw 14): cream")] public Sprite containerIconCream;
        [Tooltip("Color 14 (raw 15): blue")] public Sprite containerIconBlue;

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

        [Header("Color table (15 entries, ordered by level color index)")]
        [Tooltip("15 body colors; array index matches the level color id; changes refresh existing cars while playing.")]
        public Color[] carColors;
        [Tooltip("Cars with hidden colors all use this color and restore their own body color when revealed.")]
        public Color hiddenCarColor = new Color(0.45f, 0.34f, 0.80f);
        [Header("3D car area view angles")]
        [Tooltip("Top-down tilt of 3D car models in the upper parking area (degrees); 0 = straight down.")]
        public float carViewTilt = 36f;
        [Tooltip("Top-down tilt of 3D car models in the lower departure area (degrees); smoothly blends to the parking angle after departure.")]
        public float departureCarViewTilt = 24f;
        [Min(0.01f), Tooltip("View transition time between departure and parking areas (seconds).")]
        public float carViewTiltTransitionTime = 0.65f;

        [Header("3D car appearance (changes apply immediately while playing)")]
        [Tooltip("X/Y/Z scale relative to the standard car model; defaults X=1.4, Y=1.2, Z=1; changes appearance only, not logical slots or collision spacing.")]
        public Vector3 carModelScale = new Vector3(1.4f, 1.2f, 1f);
        [Header("3D car lighting (applied on level start; previews use the same values)")]
        [Min(0)] public float carKeyLightIntensity = 1.1f;
        [Tooltip("Light direction; Z should be greater than 0.")]
        public Vector3 carKeyLightDirection = new Vector3(0.35f, -1.4f, 1f);
        public Color carAmbientLight = new Color(0.30f, 0.30f, 0.34f);
        [Tooltip("0 = rough matte, 1 = glossy. 0.1-0.3 recommended for cartoon cars; adjustable while playing.")]
        [SerializeField, HideInInspector] float carSmoothness = 0.18f;
        [Tooltip("Specular intensity: 0 = no highlight, 1 = strong. Default 0.08 for a less metallic look.")]
        [SerializeField, HideInInspector] float carSpecular = 0.08f;
        [Tooltip("Fixed window color; not affected by paint. A darker blue-gray is recommended to avoid orange glass.")]
        [SerializeField, HideInInspector] Color carWindowColor = new Color(0.20f, 0.27f, 0.36f);
        [Tooltip("Fixed color for window frames, side skirts, trim strips and front splitter interior.")]
        [SerializeField, HideInInspector] Color carTrimColor = new Color(0.13f, 0.16f, 0.21f);
        [Tooltip("Color of the headlights and the adjacent light strip; cold white by default, no emission.")]
        [SerializeField, HideInInspector] Color carHeadlightColor = new Color(0.90f, 0.95f, 1f);
        [Tooltip("Fixed taillight color, dark red by default.")]
        [SerializeField, HideInInspector] Color carTaillightColor = new Color(0.75f, 0.04f, 0.05f);
        [Tooltip("Fixed wheel color, neutral gray by default.")]
        [SerializeField, HideInInspector] Color carWheelColor = new Color(0.25f, 0.28f, 0.33f);
        [Tooltip("Fixed tire color, near black by default; tuned separately from wheels.")]
        [SerializeField, HideInInspector] Color carTireColor = new Color(0.055f, 0.065f, 0.08f);

        [Tooltip("Configure gloss, windows, trim, lights and wheels per level color id; each is independent and updates live at runtime.")]
        public CarColorAppearance[] carAppearanceByColor;
        [Tooltip("The mystery car uses its own material config so part colors do not reveal the hidden car color.")]
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

        [Header("Car animation")]
        [Tooltip("Nose-dip strength on brake (car tips down and springs back when parking); 0 = off")]
        public float brakeStrength = 300f;
        [Tooltip("Idle wobble amplitude of the departure front row (degrees); 0 = off")]
        public float idleShake = 1.2f;
        [Header("Exhaust puff sprite")]
        [Tooltip("Effect prefab used while the front row idles in the departure area, e.g. Fx_Car_fire1. Takes priority when assigned.")]
        public GameObject idleExhaustFx;
        [Tooltip("Brake-mark effect prefab used while a car moves, e.g. Fx_Car_vestige. Takes priority when assigned.")]
        public GameObject tireTrailFx;
        [Header("Car movement effects")]
        [Tooltip("One-shot acceleration effect when a car starts moving, e.g. Fx_Car_fire2. Takes priority when assigned.")]
        public GameObject accelerationFx;
        [Tooltip("One-shot spark effect on the first corner and when entering the parking area, e.g. Fx_Car_spark.")]
        public GameObject sparkFx;
        [Tooltip("Exhaust puff sprite (empty = procedural cloud)")]
        public Sprite exhaustSprite;
        public Color exhaustColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("Exhaust port position (car-width units, nose up: x right, y forward; tail around -0.5)")]
        public Vector2 exhaustOffset = new Vector2(0.24f, -0.5f);
        [Tooltip("Exhaust puffs per second; 0 = off")]
        public float exhaustRate = 5f;

        [Header("Scene")]
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

        [Header("Audio (empty falls back to same-name files under Resources/Audio, then to synthesized tones)")]
        [Tooltip("Background music (Audio/bgm); enable looping, volume below via musicVolume")] public AudioClip music;
        [Tooltip("Button click (Audio/button_click)")] public AudioClip sfxButton;
        [Tooltip("Feedback when tapping the departure lane to launch a car (Audio/CarPick_S3C3)")] public AudioClip sfxTap;
        [Tooltip("A car departs from the lower area and drives up (Audio/carstart)")] public AudioClip sfxStart;
        [Tooltip("A car parks into a column (Audio/star_ef)")] public AudioClip sfxPark;
        [Tooltip("A column is cleared; plays once per car cleared (Audio/Victory_033_ef)")] public AudioClip sfxClear;
        [Tooltip("Container / shutter / ice unlock (Audio/coin)")] public AudioClip sfxUnlock;
        [Tooltip("Coins increase: level settlement and ad double reward (Audio/jinbi)")] public AudioClip sfxCoin;
        [Tooltip("Level clear (Audio/shengli)")] public AudioClip sfxWin;
        [Tooltip("Level failed (Audio/LevelFail)")] public AudioClip sfxFail;
        [Tooltip("Push car (no asset yet; empty = procedural synth tone)")] public AudioClip sfxPush;
        [Range(0f, 1f), Tooltip("Music volume")] public float musicVolume = 0.5f;
        [Range(0f, 1f), Tooltip("Sound volume")] public float sfxVolume = 1f;
        [Min(0f), Tooltip("Interval between per-car sound effects when clearing several cars at once (seconds); 0 = play together")] public float clearSfxSpacing = 0.09f;

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
