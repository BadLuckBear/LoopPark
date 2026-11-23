using System;
using UnityEngine;

namespace Game.Sdk
{
    /// <summary>
    /// PC / Editor 的埋点 + 评分 + GDPR：只打日志。
    /// </summary>
    internal class NullMiscSdk : IAnalyticsSdk, IReviewSdk, IConsentSdk
    {
        public void LogEvent(string eventName) => Debug.Log("[NullAnalytics] " + eventName);

        public void LogEvent(string eventName, string parametersJson)
            => Debug.Log("[NullAnalytics] " + eventName + " " + parametersJson);

        public void MaxEvent(string eventName) => Debug.Log("[NullMaxEvent] " + eventName);

        public void MaxEvent(string eventName, string parametersJson)
            => Debug.Log("[NullMaxEvent] " + eventName + " " + parametersJson);

        public void SetUserId(string uid) => Debug.Log("[NullAnalytics] SetUserId " + uid);

        public void SetUserProperty(string key, string value)
            => Debug.Log("[NullAnalytics] Prop " + key + "=" + value);

        public void CrashLog(string message) => Debug.Log("[NullCrash] " + message);

        public void CrashSetUserId(string uid) => Debug.Log("[NullCrash] uid " + uid);

        public void CrashRecordException(Exception e) => Debug.LogWarning("[NullCrash] " + e);

        // PC/Editor 没有归因后端，返回失败——调用方据此不写持久化，与迁移前一致
        public void GetAdvertisingId(Action<SdkIdEventArgs> onResult)
        {
            if (onResult != null)
                onResult(new SdkIdEventArgs { Success = false, Code = -1, Message = "null-impl" });
        }

        public void GetGoogleAdId(Action<SdkIdEventArgs> onResult)
        {
            if (onResult != null)
                onResult(new SdkIdEventArgs { Success = false, Code = -1, Message = "null-impl" });
        }

        public void GetAttribution(Action<SdkAttributionEventArgs> onResult)
        {
            if (onResult != null)
                onResult(new SdkAttributionEventArgs { Success = false, Code = -1, Message = "null-impl" });
        }

        // 保持现状：迁移前评分只在 Android player 拉起，其他平台不弹
        public void CanReview(Action<SdkEventArgs> onResult)
        {
            if (onResult != null)
                onResult(new SdkPlainEventArgs { Success = false, Code = -1, Message = "null-impl" });
        }

        public void RequestReview(Action<SdkEventArgs> onResult)
        {
            Debug.Log("[NullReview] RequestReview (not shown on PC/Editor)");
            if (onResult != null)
                onResult(new SdkPlainEventArgs { Success = false, Code = -1, Message = "null-impl" });
        }

        public void CanModifyConsent(Action<SdkEventArgs> onResult)
        {
            if (onResult != null)
                onResult(new SdkPlainEventArgs { Success = false, Code = -1, Message = "null-impl" });
        }

        public void ModifyConsent() => Debug.Log("[NullConsent] ModifyConsent (not shown on PC/Editor)");
    }
}
