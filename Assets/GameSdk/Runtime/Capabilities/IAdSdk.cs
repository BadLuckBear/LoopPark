using System;

namespace Game.Sdk
{
    /// <summary>
    /// 广告能力。频控、每日上限、关卡门槛等业务判断一律在上层，本接口只管加载与展示。
    /// </summary>
    public interface IAdSdk
    {
        /// <summary>
        /// 装配后由 Bootstrap 调用，内部做预加载。
        /// </summary>
        void Init(SdkAdSlots slots);

        bool IsRewardReady();
        bool IsInterstitialReady();
        bool IsBannerReady();

        /// <summary>
        /// 展示激励视频。回调 Success=true 表示用户看完，可发奖。
        /// customData：透传给广告 SDK 的自定义数据（JSON 字符串），为空时由实现填默认值。
        /// </summary>
        void ShowReward(Action<SdkAdEventArgs> onClose, string customData = null);

        /// <summary>
        /// 展示插屏。customData 同 ShowReward。
        /// </summary>
        void ShowInterstitial(Action<SdkAdEventArgs> onClose, string customData = null);

        void ShowBanner(Action<SdkAdEventArgs> onResult = null);
        void HideBanner();

        /// <summary>
        /// 销毁 banner（隐藏用 HideBanner）。销毁后需重新加载才能再展示。
        /// </summary>
        void CloseBanner();

        /// <summary>
        /// 确保该格式在加载（已在续拉中则忽略）。上层发现未就绪、不发起展示时调用，
        /// 保证续拉放弃后也还能重新拉起。
        /// 传 onResult 时直接发起一次加载，回调该次 Load 的结果（Success / Code / Message）。
        /// </summary>
        void Load(SdkAdFormat format, Action<SdkAdEventArgs> onResult = null);

        /// <summary>
        /// 广告展示成功（已露出，Phase=Shown）。全局推送，不对应某一次 Show 调用；
        /// Banner 自动刷新时会多次触发。Show 回调仍是该次展示的终态（关闭/失败）。
        /// </summary>
        event Action<SdkAdEventArgs> OnAdDisplayed;

        /// <summary>
        /// 横切扩展点：所有格式、所有阶段的广告事件都流经这里。
        /// </summary>
        event Action<SdkAdEventArgs> OnAdEvent;
    }
}
