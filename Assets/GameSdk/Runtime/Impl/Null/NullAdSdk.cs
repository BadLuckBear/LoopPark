using System;
using UnityEngine;

namespace Game.Sdk
{
    /// <summary>
    /// PC / Editor 的广告实现：始终就绪，激励直接发奖励。
    /// 这是完整实现而非占位符——门面不需要写满 ?. 判空。
    /// </summary>
    internal class NullAdSdk : IAdSdk
    {
        public event Action<SdkAdEventArgs> OnAdEvent;
        public event Action<SdkAdEventArgs> OnAdDisplayed;

        private SdkAdSlots _slots;

        public void Init(SdkAdSlots slots)
        {
            _slots = slots ?? new SdkAdSlots();
            Debug.Log("[NullAd] Init");
        }

        public bool IsRewardReady()       => true;
        public bool IsInterstitialReady() => true;
        public bool IsBannerReady()       => true;

        public void ShowReward(Action<SdkAdEventArgs> onClose, string customData = null)
            => Fire(SdkAdFormat.Reward, _slots == null ? null : _slots.RewardId, onClose);

        public void ShowInterstitial(Action<SdkAdEventArgs> onClose, string customData = null)
            => Fire(SdkAdFormat.Interstitial, _slots == null ? null : _slots.InterstitialId, onClose);

        public void ShowBanner(Action<SdkAdEventArgs> onResult = null)
            => Fire(SdkAdFormat.Banner, _slots == null ? null : _slots.BannerId, onResult);

        public void HideBanner()  => Debug.Log("[NullAd] HideBanner");
        public void CloseBanner() => Debug.Log("[NullAd] CloseBanner");
        public void Load(SdkAdFormat format, Action<SdkAdEventArgs> onResult = null)
        {
            //始终就绪、从不真正加载，直接回调成功
            if (onResult != null)
                onResult(new SdkAdEventArgs { Success = true, Code = 0, Message = "null-impl", Format = format, Phase = SdkAdPhase.Loaded });
        }

        private void Fire(SdkAdFormat format, string adUnitId, Action<SdkAdEventArgs> cb)
        {
            Debug.Log("[NullAd] Show " + format + " → 直接成功");

            //跟真机时序一致：先推展示成功，再回调终态
            var displayed = new SdkAdEventArgs
            {
                Success       = true,
                Code          = 0,
                Format        = format,
                Phase         = SdkAdPhase.Shown,
                AdUnitId      = adUnitId,
                MediationName = "Null",
            };
            if (OnAdDisplayed != null) OnAdDisplayed(displayed);
            if (OnAdEvent != null) OnAdEvent(displayed);

            var e = new SdkAdEventArgs
            {
                Success       = true,
                Code          = 0,
                Message       = "null-impl",
                RawJson       = null,
                Format        = format,
                Phase         = SdkAdPhase.Closed,
                AdUnitId      = adUnitId,
                Revenue       = 0d,
                MediationName = "Null",
            };

            if (OnAdEvent != null) OnAdEvent(e);
            if (cb != null) cb(e);
        }
    }
}
