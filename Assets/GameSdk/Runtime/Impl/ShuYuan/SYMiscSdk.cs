using System;
using ShuYuan.SYSDK;
using UnityEngine;

namespace Game.Sdk.Impl.ShuYuan
{
    /// <summary>
    /// 埋点 + 崩溃 + 评分 + GDPR。
    ///
    /// 这四项都绑在同一套中台 SDK 上，且不可能独立更换厂商（评分与 GDPR 本就依附广告 SDK），
    /// 因此合并为一个实现类。若将来某项需要独立替换，拆出去即可——
    /// 接口是分开的，装配根里换一行就行。
    /// </summary>
    internal class SYMiscSdk : IAnalyticsSdk, IReviewSdk, IConsentSdk
    {
        private static ShuYuanSDK SDK { get { return ShuYuanSDK.Instance; } }

        // ------------------------------ 埋点 ------------------------------

        public void LogEvent(string eventName)
        {
            if (SDK == null || string.IsNullOrEmpty(eventName)) return;
            SDK.LogEvent(eventName);
        }

        public void LogEvent(string eventName, string parametersJson)
        {
            if (SDK == null || string.IsNullOrEmpty(eventName)) return;
            SDK.LogEvent(eventName, parametersJson);
        }

        /// <summary>MAX 自定义事件。只进 MAX，不同步 Firebase / Adjust。</summary>
        public void MaxEvent(string eventName)
        {
            if (SDK == null || string.IsNullOrEmpty(eventName)) return;
            SDK.MaxEvent(eventName);
        }

        public void MaxEvent(string eventName, string parametersJson)
        {
            if (SDK == null || string.IsNullOrEmpty(eventName)) return;
            SDK.MaxEvent(eventName, parametersJson);
        }

        public void SetUserId(string uid)
        {
            if (SDK == null) return;
            SDK.AnalyticsSetUserId(uid);
            SDK.MaxSetUserIdentifier(uid);
        }

        public void SetUserProperty(string key, string value)
        {
            if (SDK == null) return;
            SDK.AnalyticsSetUserProperty(key, value);
        }

        // ------------------------------ 崩溃 ------------------------------

        public void CrashLog(string message)
        {
            if (SDK == null) return;
            SDK.CrashLog(message);
        }

        public void CrashSetUserId(string uid)
        {
            if (SDK == null) return;
            SDK.CrashSetUserId(uid);
        }

        public void CrashRecordException(Exception e)
        {
            if (SDK == null || e == null) return;
            SDK.CrashRecordException(e);
        }

        public void GetAdvertisingId(Action<SdkIdEventArgs> onResult)
        {
            if (SDK == null)
            {
                if (onResult != null)
                    onResult(new SdkIdEventArgs { Success = false, Code = -1, Message = "sdk null" });
                return;
            }

            SDK.AdjustAdId((state, json) =>
            {
                if (onResult != null) onResult(SYEventParser.ToId(state, json));
            });
        }

        public void GetGoogleAdId(Action<SdkIdEventArgs> onResult)
        {
            if (SDK == null)
            {
                if (onResult != null)
                    onResult(new SdkIdEventArgs { Success = false, Code = -1, Message = "sdk null" });
                return;
            }

            SDK.AdjustGaId((state, json) =>
            {
                if (onResult != null) onResult(SYEventParser.ToId(state, json));
            });
        }

        public void GetAttribution(Action<SdkAttributionEventArgs> onResult)
        {
            if (SDK == null)
            {
                if (onResult != null)
                    onResult(new SdkAttributionEventArgs { Success = false, Code = -1, Message = "sdk null" });
                return;
            }

            SDK.AdjustAttribution((state, json) =>
            {
                if (onResult != null) onResult(SYEventParser.ToAttribution(state, json));
            });
        }

        // ------------------------------ 评分 ------------------------------

        public void CanReview(Action<SdkEventArgs> onResult)
        {
            if (SDK == null)
            {
                if (onResult != null)
                    onResult(new SdkPlainEventArgs { Success = false, Code = -1, Message = "sdk null" });
                return;
            }

            SDK.CanAppReview((state, json) =>
            {
                if (onResult != null) onResult(SYEventParser.ToBase(state, json));
            });
        }

        public void RequestReview(Action<SdkEventArgs> onResult)
        {
            if (SDK == null)
            {
                if (onResult != null)
                    onResult(new SdkPlainEventArgs { Success = false, Code = -1, Message = "sdk null" });
                return;
            }

            SDK.AppReview((state, json) =>
            {
                if (onResult != null) onResult(SYEventParser.ToBase(state, json));
            });
        }

        // ------------------------------ GDPR ------------------------------

        public void CanModifyConsent(Action<SdkEventArgs> onResult)
        {
            if (SDK == null)
            {
                if (onResult != null)
                    onResult(new SdkPlainEventArgs { Success = false, Code = -1, Message = "sdk null" });
                return;
            }

            SDK.CanModifyConsent((state, json) =>
            {
                if (onResult != null) onResult(SYEventParser.ToBase(state, json));
            });
        }

        public void ModifyConsent()
        {
            if (SDK == null) return;
            SDK.ModifyConsent();
        }
    }
}
