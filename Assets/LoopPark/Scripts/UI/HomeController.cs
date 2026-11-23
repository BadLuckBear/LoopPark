using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LoopPark
{
    public class HomeController : MonoBehaviour
    {
        public HomeView view;
        PopupView livesPopup;

        static readonly string[] Languages =
        {
            "English", "Chinese (Traditional)", "Japanese", "French", "Korean", "German", "Italian",
            "Portuguese", "Spanish", "Arabic", "Indonesian", "Russian", "Chinese (Simplified)"
        };

        void Start()
        {
            Sfx.Init(gameObject);
            EventTrack.UeShow("home");
            view.playButton.onClick.AddListener(Play);
            if (view.lifeAddButton) view.lifeAddButton.onClick.AddListener(OpenLives);
            if (view.livesCloseButton) view.livesCloseButton.onClick.AddListener(ClosePanels);
            if (view.refillButton) view.refillButton.onClick.AddListener(Refill);
            view.settingsButton.onClick.AddListener(() => { EventTrack.UeClick("settings"); Open(view.settingsPanel); });
            view.shopButton.onClick.AddListener(() => { EventTrack.UeClick("shop"); Open(view.shopPanel); });
            view.settingsCloseButton.onClick.AddListener(() => { Sfx.Play("button"); ClosePanels(); });
            view.shopCloseButton.onClick.AddListener(() => { Sfx.Play("button"); ClosePanels(); });
            view.soundButton.onClick.AddListener(() => { SaveSystem.D.sound = !SaveSystem.D.sound; SaveSystem.Save(); Refresh(); });
            if (view.musicButton) view.musicButton.onClick.AddListener(() => { SaveSystem.D.music = !SaveSystem.D.music; SaveSystem.Save(); Refresh(); });
            view.vibrateButton.onClick.AddListener(() => { SaveSystem.D.vibrate = !SaveSystem.D.vibrate; SaveSystem.Save(); Refresh(); });
            if (view.languageButton) view.languageButton.onClick.AddListener(ToggleLanguageMenu);
            if (view.englishButton) view.englishButton.onClick.AddListener(() => SetLanguage("English"));
            if (view.chineseButton) view.chineseButton.onClick.AddListener(() => SetLanguage("Chinese (Simplified)"));
            if (view.languageOptions != null)
            {
                for (int i = 0; i < view.languageOptions.Length && i < Languages.Length; i++)
                {
                    int index = i;
                    if (view.languageOptions[i]) view.languageOptions[i].onClick.AddListener(() => SetLanguage(Languages[index]));
                }
            }
            if (view.contactButton) view.contactButton.onClick.AddListener(AppLinks.OpenSupportMail);
            // 隐私政策按钮：跳转在线网页（https://ycarm.com/privacy/），供商店审核与玩家查阅。
            if (view.privacyButton) view.privacyButton.onClick.AddListener(AppLinks.OpenPrivacyPolicy);
            for (int i = 0; i < view.buyButtons.Length; i++)
            {
                int index = i;
                view.buyButtons[i].onClick.AddListener(() => Buy(index));
            }
            SyncSystemLanguage();
            Refresh();
            if (SaveSystem.D.lives == 0) OpenLives();
            InvokeRepeating(nameof(RefreshLives), 0, 1);
        }

        void RefreshLives()
        {
            SaveSystem.TickLives();
            var d = SaveSystem.D;
            int seconds = SaveSystem.SecondsToNextLife();
            string time = d.lives >= Economy.MaxLives ? "Full" : $"{seconds / 60:00}:{seconds % 60:00}";
            view.lives.text = view.lifeTimer || d.lives < Economy.MaxLives ? d.lives.ToString() : time;
            if (view.lifeTimer) view.lifeTimer.text = time;
            else if (d.lives < Economy.MaxLives) view.lives.text += "  " + time;
            if (view.refillButton) view.refillButton.interactable = d.lives < Economy.MaxLives;
        }

        void OpenLives()
        {
            ClosePanels();
            var canvas = view.GetComponentInParent<Canvas>();
            if (!canvas) return;

            PopupView popup = null;
            popup = PopupView.Create((RectTransform)canvas.transform, "More Lives?", "Get more lives to continue playing",
                null, SaveSystem.D.lives.ToString(), "Refill Lives", () =>
                {
                    if (SaveSystem.Spend(Economy.RefillLivesCost))
                    {
                        SaveSystem.D.lives = Economy.MaxLives;
                        SaveSystem.Save();
                        CloseLivesPopup();
                        Refresh();
                    }
                    else popup.SetMessage("Not enough coins");
                }, CloseLivesPopup, Economy.RefillLivesCost, SaveSystem.D.coins, PopupTemplate.Y4);
            if (!popup) return;
            livesPopup = popup;
            popup.SetSecondary("Get", () => Ads.Service.ShowRewarded(ok =>
            {
                if (!popup || !popup.isActiveAndEnabled) return;
                if (ok)
                {
                    SaveSystem.D.lives = Mathf.Min(Economy.MaxLives, SaveSystem.D.lives + 1);
                    SaveSystem.Save();
                    CloseLivesPopup();
                    Refresh();
                }
                else popup.SetMessage("The ad did not finish, please try again");
            }, "refill_life"), "+1");
        }

        void CloseLivesPopup()
        {
            if (!livesPopup) return;
            Destroy(livesPopup.gameObject);
            livesPopup = null;
        }

        void Refill()
        {
            Sfx.Play("button");
            if (SaveSystem.D.lives >= Economy.MaxLives) return;
            if (!SaveSystem.Spend(Economy.RefillLivesCost)) { view.refillMessage.text = "Not enough coins"; return; }
            SaveSystem.D.lives = Economy.MaxLives;
            SaveSystem.Save();
            ClosePanels();
            Refresh();
        }

        void Play()
        {
            EventTrack.UeClick("play");
            Sfx.Play("button");
            if (SaveSystem.D.lives <= 0) { OpenLives(); return; }
            SceneFlow.LevelToPlay = SaveSystem.D.level;
            SceneManager.LoadScene("Game");
        }

        void Buy(int index)
        {
            Sfx.Play("button");
            if (!SaveSystem.Spend(Economy.BoosterPrice[index])) return;
            SaveSystem.D.boosters[index]++;
            SaveSystem.Save();
            Refresh();
        }

        void Open(GameObject panel)
        {
            Sfx.Play("button");
            ClosePanels();
            panel.SetActive(true);
        }

        void ClosePanels()
        {
            CloseLivesPopup();
            if (view.languageMenu) view.languageMenu.SetActive(false);
            view.settingsPanel.SetActive(false);
            view.shopPanel.SetActive(false);
            if (view.livesPanel) view.livesPanel.SetActive(false);
        }

        void Refresh()
        {
            var d = SaveSystem.D;
            view.coins.text = d.coins.ToString();
            view.level.text = $"Level {d.level}";
            if (view.difficulty && view.playStyles is { Length: 3 })
            {
                int difficulty = Mathf.Clamp(LevelRepository.Get(d.level).difficulty, 0, 2);
                view.playButton.image.sprite = view.playStyles[difficulty];
                view.difficulty.text = difficulty == 2 ? "SUPER HARD" : difficulty == 1 ? "HARD" : "";
            }
            RefreshLives();
            if (view.sound) view.sound.text = d.sound ? "Sound: on" : "Sound: off";
            if (view.music) view.music.text = d.music ? "Music: on" : "Music: off";
            if (view.vibrate) view.vibrate.text = d.vibrate ? "Vibration: on" : "Vibration: off";
            if (view.language) view.language.text = string.IsNullOrEmpty(d.language) ? "English" : d.language;
            view.RefreshLanguageOptions(d.language);
            if (view.musicButton) view.musicButton.image.color = d.music ? Color.white : new Color(.55f, .55f, .55f, 1f);
            if (view.soundButton) view.soundButton.image.color = d.sound ? Color.white : new Color(.55f, .55f, .55f, 1f);
            if (view.vibrateButton) view.vibrateButton.image.color = d.vibrate ? Color.white : new Color(.55f, .55f, .55f, 1f);
            for (int i = 0; i < view.buyLabels.Length; i++)
                view.buyLabels[i].text = $"{Economy.BoosterName[i]}  {Economy.BoosterPrice[i]} coins  ·  owned {d.boosters[i]}";
        }

        void ToggleLanguageMenu()
        {
            if (!view.languageMenu) return;
            bool open = !view.languageMenu.activeSelf;
            view.languageMenu.SetActive(open);
            if (!open) return;

            var scroll = view.languageMenu.GetComponent<ScrollRect>();
            if (!scroll || scroll.viewport == null || scroll.content == null) return;
            Canvas.ForceUpdateCanvases();
            scroll.StopMovement();

            string selected = string.IsNullOrEmpty(SaveSystem.D.language) ? "English" : SaveSystem.D.language;
            int index = System.Array.IndexOf(Languages, selected);
            if (index < 0 || index >= view.languageOptions.Length || !view.languageOptions[index]) return;

            var optionBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                scroll.viewport, view.languageOptions[index].transform);
            float maxOffset = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
            float targetOffset = scroll.content.anchoredPosition.y + scroll.viewport.rect.center.y - optionBounds.center.y;
            scroll.content.anchoredPosition = new Vector2(
                scroll.content.anchoredPosition.x, Mathf.Clamp(targetOffset, 0, maxOffset));
        }

        void SetLanguage(string value)
        {
            SaveSystem.D.language = value;
            SaveSystem.MarkManualLanguage();
            SaveSystem.Save();
            if (view.languageMenu) view.languageMenu.SetActive(false);
            Refresh();
        }

        void SyncSystemLanguage()
        {
            if (SaveSystem.HasManualLanguage) return;
            SaveSystem.D.language = SystemLanguageToLanguage(Application.systemLanguage);
            SaveSystem.Save();
        }

        static string SystemLanguageToLanguage(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.ChineseTraditional: return "Chinese (Traditional)";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified: return "Chinese (Simplified)";
                case SystemLanguage.Japanese: return "Japanese";
                case SystemLanguage.French: return "French";
                case SystemLanguage.Korean: return "Korean";
                case SystemLanguage.German: return "German";
                case SystemLanguage.Italian: return "Italian";
                case SystemLanguage.Portuguese: return "Portuguese";
                case SystemLanguage.Spanish: return "Spanish";
                case SystemLanguage.Arabic: return "Arabic";
                case SystemLanguage.Indonesian: return "Indonesian";
                case SystemLanguage.Russian: return "Russian";
                default: return "English";
            }
        }
    }
}

