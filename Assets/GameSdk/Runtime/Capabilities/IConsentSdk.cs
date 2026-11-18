using System;

namespace Game.Sdk
{
    /// <summary>
    /// GDPR / 隐私授权能力（UMP 同意表单）。
    /// </summary>
    public interface IConsentSdk
    {
        void CanModifyConsent(Action<SdkEventArgs> onResult);
        void ModifyConsent();
    }
}
