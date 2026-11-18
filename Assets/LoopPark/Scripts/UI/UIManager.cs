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
        // 弹窗
        GameObject modal;
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
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.2f;
            canvasRect = (RectTransform)go.transform;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
        }

        public void ShowHome()
        {
            CloseModal();
            game.Cleanup();
            tutorial.Stop();
            SceneManager.LoadScene("Home");
        }

        void RefreshBars()
        {
            var d = SaveSystem.D;
            if (hudCoins) hudCoins.text = d.coins.ToString();
            for (int i = 0; i < 3; i++)
                if (boosterCounts[i]) boosterCounts[i].text = d.boosters[i] > 0 ? d.boosters[i].ToString() : "+";
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
            CloseModal();
            HideTip();
            HideHand();
            currentLevel = n;
            var data = LevelRepository.Get(n);
            hud.SetActive(true);
            game.StartLevel(data);
            EventTrack.LevelStart(n, data.difficulty, data.TotalCars());
            EventTrack.UeShow("game");
            hudLevel.text = $"第 {n} 关";
            hudDiffBg.gameObject.SetActive(data.difficulty > 0);
            hudDiff.text = data.difficulty == 2 ? "超难" : "困难";
            hudDiffBg.color = data.difficulty == 2 ? new Color(0.6f, 0.25f, 0.85f) : new Color(0.9f, 0.25f, 0.3f);
            RefreshBars();
            tutorial.OnLevelStart(data);
        }

        // ===================================================================
        // HUD
        // ===================================================================
        void BuildHud()
        {
            var prefab = Resources.Load<GameObject>("UI/GameHUD");
            hud = Instantiate(prefab, canvasRect);
            UIKit.Stretch((RectTransform)hud.transform);
            var view = hud.GetComponent<HudView>();
            hudLevel = view.level;
            hudCoins = view.coins;
            hudDiff = view.difficulty;
            hudDiffBg = view.difficultyBackground;
            view.settingsButton.onClick.AddListener(() => { Sfx.Play("button"); ShowPause(); });
            if (GMEnabled)
            {
                var gm = UIKit.Btn(hud.transform, "GM", new Color(0.2f, 0.22f, 0.32f), ShowGM, 36);
                var rt = (RectTransform)gm.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-90, -80);
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
                ShowTip(b == Booster.Reveal ? "现在没有问号车" : "车辆移动中，稍后再试", 1.5f);
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
            if (modal != null) Destroy(modal);
            modal = null;
            if (game != null) game.Paused = false;
        }

        RectTransform OpenModal(string title, Color head, float height)
        {
            CloseModal();
            var card = UIKit.Modal(canvasRect, "Modal", height, out modal);
            UIKit.Title(card, title, head);
            return card;
        }

        Text Body(RectTransform card, string text, float y, int size = 46, float h = 200)
        {
            var t = UIKit.Txt(card, text, size, new Color(0.3f, 0.25f, 0.2f));
            UIKit.Place(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(780, h));
            return t;
        }

        Button ModalBtn(RectTransform card, string label, Color c, System.Action a, float y, float w = 620)
        {
            var b = UIKit.Btn(card, label, c, a, 52);
            UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 0), new Vector2(0, y), new Vector2(w, 140));
            return b;
        }

        void ShowPause()
        {
            if (game.Ended) return;
            var card = OpenModal("暂停", new Color(0.35f, 0.45f, 0.75f), 860);
            game.Paused = true;
            Body(card, "退出或重新开始会消耗 1 点体力", -210, 40, 120);
            ModalBtn(card, "继续游戏", SkinConfig.Instance.primaryButton, CloseModal, 470);
            ModalBtn(card, "重新开始", SkinConfig.Instance.secondaryButton, () =>
            {
                SaveSystem.LoseLife();
                if (SaveSystem.D.lives <= 0) { ShowHome(); ShowNoLives(); }
                else StartLevel(currentLevel);
            }, 300);
            ModalBtn(card, "回到主页", SkinConfig.Instance.dangerButton, () =>
            {
                SaveSystem.LoseLife();
                ShowHome();
            }, 130);
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

            var card = OpenModal(currentLevel == LevelRepository.MaxLevel ? "全部通关！" : "关卡完成！", new Color(0.98f, 0.66f, 0.15f), 820);
            var coin = UIKit.Img(card, "Coin", new Color(1f, 0.8f, 0.2f), SpriteFactory.Circle(), false);
            UIKit.Place(coin.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -280), new Vector2(170, 170));
            coin.gameObject.AddComponent<Breath>();
            var ct = UIKit.Txt(coin.transform, "$", 90, Color.white, TextAnchor.MiddleCenter, true);
            UIKit.Stretch(ct.rectTransform);
            Body(card, $"+{reward} 金币", -420, 64, 100);
            Button doubleBtn = null;
            doubleBtn = ModalBtn(card, $"双倍领取 +{reward}（广告）", SkinConfig.Instance.secondaryButton, () =>
            {
                Ads.Service.ShowRewarded(ok =>
                {
                    if (ok) SaveSystem.AddCoins(reward);
                    doubleBtn.interactable = false;
                    ShowTip(ok ? $"额外获得 {reward} 金币" : "广告未完成", 1.5f);
                }, "win_double_coins", currentLevel);
            }, 280);
            ModalBtn(card, currentLevel == LevelRepository.MaxLevel ? "返回主页" : "继续", SkinConfig.Instance.primaryButton, () =>
            {
                ShowHome();
            }, 110);
            FxBurstUI(card);
            SdkIntegration.TryShowInterstitial(currentLevel, "level_win");
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
            if (game.LoopCount < game.LoopLimit) { OnDeadlock(); return; }
            EventTrack.UeShow("continue_slots");
            var card = OpenModal("车位不够了！", SkinConfig.Instance.dangerButton, 980);
            game.Paused = true;
            // 示意图：+3
            var plus = UIKit.Txt(card, $"+{Economy.ContinueSlots}", 120, new Color(0.95f, 0.55f, 0.1f), TextAnchor.MiddleCenter, true);
            UIKit.Place(plus.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -270), new Vector2(400, 160));
            Body(card, $"环路扩容 {Economy.ContinueSlots} 个车位，继续游戏", -400, 44, 100);
            var payBtn = ModalBtn(card, $"继续（{Economy.ContinueCost} 金币）", SkinConfig.Instance.primaryButton, () =>
            {
                if (SaveSystem.Spend(Economy.ContinueCost))
                {
                    CloseModal();
                    game.Continue(Economy.ContinueSlots);
                }
                else ShowTip("金币不足", 1.5f);
            }, 470);
            payBtn.interactable = SaveSystem.D.coins >= Economy.ContinueCost;
            ModalBtn(card, "看广告继续", SkinConfig.Instance.secondaryButton, () =>
            {
                Ads.Service.ShowRewarded(ok =>
                {
                    if (!ok) return;
                    CloseModal();
                    game.Continue(Economy.ContinueSlots);
                }, "continue_slots", currentLevel);
            }, 300);
            ModalBtn(card, "放弃", new Color(0.6f, 0.6f, 0.65f), ShowLose, 130, 420);
        }

        /// <summary>环路没满但已无路可走（车道发完、各列互相卡住）：提供洗牌继续。</summary>
        void OnDeadlock()
        {
            EventTrack.UeShow("deadlock_shuffle");
            var card = OpenModal("无路可走了！", SkinConfig.Instance.dangerButton, 980);
            game.Paused = true;
            var icon = UIKit.Txt(card, "?", 120, new Color(0.3f, 0.6f, 0.95f), TextAnchor.MiddleCenter, true);
            UIKit.Place(icon.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -270), new Vector2(400, 160));
            Body(card, "把停车列里的车按颜色整理好，继续游戏", -400, 44, 100);
            int cost = Economy.BoosterPrice[(int)Booster.Shuffle];
            var payBtn = ModalBtn(card, $"整理继续（{cost} 金币）", SkinConfig.Instance.primaryButton, () =>
            {
                if (SaveSystem.Spend(cost)) ShuffleContinue();
                else ShowTip("金币不足", 1.5f);
            }, 470);
            payBtn.interactable = SaveSystem.D.coins >= cost;
            ModalBtn(card, "看广告整理", SkinConfig.Instance.secondaryButton, () =>
            {
                Ads.Service.ShowRewarded(ok => { if (ok) ShuffleContinue(); }, "deadlock_shuffle", currentLevel);
            }, 300);
            ModalBtn(card, "放弃", new Color(0.6f, 0.6f, 0.65f), ShowLose, 130, 420);
        }

        void ShuffleContinue()
        {
            CloseModal();
            game.Continue(0);
            game.RescueSort();
        }

        void ShowLose()
        {
            EventTrack.LevelFail(currentLevel, game.Remaining);
            EventTrack.UeShow("level_fail");
            SaveSystem.LoseLife();
            SdkIntegration.TryShowInterstitial(currentLevel, "level_fail");
            var card = OpenModal("关卡失败", new Color(0.5f, 0.5f, 0.6f), 700);
            game.Paused = true;
            Body(card, $"体力 -1（剩余 {SaveSystem.D.lives}）", -230, 46, 100);
            ModalBtn(card, "重试", SkinConfig.Instance.primaryButton, () =>
            {
                if (SaveSystem.D.lives <= 0) { ShowHome(); ShowNoLives(); }
                else StartLevel(currentLevel);
            }, 270);
            ModalBtn(card, "回到主页", SkinConfig.Instance.secondaryButton, ShowHome, 100);
        }

        void ShowNoLives()
        {
            var card = OpenModal("体力不足", SkinConfig.Instance.dangerButton, 760);
            int sec = SaveSystem.SecondsToNextLife();
            Body(card, $"{sec / 60} 分钟后恢复 1 点体力\n也可以立即补满", -250, 44, 160);
            ModalBtn(card, $"补满体力（{Economy.RefillLivesCost} 金币）", SkinConfig.Instance.primaryButton, () =>
            {
                if (SaveSystem.Spend(Economy.RefillLivesCost))
                {
                    SaveSystem.D.lives = Economy.MaxLives;
                    SaveSystem.Save();
                    CloseModal();
                }
                else ShowTip("金币不足", 1.5f);
            }, 290);
            ModalBtn(card, "关闭", new Color(0.6f, 0.6f, 0.65f), CloseModal, 120, 420);
        }

        void ShowBuyBooster(Booster b)
        {
            int i = (int)b;
            bool inGame = hud.activeSelf;
            var card = OpenModal(Economy.BoosterName[i], new Color(0.3f, 0.6f, 0.95f), 700);
            if (inGame) game.Paused = true;
            Body(card, Economy.BoosterDesc[i], -240, 44, 140);
            ModalBtn(card, $"购买 x1（{Economy.BoosterPrice[i]} 金币）", SkinConfig.Instance.primaryButton, () =>
            {
                if (SaveSystem.Spend(Economy.BoosterPrice[i]))
                {
                    SaveSystem.D.boosters[i]++;
                    SaveSystem.Save();
                    CloseModal();
                }
                else ShowTip("金币不足", 1.5f);
            }, 270);
            ModalBtn(card, "关闭", new Color(0.6f, 0.6f, 0.65f), CloseModal, 100, 420);
        }

        void ShowShop()
        {
            var card = OpenModal("商店", new Color(0.3f, 0.6f, 0.95f), 1150);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var row = UIKit.Img(card, "Row", new Color(1, 1, 1, 0.7f), UIKit.FlatRound);
                UIKit.Place(row.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -260 - i * 230), new Vector2(800, 200));
                var name = UIKit.Txt(row.transform, $"{Economy.BoosterName[i]}  （拥有 {SaveSystem.D.boosters[i]}）", 44, new Color(0.25f, 0.2f, 0.2f), TextAnchor.MiddleLeft);
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
                    else ShowTip("金币不足", 1.5f);
                }, 44);
                UIKit.Place((RectTransform)buy.transform, new Vector2(1, 0.5f), new Vector2(-130, 0), new Vector2(220, 120));
            }
            ModalBtn(card, "关闭", new Color(0.6f, 0.6f, 0.65f), CloseModal, 90, 420);
        }

        // ===================================================================
        // GM 工具
        // ===================================================================
        void ShowGM()
        {
            bool inGame = hud.activeSelf && game.Level != null;
            var card = OpenModal("GM 工具", new Color(0.85f, 0.35f, 0.2f), 1260);
            if (inGame) game.Paused = true;
            var d = SaveSystem.D;
            var info = UIKit.Txt(card, $"当前进度 第 {d.level} 关   金币 {d.coins}   体力 {d.lives}", 36, new Color(0.3f, 0.25f, 0.2f));
            UIKit.Place(info.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -175), new Vector2(820, 60));

            // 跳关
            var input = UIKit.NumberInput(card, $"关卡号 1~{LevelRepository.MaxLevel}", 48);
            UIKit.Place((RectTransform)input.transform, new Vector2(0.5f, 1), new Vector2(-150, -280), new Vector2(460, 120));
            input.text = (inGame ? currentLevel : d.level).ToString();
            var go = UIKit.Btn(card, "跳转", SkinConfig.Instance.secondaryButton, () =>
            {
                if (!int.TryParse(input.text, out var n) || n < 1 || n > LevelRepository.MaxLevel) { ShowTip("请输入正确的关卡号", 1.5f); return; }
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
            var pass = Row("通过本关", SkinConfig.Instance.primaryButton, () =>
            {
                CloseModal();
                game.ForceWin();
            });
            pass.interactable = inGame && !game.Ended;
            Row("金币 +1000", new Color(0.95f, 0.65f, 0.15f), () => { SaveSystem.AddCoins(1000); ShowGM(); });
            Row("道具各 +5", new Color(0.6f, 0.4f, 0.9f), () =>
            {
                for (int i = 0; i < SaveSystem.D.boosters.Length; i++) SaveSystem.D.boosters[i] += 5;
                SaveSystem.Save();
                ShowGM();
            });
            Row("体力补满", new Color(0.92f, 0.33f, 0.4f), () =>
            {
                SaveSystem.D.lives = Economy.MaxLives;
                SaveSystem.Save();
                ShowGM();
            });
            Row("重置引导提示", new Color(0.4f, 0.55f, 0.75f), () =>
            {
                SaveSystem.D.tips.Clear();
                SaveSystem.Save();
                ShowTip("引导提示已重置", 1.5f);
            });
            ModalBtn(card, "关闭", new Color(0.6f, 0.6f, 0.65f), CloseModal, 70, 420);
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

        void ShowSettings()
        {
            var card = OpenModal("设置", new Color(0.4f, 0.45f, 0.6f), 950);
            var d = SaveSystem.D;
            ModalBtn(card, d.sound ? "音效：开" : "音效：关", SkinConfig.Instance.secondaryButton, () =>
            {
                d.sound = !d.sound;
                SaveSystem.Save();
                ShowSettings();
            }, 600);
            ModalBtn(card, d.vibrate ? "震动：开" : "震动：关", SkinConfig.Instance.secondaryButton, () =>
            {
                d.vibrate = !d.vibrate;
                SaveSystem.Save();
                ShowSettings();
            }, 440);
            ModalBtn(card, "重置进度", SkinConfig.Instance.dangerButton, () =>
            {
                var c2 = OpenModal("确认重置？", SkinConfig.Instance.dangerButton, 600);
                Body(c2, "关卡、金币、道具将全部清空", -230, 44, 100);
                ModalBtn(c2, "确认重置", SkinConfig.Instance.dangerButton, () => { SaveSystem.Reset(); ShowHome(); }, 250);
                ModalBtn(c2, "取消", new Color(0.6f, 0.6f, 0.65f), CloseModal, 90, 420);
            }, 280);
            ModalBtn(card, "关闭", new Color(0.6f, 0.6f, 0.65f), CloseModal, 110, 420);
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
