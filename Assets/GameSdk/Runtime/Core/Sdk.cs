using UnityEngine;

namespace Game.Sdk
{
    /// <summary>
    /// SDK 门面：业务层唯一入口。
    ///
    /// 不负责选平台——那是 SdkBootstrap 的职责。
    /// 否则"门面要 new 实现 → 门面依赖实现 → 实现依赖门面"会成环。
    ///
    /// 字段初值即 Null 平台，未 Install 时调用也不会崩。
    /// </summary>
    public static class Sdk
    {
        private static SdkPlatformBase _platform = CreateDefault();

        public static IAdSdk        Ad        => _platform.Ad;
        public static IPaySdk       Pay       => _platform.Pay;
        public static IAnalyticsSdk Analytics => _platform.Analytics;
        public static IReviewSdk    Review    => _platform.Review;
        public static IConsentSdk   Consent   => _platform.Consent;

        public static void Install(SdkPlatformBase platform)
        {
            if (platform == null)
            {
                Debug.LogError("[Sdk] Install 收到 null，保持原平台不变");
                return;
            }

            SdkRunner.EnsureCreated();
            platform.Assemble();
            _platform = platform;
            Debug.Log("[Sdk] Install: " + platform.GetType().Name);
        }

        private static SdkPlatformBase CreateDefault()
        {
            var p = new NullSdkPlatform();
            p.Assemble();
            return p;
        }
    }
}
