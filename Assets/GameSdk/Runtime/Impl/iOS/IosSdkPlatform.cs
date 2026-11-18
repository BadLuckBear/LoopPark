using Game.Sdk.Impl.ShuYuan;

namespace Game.Sdk
{
    /// <summary>
    /// iOS：装配中台 SY 实现（见 <c>Runtime/Impl/ShuYuan/</c>）。
    /// 当前与 Android 装配内容一致，保留独立类是为了将来 iOS 换厂商时
    /// 不必把条件编译渗进业务代码。
    /// </summary>
    public class IosSdkPlatform : SdkPlatformBase
    {
        public override void Assemble()
        {
            SYNativeStart.Register();

            var misc = new SYMiscSdk();
            Ad        = new SYAdSdk();
            Pay       = new SYPaySdk();
            Analytics = misc;
            Review    = misc;
            Consent   = misc;
        }
    }
}
