using System;
using System.Collections.Generic;
using UnityEngine;

namespace LoopPark
{
    public enum Booster { ExtraSlot = 0, Shuffle = 1, Reveal = 2 }

    [Serializable]
    public class PlayerData
    {
        public int level = 1;
        public int coins = 300;
        public int lives = Economy.MaxLives;
        public long lifeStamp;
        public int[] boosters = { 2, 2, 2 };
        public bool sound = true;
        public bool music = true;
        public bool vibrate = true;
        public string language = "English";
        public List<string> tips = new List<string>();
        public int bestLevel = 1;
    }

    /// <summary>数值配置（集中在这里方便调整）。</summary>
    public static class Economy
    {
        public const int MaxLives = 5;
        public const int LifeRegenSeconds = 60 * 60;
        public const int ContinueCost = 900;     // 失败后 +3 车位继续
        public const int ContinueSlots = 3;
        public const int RefillLivesCost = 900;
        public static readonly int[] BoosterPrice = { 150, 200, 120 };
        public static readonly string[] BoosterName = { "+1 parking space", "mess up", "Perspective" };
        public static readonly string[] BoosterDesc =
        {
            "+1 to this level's loop capacity",
            "Shuffle the colors of all the cars in the parking line",
            "Show the color of the question mark car",
        };

        public static int WinReward(int difficulty) => 30 + difficulty * 20;
    }

    public static class SaveSystem
    {
        const string Key = "looppark_save_v1";
        const string ManualLanguageKey = "looppark_language_manual_v1";
        static PlayerData data;

        public static bool HasManualLanguage => PlayerPrefs.HasKey(ManualLanguageKey);

        public static void MarkManualLanguage() => PlayerPrefs.SetInt(ManualLanguageKey, 1);

        public static PlayerData D
        {
            get
            {
                if (data == null) Load();
                return data;
            }
        }

        public static event Action Changed;

        public static void Load()
        {
            var json = PlayerPrefs.GetString(Key, "");
            data = string.IsNullOrEmpty(json) ? new PlayerData() : JsonUtility.FromJson<PlayerData>(json);
            data.level = Mathf.Clamp(data.level, 1, LevelRepository.MaxLevel);
            data.bestLevel = Mathf.Clamp(data.bestLevel, 1, LevelRepository.MaxLevel);
            if (data.boosters == null || data.boosters.Length < 3) data.boosters = new[] { 2, 2, 2 };
            if (data.tips == null) data.tips = new List<string>();
            TickLives();
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void Reset()
        {
            data = new PlayerData();
            PlayerPrefs.DeleteKey(ManualLanguageKey);
            Save();
        }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>按时间回复体力。</summary>
        public static void TickLives()
        {
            if (data == null) Load();
            var d = data;
            if (d.lives >= Economy.MaxLives) { d.lifeStamp = Now; return; }
            long elapsed = Now - d.lifeStamp;
            if (elapsed < Economy.LifeRegenSeconds) return;
            int gained = (int)(elapsed / Economy.LifeRegenSeconds);
            d.lives = Mathf.Min(Economy.MaxLives, d.lives + gained);
            d.lifeStamp += gained * (long)Economy.LifeRegenSeconds;
            if (d.lives >= Economy.MaxLives) d.lifeStamp = Now;
            Save();
        }

        public static int SecondsToNextLife()
        {
            if (D.lives >= Economy.MaxLives) return 0;
            return (int)Math.Max(0, Economy.LifeRegenSeconds - (Now - D.lifeStamp));
        }

        public static void LoseLife()
        {
            if (D.lives >= Economy.MaxLives) D.lifeStamp = Now;
            D.lives = Mathf.Max(0, D.lives - 1);
            Save();
        }

        public static bool Spend(int coins)
        {
            if (D.coins < coins) return false;
            D.coins -= coins;
            Save();
            return true;
        }

        public static bool TryBuyBooster(Booster booster)
        {
            int index = (int)booster;
            if ((uint)index >= Economy.BoosterPrice.Length) return false;
            var player = D;
            int price = Economy.BoosterPrice[index];
            if (player.coins < price) return false;
            player.coins -= price;
            player.boosters[index]++;
            Save();
            return true;
        }

        public static void AddCoins(int coins)
        {
            D.coins += coins;
            Save();
        }

        public static bool TipShown(string id) => D.tips.Contains(id);

        public static void MarkTip(string id)
        {
            if (!D.tips.Contains(id)) { D.tips.Add(id); Save(); }
        }
    }

    /// <summary>游戏业务只依赖广告结果，不依赖具体 SDK。</summary>
    public interface IAdService
    {
        bool IsRewardedReady { get; }
        void ShowRewarded(Action<bool> onDone, string placement = null, int level = 0);
    }

    public class TestAdService : IAdService
    {
        public bool IsRewardedReady => true;
        public void ShowRewarded(Action<bool> onDone, string placement = null, int level = 0) => onDone?.Invoke(true);
    }

    public static class Ads
    {
        public static IAdService Service { get; private set; } = new TestAdService();

        public static void Install(IAdService service) => Service = service ?? throw new ArgumentNullException(nameof(service));
    }
}
