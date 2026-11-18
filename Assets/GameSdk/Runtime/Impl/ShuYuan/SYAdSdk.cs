using System;
using ShuYuan.SYSDK;
using UnityEngine;

namespace Game.Sdk.Impl.ShuYuan
{
    /// <summary>
    /// 中台广告实现。
    ///
    /// 瀑布流 / eCPM 比价由中台后台承担，本类只负责加载 / 展示 / 重载。
    /// 频控、每日上限、关卡门槛等业务判断一律在上层 AdsService，本类不做任何频控。
    /// SDK 回调走 UnitySendMessage，已在主线程，无需线程封送。
    /// </summary>
    internal class SYAdSdk : IAdSdk
    {
        public event Action<SdkAdEventArgs> OnAdEvent;
        public event Action<SdkAdEventArgs> OnAdDisplayed;

        /// <summary>
        /// 加载失败后的重试间隔。广告 SDK 常规做法：填充失败后延迟续拉。
        /// </summary>
        private const float RetryDelaySeconds = 5f;

        /// <summary>
        /// 单条广告位的最大续拉次数（× RetryDelaySeconds ≈ 100s），
        /// 足以覆盖原生 SDK 初始化耗时；超出仍未就绪说明配置有问题，停止空转。
        /// </summary>
        private const int MaxLoadAttempts = 20;

        private SdkAdSlots _slots;

        private bool _displaySubscribed;

        // 每种格式同时只允许一条续拉链，避免 Init 与播完重载叠加成多条
        private bool _rewardPumping;
        private bool _interPumping;
        private bool _bannerPumping;

        private static ShuYuanSDK SDK { get { return ShuYuanSDK.Instance; } }

        private static void Log(string msg)  { Debug.Log("[SYAd] " + msg); }
        private static void Warn(string msg) { Debug.LogWarning("[SYAd] " + msg); }

        public void Init(SdkAdSlots slots)
        {
            _slots = slots ?? new SdkAdSlots();
            Log("Init RV=" + _slots.RewardId
                + " INT=" + _slots.InterstitialId
                + " Banner=" + _slots.BannerId);

            // 全局推送，订阅一次即可；订阅时中台会通知原生层开启推送
            if (SDK != null && !_displaySubscribed)
            {
                _displaySubscribed = true;
                SDK.OnAdDisplaySuccess += OnNativeAdDisplaySuccess;
            }

            LoadReward();
            LoadInterstitial();
            LoadBanner();
        }

        /// <summary>
        /// 中台全局"展示成功"推送（OnAdDisplaySuccess）→ Phase=Shown 的广告事件。
        /// Type 为 Native / OpenAd 等本项目没有的格式时忽略。
        /// </summary>
        private void OnNativeAdDisplaySuccess(SYAdTelemetryData data)
        {
            if (data == null) return;

            SdkAdFormat format;
            switch (data.Type)
            {
                case "Rewarded":     format = SdkAdFormat.Reward;       break;
                case "Interstitial": format = SdkAdFormat.Interstitial; break;
                case "Banner":       format = SdkAdFormat.Banner;       break;
                default:             return;
            }

            var e = new SdkAdEventArgs
            {
                Success       = true,
                Code          = 0,
                Format        = format,
                Phase         = SdkAdPhase.Shown,
                AdUnitId      = data.AdUnitId,
                Revenue       = data.Revenue,
                MediationName = data.MediationName,
            };

            Log("AdDisplaySuccess " + e.Format + " " + e.AdUnitId + " " + e.MediationName);
            if (OnAdDisplayed != null) OnAdDisplayed(e);
            if (OnAdEvent != null) OnAdEvent(e);
        }

        public void Load(SdkAdFormat format, Action<SdkAdEventArgs> onResult = null)
        {
            if (onResult != null)
            {
                string adUnitId = GetAdUnitId(format);
                if (SDK == null || string.IsNullOrEmpty(adUnitId))
                {
                    onResult(Failed(format, adUnitId));
                    return;
                }

                // 直接发一次加载，不走续拉泵（泵在续拉中会忽略新请求，拿不到回调）
                switch (format)
                {
                    case SdkAdFormat.Reward:       IssueLoadReward(onResult);       break;
                    case SdkAdFormat.Interstitial: IssueLoadInterstitial(onResult); break;
                    case SdkAdFormat.Banner:       IssueLoadBanner(onResult);       break;
                }
                return;
            }

            switch (format)
            {
                case SdkAdFormat.Reward:       LoadReward();       break;
                case SdkAdFormat.Interstitial: LoadInterstitial(); break;
                case SdkAdFormat.Banner:       LoadBanner();       break;
            }
        }

        private string GetAdUnitId(SdkAdFormat format)
        {
            if (_slots == null) return null;
            switch (format)
            {
                case SdkAdFormat.Reward:       return _slots.RewardId;
                case SdkAdFormat.Interstitial: return _slots.InterstitialId;
                case SdkAdFormat.Banner:       return _slots.BannerId;
                default:                       return null;
            }
        }

