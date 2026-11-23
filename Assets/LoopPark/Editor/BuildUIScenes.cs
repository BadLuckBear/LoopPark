using LoopPark;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class BuildUIScenes
{
    const string Root = "Assets/LoopPark";
    const string Prefabs = Root + "/Resources/UI";
    const string ArtFolder = Root + "/Resources/ArtUI/";
    static readonly Color White = Color.white;
    static readonly Vector2 TopLeft = new Vector2(0, 1), Top = new Vector2(.5f, 1), Bottom = new Vector2(.5f, 0), Center = new Vector2(.5f, .5f);

    [MenuItem("LoopPark/Rebuild Home UI Prefab")]
    public static void BuildHome()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ImportArt();
        HomePrefab();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("LoopPark/Rebuild Art UI Prefabs")]
    public static void BuildArtPrefabs()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ImportArt();
        LoadingPrefab();
        HomePrefab();
        HudPrefab();
        Debug.Log("[LoopPark] Art UI prefabs rebuilt.");
    }

    [MenuItem("LoopPark/Rebuild UI Scenes and Prefabs (Overwrites)")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        System.IO.Directory.CreateDirectory(Prefabs);
        ImportArt();

        var loading = LoadingPrefab();
        var home = HomePrefab();
        HudPrefab();
        CreateScene("Loading", loading);
        CreateScene("Home", home);
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(Root + "/Scenes/Game.unity"))
            AssetDatabase.CopyAsset(Root + "/Scenes/Main.unity", Root + "/Scenes/Game.unity");
        EditorBuildSettings.scenes = System.Array.ConvertAll(
            LoopPark.EditorTools.ProjectSetup.AppScenePaths(), path => new EditorBuildSettingsScene(path, true));
        AssetDatabase.SaveAssets();
        Debug.Log("[LoopPark] Loading, Home, Game scenes and UI prefabs created.");
    }

    static void ImportArt()
    {
        foreach (var path in System.IO.Directory.GetFiles(ArtFolder, "*.png"))
        {
            var assetPath = path.Replace('\\', '/');
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            var border = SliceBorder(System.IO.Path.GetFileNameWithoutExtension(assetPath));
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single || importer.maxTextureSize < 4096 || importer.spriteBorder != border)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.maxTextureSize = 4096;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.spriteBorder = border;
                importer.SaveAndReimport();
            }
        }
    }

    // Keep corners, outlines and shadows fixed while these designer-sized surfaces expand.
    static Vector4 SliceBorder(string name)
    {
        if (name == "loading_bar") return new Vector4(21, 0, 21, 0);
        if (name == "frame_bg") return new Vector4(72, 72, 72, 72);
        if (name == "frame_title_bg") return new Vector4(72, 72, 72, 72);
        if (name == "Rectangle 630") return new Vector4(34, 34, 34, 34);
        if (name == "Rectangle 631") return new Vector4(48, 48, 48, 48);
        if (name.StartsWith("common_btn_")) return new Vector4(48, 48, 48, 48);
        return Vector4.zero;
    }

    static Sprite UIArt(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + name + ".png")
        ?? throw new System.InvalidOperationException("Missing artwork: " + name);

    static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    static Image Image(Transform parent, string name, Sprite sprite, Color color, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var image = Rect(parent, name, anchor, position, size).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        if (sprite && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    static Image FullScreenImage(Transform parent, string name, Sprite sprite)
    {
        var image = Image(parent, name, sprite, White, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        var rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return image;
    }

    static Text Text(Transform parent, string name, string value, int size, Color color, Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        var text = Rect(parent, name, anchor, position, dimensions).gameObject.AddComponent<Text>();
        text.font = SpriteFactory.Font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        return text;
    }

    static Button Button(Transform parent, string name, string title, Sprite sprite, Color color, Vector2 anchor, Vector2 position, Vector2 size, int fontSize = 42)
    {
        var image = Image(parent, name, sprite, color, anchor, position, size);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        image.gameObject.AddComponent<PressScale>();
        if (!string.IsNullOrEmpty(title)) Text(image.transform, "Label", title, fontSize, White, new Vector2(.5f, .5f), Vector2.zero, size - new Vector2(20, 10));
        return button;
    }

    static GameObject Canvas(string name)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        UIKit.ScaleForDesign(scaler);
        return root;
    }

    static GameObject LoadingPrefab()
    {
        var root = Canvas("LoadingUI");
        var view = root.AddComponent<LoadingView>();
        FullScreenImage(root.transform, "Background", UIArt("loaidng_bg 1"));
        Image(root.transform, "Logo", UIArt("logo 1"), White, new Vector2(.5f, 1), new Vector2(0, -505), new Vector2(733, 528));
        var track = Image(root.transform, "ProgressTrack", UIArt("loading_barbg (1)"), White, Bottom, new Vector2(0, 407), new Vector2(762, 74));
        var fill = Image(track.transform, "ProgressFill", UIArt("loading_bar"), White, new Vector2(0, .5f), new Vector2(12, 0), new Vector2(0, 54));
        fill.rectTransform.pivot = new Vector2(0, .5f);
        var car = Image(track.transform, "ProgressCar", UIArt("loading_car 1"), White, new Vector2(0, .5f), new Vector2(85, 8), new Vector2(170, 67));
        Image(root.transform, "LoadingBadge", UIArt("loading_barbg"), White, Bottom, new Vector2(-336, 308), new Vector2(92, 74));
        Stroke(Text(root.transform, "LoadingText", "Loading...", 58, White, Bottom, new Vector2(12, 310), new Vector2(560, 90)));
        var serialized = new SerializedObject(view);
        serialized.FindProperty("progressFill").objectReferenceValue = fill;
        serialized.FindProperty("progressCar").objectReferenceValue = car.rectTransform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/LoadingUI.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject HomePrefab()
    {
        var root = Canvas("HomeUI");
        var view = root.AddComponent<HomeView>();
        FullScreenImage(root.transform, "Background", UIArt("home_bg"));
        Image(root.transform, "CoinsPlate", UIArt("Rectangle 622 (2)"), White, new Vector2(0, 1), new Vector2(207, -200), new Vector2(260, 82));
        Image(root.transform, "CoinIcon", UIArt("Group 526"), White, new Vector2(0, 1), new Vector2(85, -200), new Vector2(92, 94));
        view.coins = Text(root.transform, "Coins", "0", 48, new Color(.50f, .16f, .02f), new Vector2(0, 1), new Vector2(222, -200), new Vector2(170, 70));
        Image(root.transform, "LivesPlate", UIArt("Rectangle 622 (2)"), White, new Vector2(0, 1), new Vector2(540, -200), new Vector2(260, 82));
        Image(root.transform, "HeartIcon", UIArt("icon_heart_1"), White, new Vector2(0, 1), new Vector2(418, -200), new Vector2(118, 101));
        view.lifeAddButton = Button(root.transform, "LifeAdd", "", UIArt("home_btn_ pause"), White, TopLeft, new Vector2(459, -237), new Vector2(48, 48));
        view.lives = Stroke(Text(root.transform, "Lives", "5", 56, White, TopLeft, new Vector2(418, -199), new Vector2(80, 74)));
        view.lifeTimer = Text(root.transform, "LifeTimer", "Full", 48, new Color(.50f, .16f, .02f), TopLeft, new Vector2(573, -200), new Vector2(165, 70));
        view.settingsButton = Button(root.transform, "Settings", "", UIArt("icon_set"), White, new Vector2(1, 1), new Vector2(-100, -200), new Vector2(130, 130));
        view.playButton = Button(root.transform, "Play", "", UIArt("common_btn_green"), White, new Vector2(.5f, 0), new Vector2(0, 510), new Vector2(684, 276));
        view.playStyles = new[] { UIArt("common_btn_green"), UIArt("common_btn_hard"), UIArt("common_btn_super hard") };
        view.level = Text(view.playButton.transform, "Level", "Level 1", 96, White, Center, Vector2.zero, new Vector2(610, 140));
        view.difficulty = Text(view.playButton.transform, "Difficulty", "", 26, White, Center, new Vector2(0, 70), new Vector2(190, 52));
        var outline = view.level.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(.13f, .23f, .06f);
        outline.effectDistance = new Vector2(4, -4);
        view.shopButton = Button(root.transform, "Shop", "", null, new Color(1, 1, 1, .001f), new Vector2(0, 1), new Vector2(190, -200), new Vector2(340, 140));

        view.settingsPanel = Panel(root.transform, "SettingsPanel", "Setting", out var card, 1500);
        Image(card, "AudioBand", UIArt("Rectangle 631"), White, Center, new Vector2(0, 285), new Vector2(792, 260));
        view.musicButton = Button(card, "Music", "", UIArt("setting_btn_bgm"), White, Center, new Vector2(-250, 285), new Vector2(176, 176));
        view.soundButton = Button(card, "Sound", "", UIArt("setting_btn_sound"), White, Center, new Vector2(0, 285), new Vector2(176, 176));
        view.vibrateButton = Button(card, "Vibrate", "", UIArt("setting_btn_shock"), White, Center, new Vector2(250, 285), new Vector2(176, 176));
        view.contactButton = PanelButton(card, "Contact", "Contact us", "common_btn_yellow", 25);
        view.languageButton = Button(card, "Language", "English", UIArt("common_btn_green (1)"), White, Center, new Vector2(0, -230), new Vector2(624, 212), 56);
        view.language = view.languageButton.GetComponentInChildren<Text>();
        Image(view.languageButton.transform, "LanguageIcon", UIArt("icon_language"), White, Center, new Vector2(-218, 0), new Vector2(70, 74));
        Image(view.languageButton.transform, "LanguageArrow", UIArt("setting_btn_Down"), White, new Vector2(1, .5f), new Vector2(-80, 0), new Vector2(58, 48));
        var menuBg = Image(card, "LanguageMenu", UIArt("Rectangle 630"), White, Center, new Vector2(0, -485), new Vector2(520, 430));
        view.languageMenu = menuBg.gameObject;
        var languageNames = new[] { "English", "Français", "Deutsch", "Italiano", "Japanese" };
        view.languageOptions = new Button[languageNames.Length];
        for (int i = 0; i < languageNames.Length; i++)
        {
            var option = Button(menuBg.transform, "LanguageOption" + i, languageNames[i], null,
                new Color(1, 1, 1, .001f), Center, new Vector2(0, 155 - i * 78), new Vector2(440, 68), 30);
            view.languageOptions[i] = option;
        }
        view.englishButton = null;
        view.chineseButton = null;
        view.languageMenu.SetActive(false);
        view.uid = Text(card, "UID", "UID:186175", 36, new Color(.75f, .82f, 1f), Center, new Vector2(0, -410), new Vector2(600, 60));
        view.version = Text(card, "Version", "version no.0.1", 34, new Color(.75f, .82f, 1f), Center, new Vector2(0, -470), new Vector2(600, 60));
        view.privacyButton = Button(card, "Privacy", "Privacy Policy", null, new Color(1, 1, 1, .001f), Center, new Vector2(0, -545), new Vector2(480, 70), 38);
        view.settingsCloseButton = CloseButton(card);

        view.livesPanel = Panel(root.transform, "LivesPanel", "More Lives?", out card);
        Image(card, "Heart", UIArt("icon_heart_2"), White, Center, new Vector2(0, 116), new Vector2(215, 185));
        view.refillMessage = Text(card, "Status", "", 42, White, Center, new Vector2(0, -66), new Vector2(710, 100));
        view.refillButton = PanelButton(card, "Refill", $"Refill Lives  {Economy.RefillLivesCost}", "common_btn_green (1)", -237);
        view.livesCloseButton = CloseButton(card);

        view.shopPanel = Panel(root.transform, "ShopPanel", "Shop", out card);
        view.buyButtons = new Button[3];
        view.buyLabels = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            view.buyButtons[i] = Button(card, "Buy" + i, "Buy", UIArt("common_btn_green (1)"), White, Center, new Vector2(0, 160 - i * 170), new Vector2(720, 148), 35);
            view.buyLabels[i] = view.buyButtons[i].GetComponentInChildren<Text>();
        }
        view.shopCloseButton = CloseButton(card);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/HomeUI.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject Panel(Transform root, string name, string title, out Transform card, float cardHeight = 880)
    {
        var shade = Image(root, name, null, new Color(0, 0, 0, .65f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1080, 1920));
        UIKit.Stretch(shade.rectTransform);
        shade.raycastTarget = true;
        var paper = Image(shade.transform, "Card", UIArt("frame_bg"), White, Center, Vector2.zero, new Vector2(916, cardHeight));
        card = paper.transform;
        UIKit.Title(paper.rectTransform, title, Color.white);
        shade.gameObject.SetActive(false);
        return shade.gameObject;
    }

    static Text Stroke(Text text)
    {
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(.04f, .10f, .22f);
        outline.effectDistance = new Vector2(4, -4);
        return text;
    }

    static Button PanelButton(Transform card, string name, string title, string art, float y) =>
        Button(card, name, title, UIArt(art), White, Center, new Vector2(0, y), new Vector2(504, 145));

    static Button CloseButton(Transform card) => Button(card, "Close", "×", UIArt("home_btn_ pause (1)"), White,
        new Vector2(1, 1), new Vector2(-62, -42), new Vector2(108, 108), 66);

    static void HudPrefab()
    {
        var root = new GameObject("GameHUD", typeof(RectTransform), typeof(HudView));
        var view = root.GetComponent<HudView>();
        view.level = Stroke(Text(root.transform, "Level", "Level 1", 64, White, Top, new Vector2(0, -225), new Vector2(470, 94)));
        Image(root.transform, "CoinPlate", UIArt("Rectangle 622 (2)"), White, TopLeft, new Vector2(203, -232), new Vector2(260, 82));
        Image(root.transform, "CoinIcon", UIArt("icon_coin 1"), White, TopLeft, new Vector2(84, -232), new Vector2(106, 108));
        view.coins = Text(root.transform, "Coins", "0", 48, new Color(.50f, .16f, .02f), TopLeft, new Vector2(226, -230), new Vector2(170, 70));
        view.difficultyBackground = Image(root.transform, "Difficulty", null, new Color(.85f, .26f, .28f), Top, new Vector2(0, -297), new Vector2(180, 60));
        view.difficulty = Text(view.difficultyBackground.transform, "Text", "HARD", 36, White, new Vector2(.5f, .5f), Vector2.zero, new Vector2(170, 55));
        for (int i = 0; i < 3; i++)
            Image(root.transform, "CollectionSlot" + i, UIArt("Rectangle 629"), White, Vector2.zero, new Vector2(91 + i * 124, 759), new Vector2(127, 127));
        var footer = Image(root.transform, "Footer", UIArt("collect_top_bg"), White, Bottom, new Vector2(0, 150), new Vector2(1125, 300));
        footer.rectTransform.anchorMin = Vector2.zero;
        footer.rectTransform.anchorMax = Vector2.right;
        footer.rectTransform.sizeDelta = new Vector2(0, 300);
        view.boosterButtons = new Button[3];
        view.boosterCounts = new Text[3];
        view.boosterBadges = new Image[3];
        var icons = new[] { "icon_add cart", "icon_Green light", "icon_banner 3" };
        for (int i = 0; i < 3; i++)
        {
            var button = Button(root.transform, "Booster" + i, "", UIArt("Group 512"), White, Bottom, new Vector2((i - 1) * 252, 262), new Vector2(190, 190));
            var icon = UIArt(icons[i]);
            Image(button.transform, "Icon", icon, White, Center, Vector2.zero, icon.rect.size);
            view.boosterButtons[i] = button;
            view.boosterBadges[i] = Image(button.transform, "CountBadge", UIArt("home_btn_ pause (1)"), White, new Vector2(1, 0), new Vector2(-30, 30), new Vector2(53, 55));
            view.boosterCounts[i] = Stroke(Text(button.transform, "Count", "2", 30, White, new Vector2(1, 0), new Vector2(-30, 30), new Vector2(53, 55)));
        }
        view.settingsButton = Button(root.transform, "Settings", "", UIArt("icon_set"), White, new Vector2(1, 1), new Vector2(-100, -232), new Vector2(130, 130));
        PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/GameHUD.prefab");
        Object.DestroyImmediate(root);
    }

    static void CreateScene(string name, GameObject prefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 0, -10);
        camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        camera.GetComponent<Camera>().backgroundColor = new Color(.45f, .51f, .69f);
        var system = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        if (name == "Loading") new GameObject("LoadingController").AddComponent<LoadingController>().view = instance.GetComponent<LoadingView>();
        else new GameObject("HomeController").AddComponent<HomeController>().view = instance.GetComponent<HomeView>();
        EditorSceneManager.SaveScene(scene, Root + "/Scenes/" + name + ".unity");
    }
}


