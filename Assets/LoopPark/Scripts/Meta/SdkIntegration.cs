using System;
using System.Collections.Generic;
using Game.Sdk;
using UnityEngine;

namespace LoopPark
{
    public static class SdkIntegration
    {
        static bool initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            Sdk.Install(new AndroidSdkPlatform());
#else
            Sdk.Install(new NullSdkPlatform());
#endif
            SdkNativeStarter.Start(Debug.isDebugBuild);
            Ads.Install(new SdkAdService());
            Sdk.Ad.Init(new SdkAdSlots
            {
                // The supplied ca-app-pub IDs are AdMob IDs. SYSDK needs MAX IDs here.
                RewardId = "",
                InterstitialId = ""
            });
            Sdk.Ad.OnAdDisplayed += e =>
            {
                if (e.Format == SdkAdFormat.Reward || e.Format == SdkAdFormat.Interstitial)
                    EventTrack.AdRevenue(e.Format.ToString(), e.MediationName, e.Revenue);
            };
        }

        public static void TryShowInterstitial(int completedLevel, string placement)
        {
            if (completedLevel < 8) return;
            if (!Sdk.Ad.IsInterstitialReady())
            {
                Sdk.Ad.Load(SdkAdFormat.Interstitial);
                EventTrack.Ad(completedLevel, placement, "unavailable");
                return;
            }

            EventTrack.Ad(completedLevel, placement, "requested");
            Sdk.Ad.ShowInterstitial(e => EventTrack.Ad(completedLevel, placement, e.Success ? "closed" : "failed"));
        }

        sealed class SdkAdService : IAdService
        {
            public bool IsRewardedReady => Sdk.Ad.IsRewardReady();

            public void ShowRewarded(Action<bool> onDone, string placement = null, int level = 0)
            {
                if (!IsRewardedReady)
                {
                    Sdk.Ad.Load(SdkAdFormat.Reward);
                    onDone?.Invoke(false);
                    EventTrack.Ad(level, placement, "unavailable");
                    return;
                }

                EventTrack.Ad(level, placement, "requested");
                Sdk.Ad.ShowReward(e =>
                {
                    EventTrack.Ad(level, placement, e.Success ? "rewarded" : "closed");
                    onDone?.Invoke(e.Success);
                });
            }
        }
    }

    public static class EventTrack
    {
        static float levelStartTime;
        static int levelDifficulty;
        static int shots;
        static bool firstClear;
        static int totalCars;
        static string adPlacement;
        [Serializable]
        sealed class EventData
        {
            public int level;
            public string placement;
            public string state;
            public string network;
            public double revenue;
        }

        static void Send(string name, int level, string placement = null, string state = null)
        {
            SdkIntegration.Initialize();
            Sdk.Analytics.LogEvent(name, JsonUtility.ToJson(new EventData
            {
                level = level,
                placement = placement,
                state = state
            }));
        }

        public static void LevelStart(int level, int difficulty, int cars)
        {
            levelStartTime = Time.time;
            levelDifficulty = difficulty;
            shots = 0;
            firstClear = false;
            totalCars = cars;
            Send("level_start", level);
            EventTrackingManager.Track("lv", new Dictionary<string, object>
            {
                ["lv_id"] = level, ["lv_type"] = LevelType, ["lv_state"] = "show"
            });
        }

        static string LevelType => levelDifficulty == 2 ? "sphard" : levelDifficulty == 1 ? "hard" : "easy";

        public static void GameEvent(string id)
        {
            if (id == "tap" && ++shots == 1)
                LevelState("first_click");
            if (id == "clear" && !firstClear)
            {
                firstClear = true;
                LevelState("first_combine", new Dictionary<string, object> { ["lv_firstcombo"] = shots });
            }
        }

        static void LevelState(string state, Dictionary<string, object> extra = null)
        {
            var data = extra ?? new Dictionary<string, object>();
            data["lv_type"] = LevelType;
            data["lv_state"] = state;
            EventTrackingManager.Track("lv", data);
        }

        public static void UeShow(string page) => EventTrackingManager.Track("ue", new Dictionary<string, object> { ["ue_show"] = page });
        public static void UeClick(string action) => EventTrackingManager.Track("ue", new Dictionary<string, object> { ["ue_click"] = action });
        public static void GuideOperate(string guide) => EventTrackingManager.Track("guide", new Dictionary<string, object> { ["guide_operate"] = guide });

        public static void LevelWin(int level)
        {
            Send("level_complete", level);
            EventTrackingManager.Track("lv", new Dictionary<string, object>
            {
                ["lv_id"] = level, ["lv_type"] = LevelType, ["lv_state"] = "win",
                ["lv_time"] = Mathf.RoundToInt(Time.time - levelStartTime), ["lv_pace"] = shots,
                ["lv_progress"] = "100%"
            });
        }

        public static void LevelFail(int level, int remaining)
        {
            Send("level_fail", level);
            EventTrackingManager.Track("lv", new Dictionary<string, object>
            {
                ["lv_id"] = level, ["lv_type"] = LevelType, ["lv_state"] = "fail",
                ["lv_time"] = Mathf.RoundToInt(Time.time - levelStartTime), ["lv_pace"] = shots,
                ["lv_progress"] = (totalCars > 0 ? Mathf.Clamp((totalCars - remaining) * 100 / totalCars, 0, 100) : 0) + "%",
                ["lv_losereason"] = "dead"
            });
        }

        public static void Ad(int level, string placement, string state)
        {
            Send("ad_event", level, placement, state);
            if (state == "requested")
            {
                adPlacement = placement;
                string reward = placement == "win_double_coins" ? "multiple"
                    : placement == "continue_slots" ? "revive" : null;
                if (reward != null)
                    EventTrackingManager.Track("ue", new Dictionary<string, object> { ["ue_reward"] = reward, ["lv_id"] = level });
                return;
            }
            EventTrackingManager.Track("ad", new Dictionary<string, object>
            {
                ["lv_id"] = level, ["ad_type"] = placement == "level_win" || placement == "level_fail" ? "inter" : "reward",
                ["ad_placement"] = placement,
                ["ad_state"] = state == "unavailable" || state == "failed" ? "noshow" : "showover"
            });
        }
        public static void AdRevenue(string format, string network, double revenue)
        {
            Sdk.Analytics.LogEvent("ad_revenue", JsonUtility.ToJson(new EventData
            {
                level = SaveSystem.D.level,
                placement = format,
                network = network,
                revenue = revenue
            }));
            EventTrackingManager.Track("ad", new Dictionary<string, object>
            {
                ["ad_type"] = format == "Interstitial" ? "inter" : "reward",
                ["ad_placement"] = adPlacement, ["ad_network"] = network,
                ["ad_revenue"] = revenue, ["ad_state"] = "revenue"
            });
        }
    }
}