        // ------------------------------ 激励视频 ------------------------------

        public bool IsRewardReady()
        {
            // 被 AdsService 轮询，这里不打日志以免刷屏
            return SDK != null
                   && _slots != null && !string.IsNullOrEmpty(_slots.RewardId)
                   && SDK.IsRewardedVideoAdReady(_slots.RewardId);
        }

        public void ShowReward(Action<SdkAdEventArgs> onClose, string customData = null)
        {
            if (SDK == null || _slots == null)
            {
                Warn("ShowReward: SDK 未就绪");
                Dispatch(Failed(SdkAdFormat.Reward, _slots == null ? null : _slots.RewardId), onClose);
                return;
            }

            customData = BuildCustomData(customData);
            Log("ShowReward " + _slots.RewardId + " customData=" + customData);
            SDK.ShowRewardedVideoAd(_slots.RewardId, customData, (state, json) =>
            {
                var e = SYEventParser.ToAd(state, json, SdkAdFormat.Reward,
                                           SdkAdPhase.Closed, _slots.RewardId);
                Log("ShowReward 回调 success=" + e.Success + "（true=看完可发奖）");
                Dispatch(e, onClose);
                LoadReward();   // 播完重载下一条
            });
        }

        private void LoadReward()
        {
            if (SDK == null || _slots == null || string.IsNullOrEmpty(_slots.RewardId)) return;
            if (_rewardPumping) return;

            _rewardPumping = true;
            PumpLoad("Reward", IsRewardReady, () => IssueLoadReward(null), v => _rewardPumping = v, 0);
        }

        private void IssueLoadReward(Action<SdkAdEventArgs> onResult)
        {
            SDK.LoadRewardedVideoAd(_slots.RewardId, (state, json) =>
            {
                var e = SYEventParser.ToAd(state, json, SdkAdFormat.Reward,
                                           state ? SdkAdPhase.Loaded : SdkAdPhase.Failed,
                                           _slots.RewardId);
                Dispatch(e, onResult);
            });
        }

        // ------------------------------ 插屏 ------------------------------

        public bool IsInterstitialReady()
        {
            return SDK != null
                   && _slots != null && !string.IsNullOrEmpty(_slots.InterstitialId)
                   && SDK.IsInterstitialAdReady(_slots.InterstitialId);
        }

        public void ShowInterstitial(Action<SdkAdEventArgs> onClose, string customData = null)
        {
            if (SDK == null || _slots == null)
            {
                Warn("ShowInterstitial: SDK 未就绪");
                Dispatch(Failed(SdkAdFormat.Interstitial, _slots == null ? null : _slots.InterstitialId), onClose);
                return;
            }

            customData = BuildCustomData(customData);
            Log("ShowInterstitial " + _slots.InterstitialId + " customData=" + customData);
            SDK.ShowInterstitialAd(_slots.InterstitialId, customData, (state, json) =>
            {
                var e = SYEventParser.ToAd(state, json, SdkAdFormat.Interstitial,
                                           SdkAdPhase.Closed, _slots.InterstitialId);
                Log("ShowInterstitial 回调 success=" + e.Success);
                Dispatch(e, onClose);
                LoadInterstitial();
            });
        }

        private void LoadInterstitial()
        {
            if (SDK == null || _slots == null || string.IsNullOrEmpty(_slots.InterstitialId)) return;
            if (_interPumping) return;

            _interPumping = true;
            PumpLoad("Interstitial", IsInterstitialReady, () => IssueLoadInterstitial(null),
                     v => _interPumping = v, 0);
        }

        private void IssueLoadInterstitial(Action<SdkAdEventArgs> onResult)
        {
            SDK.LoadInterstitialAd(_slots.InterstitialId, (state, json) =>
            {
                var e = SYEventParser.ToAd(state, json, SdkAdFormat.Interstitial,
                                           state ? SdkAdPhase.Loaded : SdkAdPhase.Failed,
                                           _slots.InterstitialId);
                Dispatch(e, onResult);
            });
        }

        // ------------------------------ Banner ------------------------------

        public bool IsBannerReady()
        {
            return SDK != null
                   && _slots != null && !string.IsNullOrEmpty(_slots.BannerId)
                   && SDK.IsBannerAdReady(_slots.BannerId);
        }

        public void ShowBanner(Action<SdkAdEventArgs> onResult = null)
        {
            if (SDK == null || _slots == null)
            {
                Warn("ShowBanner: SDK 未就绪");
                Dispatch(Failed(SdkAdFormat.Banner, _slots == null ? null : _slots.BannerId), onResult);
                return;
            }

            SDK.ShowBannerAd(_slots.BannerId, "portrait:bottom:0", (state, json) =>
            {
                var e = SYEventParser.ToAd(state, json, SdkAdFormat.Banner,
                                           SdkAdPhase.Shown, _slots.BannerId);
                Dispatch(e, onResult);

                if (!state) LoadBanner();   // 展示失败（无填充等）重新拉一条
            });
        }

