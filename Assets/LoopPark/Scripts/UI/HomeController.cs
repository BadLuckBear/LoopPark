using UnityEngine;
using UnityEngine.SceneManagement;

namespace LoopPark
{
    public class HomeController : MonoBehaviour
    {
        public HomeView view;

        void Start()
        {
            Sfx.Init(gameObject);
            EventTrack.UeShow("home");
            view.playButton.onClick.AddListener(Play);
            view.settingsButton.onClick.AddListener(() => { EventTrack.UeClick("settings"); Open(view.settingsPanel); });
            view.shopButton.onClick.AddListener(() => { EventTrack.UeClick("shop"); Open(view.shopPanel); });
            view.leaderboardButton.onClick.AddListener(() => { EventTrack.UeClick("leaderboard"); Open(view.leaderboardPanel); });
            view.settingsCloseButton.onClick.AddListener(() => { Sfx.Play("button"); ClosePanels(); });
            view.shopCloseButton.onClick.AddListener(() => { Sfx.Play("button"); ClosePanels(); });
            view.leaderboardCloseButton.onClick.AddListener(() => { Sfx.Play("button"); ClosePanels(); });
            view.soundButton.onClick.AddListener(() => { SaveSystem.D.sound = !SaveSystem.D.sound; SaveSystem.Save(); Refresh(); });
            view.vibrateButton.onClick.AddListener(() => { SaveSystem.D.vibrate = !SaveSystem.D.vibrate; SaveSystem.Save(); Refresh(); });
            view.resetButton.onClick.AddListener(() => Open(view.resetPanel));
            view.confirmResetButton.onClick.AddListener(() => { Sfx.Play("button"); SaveSystem.Reset(); ClosePanels(); Refresh(); });
            view.cancelResetButton.onClick.AddListener(() => { Sfx.Play("button"); ClosePanels(); });
            for (int i = 0; i < view.buyButtons.Length; i++)
            {
                int index = i;
                view.buyButtons[i].onClick.AddListener(() => Buy(index));
            }
            Refresh();
        }

        void Update()
        {
            SaveSystem.TickLives();
            var d = SaveSystem.D;
            int seconds = SaveSystem.SecondsToNextLife();
            view.lives.text = d.lives >= Economy.MaxLives ? "Full" : $"{d.lives}  {seconds / 60:00}:{seconds % 60:00}";
        }

        void Play()
        {
            EventTrack.UeClick("play");
            Sfx.Play("button");
            if (SaveSystem.D.lives <= 0) { Open(view.shopPanel); return; }
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
            view.settingsPanel.SetActive(false);
            view.shopPanel.SetActive(false);
            view.leaderboardPanel.SetActive(false);
            view.resetPanel.SetActive(false);
        }

        void Refresh()
        {
            var d = SaveSystem.D;
            view.coins.text = d.coins.ToString();
            view.level.text = $"Level {d.level}";
            view.bestLevel.text = $"最高关卡  {d.bestLevel}";
            view.sound.text = d.sound ? "音效：开" : "音效：关";
            view.vibrate.text = d.vibrate ? "震动：开" : "震动：关";
            for (int i = 0; i < view.buyLabels.Length; i++)
                view.buyLabels[i].text = $"{Economy.BoosterName[i]}  {Economy.BoosterPrice[i]} 金币  ·  拥有 {d.boosters[i]}";
        }
    }
}
