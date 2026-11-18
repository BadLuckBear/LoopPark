using System;

namespace Game.Sdk
{
    /// <summary>
    /// 应用内评分能力（Google Play In-App Review / iOS SKStoreReview）。
    /// </summary>
    public interface IReviewSdk
    {
        void CanReview(Action<SdkEventArgs> onResult);
        void RequestReview(Action<SdkEventArgs> onResult);
    }
}
