using System;

namespace Game.Sdk
{
    /// <summary>
    /// 埋点与崩溃上报能力。
    /// </summary>
    public interface IAnalyticsSdk
    {
        void LogEvent(string eventName);
        void LogEvent(string eventName, string parametersJson);

        /// <summary>
        /// AppLovin MAX 自定义事件（Axon Event Tracking）。
        /// 只进 MAX，不会同步到 Firebase / Adjust——那条链路是 LogEvent。
        /// </summary>
        void MaxEvent(string eventName);

        /// <summary>
        /// 同上，带参数。parametersJson 的数值须为 JSON 字面量（{"level_id":1}），
        /// 写成字符串 "1" 会在 Android 侧丢掉原始类型。
        /// </summary>
        void MaxEvent(string eventName, string parametersJson);

        void SetUserId(string uid);
        void SetUserProperty(string key, string value);
        void CrashLog(string message);
        void CrashSetUserId(string uid);
        void CrashRecordException(Exception e);

        /// <summary>
        /// 广告归因设备 ID。底层要跟归因后端完成首次 session 才有值，故为异步；
        /// 拿不到时回调 Success=false、Id 为空，调用方不应写入持久化。
        /// </summary>
        void GetAdvertisingId(Action<SdkIdEventArgs> onResult);

        /// <summary>
        /// Google 广告 ID（GAID，仅 Android）。拿不到 / 非 Android 时回调 Success=false、Id 为空。
        /// </summary>
        void GetGoogleAdId(Action<SdkIdEventArgs> onResult);

        /// <summary>
        /// 归因信息（渠道 network / campaign 等）。同样要等归因后端首次 session，可能晚到；
        /// 拿不到时回调 Success=false。
        /// </summary>
        void GetAttribution(Action<SdkAttributionEventArgs> onResult);
    }
}
