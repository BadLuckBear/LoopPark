using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LoopPark
{
    /// <summary>
    /// 全部界面：主界面、游戏 HUD、暂停、胜利、失败、商店、设置、体力不足、提示气泡。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        /// <summary>GM 工具开关。上线前改成 false。</summary>
        public static bool GMEnabled = true;

        public static UIManager I { get; private set; }

        Canvas canvas;
        RectTransform canvasRect;
        GameController game;
        Tutorial tutorial;

        // HUD
        GameObject hud;
        Text hudLevel, hudCoins, hudDiff;
        Image hudDiffBg;
        readonly Button[] boosterBtns = new Button[3];
        readonly Text[] boosterCounts = new Text[3];
        Image[] boosterBadges;
        Sprite availableBadge, buyBadge;
        // 弹窗
        GameObject modal;
        // 只保存当前弹窗链路可返回的上一级页面，开始新流程或回主页时清空。
        readonly System.Collections.Generic.Stack<System.Action> popupBackStack = new();
        // 提示
        RectTransform tipRoot;
        Text tipText;
        float tipTimer;
        RectTransform hand;
        Bob handBob;

        int currentLevel;

        public RectTransform CanvasRect => canvasRect;

        void Awake() => I = this;

        public void Init(GameController g)
        {
            game = g;
            BuildCanvas();
            BuildHud();
            BuildTip();
            tutorial = gameObject.AddComponent<Tutorial>();
            tutorial.Init(this, game);
            game.Won += OnWin;
            game.Failed += OnFail;
            game.OnEvent += (id, car) => EventTrack.GameEvent(id);
            SaveSystem.Changed += RefreshBars;
        }

        void BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            UIKit.ScaleForDesign(scaler);
            canvasRect = (RectTransform)go.transform;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        public void ShowHome()
        {
            const string homeScene = "Home";
            if (!Application.CanStreamedLevelBeLoaded(homeScene))
            {
                Debug.LogError("[LoopPark] Home scene is missing from the build.");
                ShowTip("This version is missing the home scene; please update the game", 2f);
                return;
            }
            popupBackStack.Clear();
            CloseModal();
            game.Cleanup();
            tutorial.Stop();
            SceneManager.LoadScene(homeScene);
        }

        void OnDestroy() => SaveSystem.Changed -= RefreshBars;

        void RefreshBars()
        {
            var d = SaveSystem.D;
            if (hudCoins) hudCoins.text = d.coins.ToString();
            for (int i = 0; i < 3; i++)
            {
                if (!boosterCounts[i]) continue;
                bool hasStock = d.boosters[i] > 0;
                boosterCounts[i].text = hasStock ? d.boosters[i].ToString() : "";
                if (boosterBadges.Length == 3) boosterBadges[i].sprite = hasStock ? availableBadge : buyBadge;
                else if (!hasStock) boosterCounts[i].text = "+";
            }
        }

        void Update()
        {
            SaveSystem.TickLives();
            if (tipTimer > 0 && tipTimer < 900 && tipShownAt + 0.6f < Time.time && Input.GetMouseButtonDown(0))
                tipTimer = 0.01f; // 点一下屏幕收起提示（引导中的常驻提示除外）
            if (tipTimer > 0)
            {
                tipTimer -= Time.deltaTime;
                if (tipTimer <= 0) tipRoot.gameObject.SetActive(false);
            }
        }

        public void StartLevel(int n)
        {
            n = Mathf.Clamp(n, 1, LevelRepository.MaxLevel);
            // 新关卡开始后，旧弹窗链路不再属于当前流程。
            popupBackStack.Clear();
            CloseModal();
            HideTip();
            HideHand();
            currentLevel = n;
            var data = LevelRepository.Get(n);
            hud.SetActive(true);
            game.StartLevel(data);
            EventTrack.LevelStart(n, data.difficulty, data.TotalCars());
            EventTrack.UeShow("game");
            hudLevel.text = $"Level {n}";
            hudDiffBg.gameObject.SetActive(data.difficulty > 0);
            hudDiff.text = data.difficulty == 2 ? "SUPER HARD" : "HARD";
            hudDiffBg.color = data.difficulty == 2 ? new Color(0.6f, 0.25f, 0.85f) : new Color(0.9f, 0.25f, 0.3f);
            RefreshBars();
            tutorial.OnLevelStart(data);
        }

        // ===================================================================
        // HUD
        // ===================================================================
        void BuildHud()
        {
            const string hudPrefabPath = "UI/GameHUD_P";
            var prefab = Resources.Load<GameObject>(hudPrefabPath);
            if (!prefab)
            {
                Debug.LogError($"[LoopPark] Missing HUD prefab at Resources/{hudPrefabPath}.");
                return;
            }
            hud = Instantiate(prefab, canvasRect);
            UIKit.Stretch((RectTransform)hud.transform);
            var view = hud.GetComponent<HudView>();
            hudLevel = view.level;
            hudCoins = view.coins;
            hudDiff = view.difficulty;
            hudDiffBg = view.difficultyBackground;
            boosterBadges = view.boosterBadges ?? System.Array.Empty<Image>();
            availableBadge = UIKit.Art("home_btn_ pause (1)");
            buyBadge = UIKit.Art("home_btn_ pause");
            view.settingsButton.onClick.AddListener(() => { Sfx.Play("button"); ShowExitPrompt(); });
            if (GMEnabled)
            {
                var gm = UIKit.Btn(hud.transform, "GM", new Color(0.2f, 0.22f, 0.32f), ShowGM, 36);
                var rt = (RectTransform)gm.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-90, -373);
                rt.sizeDelta = new Vector2(120, 64);
            }
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                boosterBtns[i] = view.boosterButtons[i];
                boosterCounts[i] = view.boosterCounts[i];
                boosterBtns[i].onClick.AddListener(() => { Sfx.Play("button"); OnBooster((Booster)index); });
            }
            hud.SetActive(false);
        }

        void OnBooster(Booster b)
        {
            var d = SaveSystem.D;
            int i = (int)b;
            if (!game.CanUseBooster(b))
            {
                ShowTip(b == Booster.Reveal ? "No mystery car right now" : "A car is moving, try again in a moment", 1.5f);
                return;
            }
            if (d.boosters[i] <= 0)
            {
                ShowBuyBooster(b);
                return;
            }
            d.boosters[i]--;
            SaveSystem.Save();
            game.UseBooster(b);
            ShowTip(Economy.BoosterDesc[i], 1.6f);
        }

        // ===================================================================
        // 弹窗
        // ===================================================================
        void CloseModal()
        {
            if (modal != null)
            {
                modal.SetActive(false);
                Destroy(modal);
            }
            modal = null;
            if (game != null) game.Paused = false;
        }

        PopupView OpenPopup(string title, string message, Sprite icon, string amount,
            string label, System.Action action, System.Action close, int? price = null,
            PopupTemplate template = PopupTemplate.Y1)
        {
            var popup = PopupView.Create(canvasRect, title, message, icon, amount, label, action, close,
                price, SaveSystem.D.coins, template);
            if (!popup) return null;
            CloseModal();
            modal = popup.gameObject;
            game.Paused = true;
            return popup;
        }

        void AdoptPopup(PopupView popup)
        {
            if (!popup) return;
            CloseModal();
            modal = popup.gameObject;
            game.Paused = true;
        }

        RectTransform OpenModal(string title, Color head, float height, bool transparentCard = false, bool dismissible = true)
        {
            CloseModal();
            var card = UIKit.Modal(canvasRect, "Modal", height, out modal);
            if (transparentCard)
                card.GetComponent<Image>().enabled = false;
            else
            {
                UIKit.Title(card, title, head);
                if (dismissible)
                {
                    var close = UIKit.Btn(card, "×", Color.white, CloseModal, 64);
                    UIKit.Paint(close, "home_btn_ pause (1)");
                    UIKit.Place((RectTransform)close.transform, Vector2.one, new Vector2(-62, -42), new Vector2(108, 108));
                }
            }
            return card;
        }

        Text Body(RectTransform card, string text, float y, int size = 46, float h = 200)
        {
            var background = UIKit.Img(card, "Message", Color.white, UIKit.Art("Rectangle 630"));
            background.raycastTarget = false;
            UIKit.Place(background.rectTransform, new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(792, h + 24));
            var t = UIKit.Txt(background.transform, text, size, new Color(.37f, .15f, .07f));
            UIKit.Stretch(t.rectTransform);
            return t;
        }

        Button ModalBtn(RectTransform card, string label, Color c, System.Action a, float y, float w = 620)
        {
            var b = UIKit.Btn(card, label, c, a, 52);
            var skin = SkinConfig.Instance;
            UIKit.Paint(b, c == skin.dangerButton ? "common_btn_green (2)" : c == skin.primaryButton ? "common_btn_green (1)" : "common_btn_yellow");
            UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 0), new Vector2(0, y), new Vector2(w, 140));
            return b;
        }

        void ShowPause()
        {
            if (game.Ended) return;
            var d = SaveSystem.D;
            var card = OpenModal("Setting", new Color(0.35f, 0.45f, 0.75f), 1500);
            game.Paused = true;

            var band = UIKit.Img(card, "AudioBand", Color.white, UIKit.Art("Rectangle 631"));
            UIKit.Place(band.rectTransform, new Vector2(.5f, .5f), new Vector2(0, 285), new Vector2(792, 260));
            var music = UIKit.Btn(card, "", Color.white, () => TogglePauseSetting(0));
            var sound = UIKit.Btn(card, "", Color.white, () => TogglePauseSetting(1));
            var vibration = UIKit.Btn(card, "", Color.white, () => TogglePauseSetting(2));
            UIKit.Paint(music, "setting_btn_bgm");
            UIKit.Paint(sound, "setting_btn_sound");
            UIKit.Paint(vibration, "setting_btn_shock");
            music.image.color = d.music ? Color.white : Color.gray;
            sound.image.color = d.sound ? Color.white : Color.gray;
            vibration.image.color = d.vibrate ? Color.white : Color.gray;
            UIKit.Place((RectTransform)music.transform, new Vector2(.5f, .5f), new Vector2(-250, 285), new Vector2(176, 176));
            UIKit.Place((RectTransform)sound.transform, new Vector2(.5f, .5f), new Vector2(0, 285), new Vector2(176, 176));
            UIKit.Place((RectTransform)vibration.transform, new Vector2(.5f, .5f), new Vector2(250, 285), new Vector2(176, 176));
            ModalBtn(card, "Contact us", SkinConfig.Instance.secondaryButton, AppLinks.OpenSupportMail, 760);

            var language = ModalBtn(card, string.IsNullOrEmpty(d.language) ? "English" : d.language, SkinConfig.Instance.primaryButton, null, 500, 624);
            var languageIcon = UIKit.Img(language.transform, "LanguageIcon", Color.white, UIKit.Art("icon_language"), false);
            UIKit.Place(languageIcon.rectTransform, new Vector2(.5f, .5f), new Vector2(-218, 0), new Vector2(70, 74));
            var languageArrow = UIKit.Img(language.transform, "LanguageArrow", Color.white, UIKit.Art("setting_btn_Down"), false);
            UIKit.Place(languageArrow.rectTransform, new Vector2(1, .5f), new Vector2(-80, 0), new Vector2(58, 48));
            var menu = UIKit.Img(card, "LanguageMenu", Color.white, UIKit.Art("Rectangle 630"));
            UIKit.Place(menu.rectTransform, new Vector2(.5f, .5f), new Vector2(0, -485), new Vector2(520, 430));
            menu.gameObject.SetActive(false);
            var languageNames = new[] { "English", "Français", "Deutsch", "Italiano", "Japanese" };
            for (int i = 0; i < languageNames.Length; i++)
            {
                int index = i;
                var option = UIKit.Btn(menu.transform, languageNames[i], new Color(1, 1, 1, .001f), () =>
                {
                    d.language = languageNames[index];
                    SaveSystem.Save();
                    ShowPause();
                }, 30);
                UIKit.Label(option).color = new Color(.25f, .25f, .35f);
                UIKit.Place((RectTransform)option.transform, new Vector2(.5f, .5f), new Vector2(0, 155 - i * 78), new Vector2(440, 68));
            }
            language.onClick.AddListener(() => menu.gameObject.SetActive(!menu.gameObject.activeSelf));

            var meta = UIKit.Txt(card, $"UID:{SystemInfo.deviceUniqueIdentifier.GetHashCode():X6}\nversion {Application.version}", 34, new Color(.75f, .82f, 1f), TextAnchor.MiddleCenter);
            UIKit.Place(meta.rectTransform, new Vector2(.5f, 0), new Vector2(0, 270), new Vector2(600, 130));
            // 设置面板里的隐私政策文字按钮：跳在线隐私政策页，不再开邮件。
            var privacy = ModalBtn(card, "Privacy Policy", new Color(.45f, .62f, .95f), AppLinks.OpenPrivacyPolicy, 145, 420);
            var leave = ModalBtn(card, "Leave Level", SkinConfig.Instance.dangerButton, () =>
            {
                PushPopupBack(ShowPause);
                ConfirmLeave(() =>
                {
                    SaveSystem.LoseLife();
                    ShowHome();
                });
            }, 145, 420);
            ((RectTransform)privacy.transform).anchoredPosition += Vector2.left * 220;
            ((RectTransform)leave.transform).anchoredPosition += Vector2.right * 220;
        }

        void TogglePauseSetting(int setting)
        {
            if (setting == 0) SaveSystem.D.music = !SaveSystem.D.music;
            else if (setting == 1) SaveSystem.D.sound = !SaveSystem.D.sound;
            else SaveSystem.D.vibrate = !SaveSystem.D.vibrate;
            SaveSystem.Save();
            ShowPause();
        }

        void PushPopupBack(System.Action previous)
        {
            if (previous != null) popupBackStack.Push(previous);
        }

        System.Action PopPopupBack()
        {
            return popupBackStack.Count > 0 ? popupBackStack.Pop() : null;
        }

        void GoBackPopup()
        {
            // 只有真正点击返回按钮时才弹出上一页，避免打开页面时提前消耗栈记录。
            var previous = PopPopupBack();
            if (previous != null) previous();
            else CloseModal();
        }

        void ShowExitPrompt()
        {
            OpenPopup($"Level {currentLevel}", "Level Failed", UIKit.Art("icon_heart_1"), "-1",
                "Quit", () =>
                {
                    PushPopupBack(ShowExitPrompt);
                    ConfirmLeave(() =>
                    {
                        SaveSystem.LoseLife();
                        ShowHome();
                    });
                }, () =>
                {
                    PushPopupBack(ShowExitPrompt);
                    ConfirmLeave(() =>
                    {
                        SaveSystem.LoseLife();
                        ShowHome();
                    });
                }, null, PopupTemplate.Y3);
        }

        void ConfirmLeave(System.Action leave)
        {
            OpenPopup("Are You Sure?", "You will lose 1 life!", null, "",
                "Quit", leave, GoBackPopup, null, PopupTemplate.Y2);
        }

        void OnWin()
        {
            EventTrack.LevelWin(currentLevel);
            EventTrack.UeShow("level_win");
            HideHand();
            int reward = Economy.WinReward(game.Level.difficulty);
            var d = SaveSystem.D;
            if (d.level == currentLevel) d.level = Mathf.Min(currentLevel + 1, LevelRepository.MaxLevel);
            d.bestLevel = Mathf.Max(d.bestLevel, d.level);
            d.coins += reward;
            SaveSystem.Save();

            OpenSettlementModal(reward);
            SdkIntegration.TryShowInterstitial(currentLevel, "level_win");
        }

        void OpenSettlementModal(int reward)
        {
            const string prefabPath = "UI/Modal";
            var prefab = Resources.Load<GameObject>(prefabPath);
            if (!prefab)
            {
                Debug.LogError($"[LoopPark] Missing settlement modal prefab at Resources/{prefabPath}.");
                return;
            }

            CloseModal();
            modal = Instantiate(prefab, canvasRect, false);
            UIKit.Stretch((RectTransform)modal.transform);
            game.Paused = true;

            var view = modal.AddComponent<SettlementModalView>();
            // 双倍按钮标题（"X2 Coin"）由 prefab 里的 Text 决定，这里只灌数值与「继续」文案，
            // 避免长句覆盖掉美术排好的两行布局。
            var continueLabel = currentLevel == LevelRepository.MaxLevel ? "Back to Home" : "Continue";
            if (!view.Bind(reward, continueLabel,
                () =>
                {
                    Ads.Service.ShowRewarded(ok =>
                    {
                        if (ok)
                        {
                            SaveSystem.AddCoins(reward);
                            Sfx.Play("coin"); // 广告双倍到账，同样是「金币增加」
                        }
                        view.SetDoubleButtonInteractable(false);
                        ShowTip(ok ? $"Extra {reward} coins" : "Ad not completed", 1.5f);
                    }, "win_double_coins", currentLevel);
                }, ContinueFromSettlement))
            {
                CloseModal();
                return;
            }

            var card = modal.transform.Find("Card") as RectTransform;
            if (card) FxBurstUI(card);
            // 金币入账提示：等结算卡片的 PopIn（0.25s）落定再响，避免和胜利音效叠成一团。
            StartCoroutine(CoinCueAfterPopIn(0.25f));
        }

        IEnumerator CoinCueAfterPopIn(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Sfx.Play("coin");
        }

        /// <summary>
        /// 过关结算的「继续」：连续闯关区（前 10 关）原地进入下一关，不打断流程回主界面；
        /// 区间外或最后一关则回主界面。下一关取 currentLevel + 1，与 OnWin 里推进的存档进度一致。
        /// </summary>
        void ContinueFromSettlement()
        {
            if (LevelFlow.ShouldContinueInPlace(currentLevel)) StartLevel(currentLevel + 1);
            else ShowHome();
        }

        void FxBurstUI(RectTransform card)
        {
            // 彩带：在世界坐标中喷一些粒子
            var cam = Camera.main;
            for (int i = 0; i < 6; i++)
            {
                var p = cam.ViewportToWorldPoint(new Vector3(Random.Range(0.15f, 0.85f), Random.Range(0.55f, 0.85f), 10));
                FxParticle.Burst(game.transform, p, Palette.Get(Random.Range(0, Palette.Count)), 14, 0.22f, 6f);
            }
        }

        void OnFail()
        {
            HideHand();
            EventTrack.UeShow("continue_slots");
            popupBackStack.Clear();
            PushPopupBack(ShowExitPrompt);
            if (SaveSystem.D.lives <= 0) ShowNoLives(ShowRevive);
            else ShowRevive();
        }

        void ShowRevive()
        {
            PopupView popup = null;
            popup = OpenPopup("Out of Space!", $"Extra {Economy.ContinueSlots} parking lot to keep playing",
                UIKit.Art("icon_add cart"), $"+{Economy.ContinueSlots}", "Play On", Pay,
                GoBackPopup, Economy.ContinueCost, PopupTemplate.Y5);
            if (!popup) return;

            void Pay()
            {
                if (SaveSystem.Spend(Economy.ContinueCost)) Resume();
                else popup.SetMessage("Not enough coins");
            }

            void Resume()
            {
                // 继续游戏后，失败流程已结束，不能把旧页面留给下一次弹窗。
                popupBackStack.Clear();
                CloseModal();
                game.Continue(Economy.ContinueSlots);
            }
        }

        void ShowLose()
        {
            EventTrack.LevelFail(currentLevel, game.Remaining);
            EventTrack.UeShow("level_fail");
            SaveSystem.LoseLife();
            SdkIntegration.TryShowInterstitial(currentLevel, "level_fail");
            var popup = OpenPopup($"Level {currentLevel}", $"Level failed ({SaveSystem.D.lives} stamina left)",
                UIKit.Art("icon_heart_1"), "-1", "Retry", () =>
            {
                if (SaveSystem.D.lives <= 0) ShowHome();
                else StartLevel(currentLevel);
            }, null);
            if (popup) popup.AddSecondary("Back to Home", ShowHome);
        }

        void ShowNoLives(System.Action onRefilled)
        {
            PopupView popup = null;
            popup = OpenPopup("More Lives?", "Get more lives to continue playing", null,
                SaveSystem.D.lives.ToString(), "Refill Lives", () =>
            {
                if (SaveSystem.Spend(Economy.RefillLivesCost))
                {
                    SaveSystem.D.lives = Economy.MaxLives;
                    SaveSystem.Save();
                    onRefilled?.Invoke();
                }
                else popup.SetMessage("Not enough coins");
            }, GoBackPopup, Economy.RefillLivesCost, PopupTemplate.Y4);
            if (!popup) return;

            popup.SetSecondary("Get", () =>
            {
                Ads.Service.ShowRewarded(ok =>
                {
                    if (!popup || !popup.isActiveAndEnabled) return;
                    if (ok)
                    {
                        SaveSystem.D.lives = Mathf.Min(Economy.MaxLives, SaveSystem.D.lives + 1);
                        SaveSystem.Save();
                        onRefilled?.Invoke();
                    }
                    else popup.SetMessage("The ad did not finish, please try again");
                }, "refill_life", currentLevel);
            }, "+1");
        }

        void ShowBuyBooster(Booster b)
        {
            var popup = BoosterPurchasePopup.Open(canvasRect, b, CloseModal);
            AdoptPopup(popup);
        }

        void ShowShop()
        {
            var card = OpenModal("Store", new Color(0.3f, 0.6f, 0.95f), 1150);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var row = UIKit.Img(card, "Row", new Color(1, 1, 1, 0.7f), UIKit.FlatRound);
                UIKit.Place(row.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -260 - i * 230), new Vector2(800, 200));
                var name = UIKit.Txt(row.transform, $"{Economy.BoosterName[i]}  （have {SaveSystem.D.boosters[i]}）", 44, new Color(0.25f, 0.2f, 0.2f), TextAnchor.MiddleLeft);
                UIKit.Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(250, 30), new Vector2(460, 70));
                var desc = UIKit.Txt(row.transform, Economy.BoosterDesc[i], 32, new Color(0.4f, 0.35f, 0.3f), TextAnchor.MiddleLeft);
                UIKit.Place(desc.rectTransform, new Vector2(0, 0.5f), new Vector2(250, -40), new Vector2(460, 60));
                var buy = UIKit.Btn(row.transform, $"{Economy.BoosterPrice[i]}", SkinConfig.Instance.primaryButton, () =>
                {
                    if (SaveSystem.Spend(Economy.BoosterPrice[idx]))
                    {
                        SaveSystem.D.boosters[idx]++;
                        SaveSystem.Save();
                        ShowShop();
                    }
                    else ShowTip("Not enough coins", 1.5f);
                }, 44);
                UIKit.Place((RectTransform)buy.transform, new Vector2(1, 0.5f), new Vector2(-130, 0), new Vector2(220, 120));
            }
            ModalBtn(card, "Close", new Color(0.6f, 0.6f, 0.65f), CloseModal, 90, 420);
        }

        // ===================================================================
        // GM 工具
        // ===================================================================
        void ShowGM()
        {
            bool inGame = hud.activeSelf && game.Level != null;
            var card = OpenModal("GM Tools", new Color(0.85f, 0.35f, 0.2f), 1260);
            if (inGame) game.Paused = true;
            var d = SaveSystem.D;
            var info = UIKit.Txt(card, $"Level {d.level}   Coins {d.coins}   Lives {d.lives}", 36, new Color(0.3f, 0.25f, 0.2f));
            UIKit.Place(info.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -175), new Vector2(820, 60));

            // 跳关
            var input = UIKit.NumberInput(card, $"Level number 1~{LevelRepository.MaxLevel}", 48);
            UIKit.Place((RectTransform)input.transform, new Vector2(0.5f, 1), new Vector2(-150, -280), new Vector2(460, 120));
            input.text = (inGame ? currentLevel : d.level).ToString();
            var go = UIKit.Btn(card, "Go", SkinConfig.Instance.secondaryButton, () =>
            {
                if (!int.TryParse(input.text, out var n) || n < 1 || n > LevelRepository.MaxLevel) { ShowTip("Enter a valid level number", 1.5f); return; }
                SaveSystem.D.level = n;
                SaveSystem.D.bestLevel = Mathf.Max(SaveSystem.D.bestLevel, n);
                SaveSystem.Save();
                StartLevel(n);
            }, 48);
            UIKit.Place((RectTransform)go.transform, new Vector2(0.5f, 1), new Vector2(250, -280), new Vector2(260, 120));

            float y = -430;
            Button Row(string label, Color c, System.Action a)
            {
                var b = UIKit.Btn(card, label, c, a, 46);
                UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(700, 115));
                y -= 135;
                return b;
            }
            var pass = Row("Pass This Level", SkinConfig.Instance.primaryButton, () =>
            {
                CloseModal();
                game.ForceWin();
            });
            pass.interactable = inGame && !game.Ended;
            Row("Coins +1000", new Color(0.95f, 0.65f, 0.15f), () => { SaveSystem.AddCoins(1000); ShowGM(); });
            Row("Boosters +5 Each", new Color(0.6f, 0.4f, 0.9f), () =>
            {
                for (int i = 0; i < SaveSystem.D.boosters.Length; i++) SaveSystem.D.boosters[i] += 5;
                SaveSystem.Save();
                ShowGM();
            });
            Row("Refill Lives", new Color(0.92f, 0.33f, 0.4f), () =>
            {
                SaveSystem.D.lives = Economy.MaxLives;
                SaveSystem.Save();
                ShowGM();
            });
            Row("Reset Tutorial Hints", new Color(0.4f, 0.55f, 0.75f), () =>
            {
                SaveSystem.D.tips.Clear();
                SaveSystem.Save();
                ShowTip("Tutorial hints reset", 1.5f);
            });
            ModalBtn(card, "Close", new Color(0.6f, 0.6f, 0.65f), CloseModal, 70, 420);
        }

        /// <summary>命令行 -lp_gmtest：自动执行 GM 功能并输出日志（测试用）。</summary>
        public System.Collections.IEnumerator GMSelfTest()
        {
            var d = SaveSystem.D;
            Debug.Log($"[GMTEST] start level={d.level} coins={d.coins} boosters={string.Join(",", d.boosters)} lives={d.lives}");
            ShowGM();
            yield return new WaitForSeconds(1.5f);
            SaveSystem.D.level = 30; SaveSystem.Save(); StartLevel(30);
            yield return new WaitForSeconds(2f);
            Debug.Log($"[GMTEST] jumped hudLevel={hudLevel.text} gameLevel={game.Level.id}");
            ShowGM();
            yield return new WaitForSeconds(1.5f);
            CloseModal();
            game.ForceWin();
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[GMTEST] after pass level={SaveSystem.D.level} coins={SaveSystem.D.coins}");
            SaveSystem.AddCoins(1000);
            for (int i = 0; i < SaveSystem.D.boosters.Length; i++) SaveSystem.D.boosters[i] += 5;
            SaveSystem.D.lives = 1; SaveSystem.Save();
            SaveSystem.D.lives = Economy.MaxLives; SaveSystem.Save();
            Debug.Log($"[GMTEST] after add coins={SaveSystem.D.coins} boosters={string.Join(",", SaveSystem.D.boosters)} lives={SaveSystem.D.lives}");
            ShowHome();
        }

        // ===================================================================
        // 提示气泡 + 手指
        // ===================================================================
        void BuildTip()
        {
            var bg = UIKit.Img(canvasRect, "Tip", new Color(1f, 1f, 1f, 0.95f), UIKit.FlatRound);
            tipRoot = bg.rectTransform;
            UIKit.Place(tipRoot, new Vector2(0.5f, 0), new Vector2(0, 340), new Vector2(940, 150));
            bg.raycastTarget = false;
            tipText = UIKit.Txt(tipRoot, "", 44, new Color(0.25f, 0.22f, 0.35f));
            UIKit.Stretch(tipText.rectTransform);
            tipText.rectTransform.offsetMin = new Vector2(30, 10);
            tipText.rectTransform.offsetMax = new Vector2(-30, -10);
            tipRoot.gameObject.SetActive(false);

            var h = UIKit.Img(canvasRect, "Hand", Color.white, SpriteFactory.Hand(), false);
            h.raycastTarget = false;
            hand = h.rectTransform;
            hand.sizeDelta = new Vector2(120, 160);
            hand.pivot = new Vector2(0.42f, 1f);
            handBob = h.gameObject.AddComponent<Bob>();
            hand.gameObject.SetActive(false);
        }

        public void ShowTip(string text, float seconds = 3.5f)
        {
            tipText.text = text;
            tipRoot.gameObject.SetActive(true);
            tipRoot.SetAsLastSibling();
            tipRoot.localScale = Vector3.one;
            tipTimer = seconds;
            tipShownAt = Time.time;
        }

        float tipShownAt;

        public void HideTip()
        {
            tipTimer = 0;
            if (tipRoot) tipRoot.gameObject.SetActive(false);
        }

        public void ShowHandAtWorld(Vector3 world)
        {
            var p = UIKit.WorldToCanvas(canvasRect, world);
            hand.anchorMin = hand.anchorMax = new Vector2(0.5f, 0.5f);
            handBob.basePos = p + new Vector2(0, -20);
            hand.anchoredPosition = handBob.basePos;
            hand.gameObject.SetActive(true);
            hand.SetAsLastSibling();
        }

        public void HideHand()
        {
            if (hand) hand.gameObject.SetActive(false);
        }
    }

    /// <summary>呼吸缩放（主按钮）。</summary>
    public class Breath : MonoBehaviour
    {
        void Update()
        {
            float s = 1 + Mathf.Sin(Time.unscaledTime * 3f) * 0.03f;
            transform.localScale = new Vector3(s, s, 1);
        }
    }
}