        public void HideBanner()
        {
            if (SDK == null || _slots == null || string.IsNullOrEmpty(_slots.BannerId)) return;
            SDK.HideBannerAd(_slots.BannerId, (state, json) => { });
        }

        public void CloseBanner()
        {
            if (SDK == null || _slots == null || string.IsNullOrEmpty(_slots.BannerId)) return;
            SDK.CloseBannerAd(_slots.BannerId, (state, json) => { });
        }

        private void LoadBanner()
        {
            if (SDK == null || _slots == null || string.IsNullOrEmpty(_slots.BannerId)) return;
            if (_bannerPumping) return;

            _bannerPumping = true;
            PumpLoad("Banner", IsBannerReady, () => IssueLoadBanner(null), v => _bannerPumping = v, 0);
        }

        private void IssueLoadBanner(Action<SdkAdEventArgs> onResult)
        {
            // Banner 的自定义数据只能在 Load 时透传（ShowBannerAd 只收 position）
            SDK.LoadBannerAd(_slots.BannerId, BuildCustomData(), (state, json) =>
            {
                var e = SYEventParser.ToAd(state, json, SdkAdFormat.Banner,
                                           state ? SdkAdPhase.Loaded : SdkAdPhase.Failed,
                                           _slots.BannerId);
                // 上层可据此在进关时补展示
                Dispatch(e, onResult);
            });
        }

        // ------------------------------ 加载看门狗 ------------------------------

        /// <summary>
        /// 加载续拉泵。
        ///
        /// **为什么不能只靠失败回调重试**：中台 `ShuYuanSDK.Init(bool)` 是 fire-and-forget，
        /// 既没有"初始化完成"回调、也没有就绪查询 API。在原生层就绪之前发起的 Load 会被
        /// **静默丢弃** —— AppLovin 只打一句
        /// `Attempted to load ad before SDK initialization` 警告，**回调根本不会来**，
        /// 于是"失败才重试"的逻辑永远不触发，整个会话的广告全部失效。
        ///
        /// 迁移前 `AdsAdMaxManager.cs:205` 是挂 `MaxSdkCallbacks.OnSdkInitializedEvent`
        /// 等 SDK 初始化完成后才加载的；中台无等价回调，本泵是其等效替代：
        /// 发起 Load 后定时自查，未就绪就再拉一次，就绪即停。
        /// 顺带也覆盖了原本的"无填充失败续拉"，两条路径合并成一个机制。
        /// </summary>
        private void PumpLoad(string what, Func<bool> isReady, Action issueLoad,
                              Action<bool> setPumping, int attempt)
        {
            if (SDK == null) { setPumping(false); return; }

            if (isReady())
            {
                if (attempt > 0) Log(what + " 已就绪（第 " + attempt + " 次续拉后）");
                setPumping(false);
                return;
            }

            if (attempt >= MaxLoadAttempts)
            {
                Warn(what + " 续拉 " + MaxLoadAttempts + " 次仍未就绪，停止（请检查广告位配置与网络）");
                setPumping(false);
                return;
            }

            issueLoad();
            SdkRunner.Delay(RetryDelaySeconds,
                () => PumpLoad(what, isReady, issueLoad, setPumping, attempt + 1));
        }

        // ------------------------------ 公共 ------------------------------

        /// <summary>
        /// 激励/插屏展示、Banner 加载时透传的自定义数据：调用方传入的 customData 合并默认字段
        /// dataVersion / appVersion 后的 JSON。调用方已带同名字段时以调用方为准；
        /// customData 不是 JSON 对象时无法合并，原样透传。
        /// </summary>
        private static string BuildCustomData(string customData = null)
        {
            Newtonsoft.Json.Linq.JObject obj;
            if (string.IsNullOrEmpty(customData))
            {
                obj = new Newtonsoft.Json.Linq.JObject();
            }
            else
            {
                try
                {
                    obj = Newtonsoft.Json.Linq.JObject.Parse(customData);
                }
                catch (Newtonsoft.Json.JsonReaderException)
                {
                    Warn("customData 不是 JSON 对象，无法合并默认字段，原样透传: " + customData);
                    return customData;
                }
            }

            if (obj["dataVersion"] == null) obj["dataVersion"] = Application.version;
            if (obj["appVersion"] == null)  obj["appVersion"]  = Application.version;
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static SdkAdEventArgs Failed(SdkAdFormat format, string adUnitId)
        {
            return new SdkAdEventArgs
            {
                Success  = false,
                Code     = -1,
                Message  = "sdk not ready",
                Format   = format,
                Phase    = SdkAdPhase.Failed,
                AdUnitId = adUnitId,
            };
        }

        private void Dispatch(SdkAdEventArgs e, Action<SdkAdEventArgs> cb)
        {
            if (OnAdEvent != null) OnAdEvent(e);
            if (cb != null) cb(e);
        }
    }
}
