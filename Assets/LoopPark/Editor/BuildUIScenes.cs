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
    static readonly Color Blue = new Color(0.03f, 0.39f, 0.73f);
    static readonly Color White = Color.white;

    [MenuItem("LoopPark/Rebuild Home UI Prefab")]
    public static void BuildHome()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var name in new[] { "splash", "avatar_car", "hud_coin_bar", "hud_heart_bar" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Resources/LegacyUI/" + name + ".png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
        HomePrefab();
        AssetDatabase.SaveAssets();
    }

    [MenuItem("LoopPark/Rebuild UI Scenes and Prefabs (Overwrites)")]
    public static void Build()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        System.IO.Directory.CreateDirectory(Prefabs);
        foreach (var name in new[] { "splash", "avatar_car", "hud_coin_bar", "hud_heart_bar" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Resources/LegacyUI/" + name + ".png");
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
        }

        var loading = LoadingPrefab();
        var home = HomePrefab();
        HudPrefab();
        CreateScene("Loading", loading);
        CreateScene("Home", home);
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(Root + "/Scenes/Game.unity"))
            AssetDatabase.CopyAsset(Root + "/Scenes/Main.unity", Root + "/Scenes/Game.unity");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(Root + "/Scenes/Loading.unity", true),
            new EditorBuildSettingsScene(Root + "/Scenes/Home.unity", true),
            new EditorBuildSettingsScene(Root + "/Scenes/Game.unity", true)
        };
        AssetDatabase.SaveAssets();
        Debug.Log("[LoopPark] Loading, Home, Game scenes and UI prefabs created.");
    }

    static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Resources/LegacySprites/" + name + ".asset");
    static Sprite Splash() => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Resources/LegacyUI/splash.png");
    static Sprite LegacyUI(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Resources/LegacyUI/" + name + ".png");

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
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = .2f;
        return root;
    }

    static GameObject LoadingPrefab()
    {
        var root = Canvas("LoadingUI");
        var view = root.AddComponent<LoadingView>();
        Image(root.transform, "Background", Splash(), White, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1920));
        var track = Image(root.transform, "ProgressTrack", null, new Color(.13f, .13f, .14f), new Vector2(.5f, 0), new Vector2(0, 95), new Vector2(900, 86));
        var fill = Image(track.transform, "ProgressFill", null, new Color(1f, .48f, .02f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(880, 66));
        fill.type = UnityEngine.UI.Image.Type.Filled;
        fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        fill.fillAmount = 0;
        Text(root.transform, "LoadingText", "Loading...", 50, White, new Vector2(.5f, 0), new Vector2(0, 145), new Vector2(500, 70));
        var serialized = new SerializedObject(view);
        serialized.FindProperty("progressFill").objectReferenceValue = fill;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/LoadingUI.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject HomePrefab()
    {
        var root = Canvas("HomeUI");
        var view = root.AddComponent<HomeView>();
        Image(root.transform, "Background", Splash(), White, new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1920));
        Image(root.transform, "AvatarFrame", null, new Color(.04f, .73f, .92f), new Vector2(0, 1), new Vector2(135, -140), new Vector2(160, 160));
        Image(root.transform, "AvatarCar", LegacyUI("avatar_car"), White, new Vector2(0, 1), new Vector2(135, -140), new Vector2(145, 145));
        Image(root.transform, "CoinsPlate", LegacyUI("hud_heart_bar"), White, new Vector2(0, 1), new Vector2(406, -140), new Vector2(320, 74));
        Image(root.transform, "CoinIcon", Art("Coin Frames 1 1"), White, new Vector2(0, 1), new Vector2(300, -140), new Vector2(100, 100));
        Image(root.transform, "CoinAdd", Art("add_icon"), White, new Vector2(0, 1), new Vector2(350, -175), new Vector2(52, 52));
        view.coins = Text(root.transform, "Coins", "0", 44, new Color(.38f, .16f, .08f), new Vector2(0, 1), new Vector2(465, -140), new Vector2(135, 70));
        Image(root.transform, "LivesPlate", LegacyUI("hud_heart_bar"), White, new Vector2(0, 1), new Vector2(740, -140), new Vector2(275, 74));
        Image(root.transform, "HeartIcon", Art("heart_icon_big1"), White, new Vector2(0, 1), new Vector2(610, -140), new Vector2(108, 100));
        view.lives = Text(root.transform, "Lives", "5", 40, new Color(.38f, .16f, .08f), new Vector2(0, 1), new Vector2(760, -140), new Vector2(175, 70));
        view.settingsButton = Button(root.transform, "Settings", "", Art("button_settings"), White, new Vector2(1, 1), new Vector2(-90, -140), new Vector2(100, 100));
        Image(root.transform, "PlayBorder", Art("button_border"), White, new Vector2(.5f, 0), new Vector2(0, 435), new Vector2(620, 245));
        view.playButton = Button(root.transform, "Play", "", Art("button_green"), White, new Vector2(.5f, 0), new Vector2(0, 438), new Vector2(585, 210));
        view.level = Text(view.playButton.transform, "Level", "Level 1", 76, White, new Vector2(.5f, .5f), Vector2.zero, new Vector2(550, 130));
        var outline = view.level.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(.12f, .1f, .08f);
        outline.effectDistance = new Vector2(3, -3);
        Image(root.transform, "Footer", Art("footer_menu_base"), White, new Vector2(.5f, 0), new Vector2(0, 132), new Vector2(1080, 264));
        Image(root.transform, "HomeSelection", Art("footer_menu_selection"), White, new Vector2(.5f, 0), new Vector2(0, 132), new Vector2(360, 264));
        view.shopButton = Button(root.transform, "Shop", "Shop", null, new Color(.05f, .47f, .72f, .01f), new Vector2(1f / 6f, 0), new Vector2(0, 132), new Vector2(350, 255), 39);
        view.leaderboardButton = Button(root.transform, "Leaderboard", "Leaderboard", null, new Color(.05f, .47f, .72f, .01f), new Vector2(5f / 6f, 0), new Vector2(0, 132), new Vector2(350, 255), 39);
        ((RectTransform)view.shopButton.GetComponentInChildren<Text>().transform).anchoredPosition = new Vector2(0, -70);
        ((RectTransform)view.leaderboardButton.GetComponentInChildren<Text>().transform).anchoredPosition = new Vector2(0, -70);
        Image(view.shopButton.transform, "ShopIcon", Art("footer_icon_shop_on"), White, new Vector2(.5f, .5f), new Vector2(0, 38), new Vector2(108, 108));
        Image(root.transform, "HomeIcon", Art("footer_icon_house_on"), White, new Vector2(.5f, 0), new Vector2(0, 170), new Vector2(105, 105));
        Image(root.transform, "LeaderIcon", Art("footer_icon_leader_on_0"), White, new Vector2(5f / 6f, 0), new Vector2(0, 170), new Vector2(115, 100));
        Text(root.transform, "HomeLabel", "Home", 39, White, new Vector2(.5f, 0), new Vector2(0, 62), new Vector2(240, 60));

        view.settingsPanel = Panel(root.transform, "SettingsPanel", "设置", out var card);
        view.soundButton = Button(card, "Sound", "音效：开", null, Blue, new Vector2(.5f, .5f), new Vector2(0, 160), new Vector2(600, 115));
        view.sound = view.soundButton.GetComponentInChildren<Text>();
        view.vibrateButton = Button(card, "Vibrate", "震动：开", null, Blue, new Vector2(.5f, .5f), new Vector2(0, 20), new Vector2(600, 115));
        view.vibrate = view.vibrateButton.GetComponentInChildren<Text>();
        view.resetButton = Button(card, "Reset", "重置进度", null, new Color(.87f, .25f, .23f), new Vector2(.5f, .5f), new Vector2(0, -120), new Vector2(600, 115));
        view.settingsCloseButton = Button(card, "Close", "关闭", null, new Color(.5f, .53f, .58f), new Vector2(.5f, .5f), new Vector2(0, -260), new Vector2(350, 100));

        view.shopPanel = Panel(root.transform, "ShopPanel", "商店", out card);
        view.buyButtons = new Button[3];
        view.buyLabels = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            view.buyButtons[i] = Button(card, "Buy" + i, "购买", Art("button_green"), White, new Vector2(.5f, .5f), new Vector2(0, 200 - i * 155), new Vector2(720, 125), 35);
            view.buyLabels[i] = view.buyButtons[i].GetComponentInChildren<Text>();
        }
        view.shopCloseButton = Button(card, "Close", "关闭", null, new Color(.5f, .53f, .58f), new Vector2(.5f, .5f), new Vector2(0, -310), new Vector2(350, 90));

        view.leaderboardPanel = Panel(root.transform, "LeaderboardPanel", "排行", out card);
        view.bestLevel = Text(card, "BestLevel", "最高关卡", 48, new Color(.15f, .27f, .45f), new Vector2(.5f, .5f), new Vector2(0, 60), new Vector2(650, 120));
        view.leaderboardCloseButton = Button(card, "Close", "关闭", null, Blue, new Vector2(.5f, .5f), new Vector2(0, -190), new Vector2(350, 100));

        view.resetPanel = Panel(root.transform, "ResetPanel", "确认重置？", out card);
        Text(card, "Warning", "关卡、金币和道具会清空", 42, new Color(.3f, .25f, .2f), new Vector2(.5f, .5f), new Vector2(0, 70), new Vector2(750, 110));
        view.confirmResetButton = Button(card, "Confirm", "确认重置", null, new Color(.87f, .25f, .23f), new Vector2(.5f, .5f), new Vector2(0, -70), new Vector2(520, 110));
        view.cancelResetButton = Button(card, "Cancel", "取消", null, Blue, new Vector2(.5f, .5f), new Vector2(0, -210), new Vector2(350, 100));

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/HomeUI.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject Panel(Transform root, string name, string title, out Transform card)
    {
        var shade = Image(root, name, null, new Color(0, 0, 0, .65f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1080, 1920));
        var paper = Image(shade.transform, "Card", null, new Color(1, .97f, .89f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(870, 820));
        card = paper.transform;
        Image(card, "Header", null, new Color(.35f, .43f, .62f), new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(760, 120));
        Text(card, "Title", title, 58, White, new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(700, 105));
        shade.gameObject.SetActive(false);
        return shade.gameObject;
    }

    static void HudPrefab()
    {
        var root = new GameObject("GameHUD", typeof(RectTransform), typeof(HudView));
        var view = root.GetComponent<HudView>();
        view.level = Text(root.transform, "Level", "第 1 关", 64, White, new Vector2(.5f, 1), new Vector2(0, -85), new Vector2(470, 100));
        Image(root.transform, "CoinPlate", null, White, new Vector2(0, 1), new Vector2(180, -82), new Vector2(235, 78));
        Image(root.transform, "CoinIcon", Art("coins"), White, new Vector2(0, 1), new Vector2(75, -82), new Vector2(75, 75));
        view.coins = Text(root.transform, "Coins", "0", 42, new Color(.38f, .16f, .08f), new Vector2(0, 1), new Vector2(205, -82), new Vector2(150, 70));
        view.difficultyBackground = Image(root.transform, "Difficulty", null, new Color(.85f, .26f, .28f), new Vector2(.5f, 1), new Vector2(0, -165), new Vector2(180, 60));
        view.difficulty = Text(view.difficultyBackground.transform, "Text", "困难", 36, White, new Vector2(.5f, .5f), Vector2.zero, new Vector2(170, 55));
        Image(root.transform, "Footer", Art("gameplay-ui-bottom-bar-bg-blue"), White, new Vector2(.5f, 0), new Vector2(0, 95), new Vector2(1080, 190));
        view.boosterButtons = new Button[3];
        view.boosterCounts = new Text[3];
        var icons = new[] { "menu-booster-icon-extra-slot", "gameplay-booster-icon-shuffle-medium-white", "gameplay-booster-icon-hammer-white" };
        for (int i = 0; i < 3; i++)
        {
            var button = Button(root.transform, "Booster" + i, "", Art("gameplay-ui-button-unlocked-blue"), White, new Vector2(.22f + i * .19f, 0), new Vector2(0, 92), new Vector2(150, 150));
            Image(button.transform, "Icon", Art(icons[i]), White, new Vector2(.5f, .5f), Vector2.zero, new Vector2(92, 92));
            view.boosterButtons[i] = button;
            Image(button.transform, "CountBadge", null, new Color(.95f, .12f, .18f), new Vector2(1, 0), new Vector2(-12, 10), new Vector2(55, 55));
            view.boosterCounts[i] = Text(button.transform, "Count", "2", 33, White, new Vector2(1, 0), new Vector2(-12, 10), new Vector2(55, 55));
        }
        view.settingsButton = Button(root.transform, "Settings", "", Art("gameplay-ui-button-unlocked-blue"), White, new Vector2(.79f, 0), new Vector2(0, 92), new Vector2(150, 150));
        Image(view.settingsButton.transform, "Icon", Art("gameplay-ui-settings-icon"), White, new Vector2(.5f, .5f), Vector2.zero, new Vector2(90, 90));
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
