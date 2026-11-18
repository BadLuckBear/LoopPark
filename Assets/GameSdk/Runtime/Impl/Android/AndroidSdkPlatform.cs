using Game.Sdk.Impl.ShuYuan;

namespace Game.Sdk
{
    /// <summary>
    /// Android：装配中台 SY 实现（见 <c>Runtime/Impl/ShuYuan/</c>）。
    ///
    /// 换广告/支付厂商只需改这里对应的一行 new，业务代码与门面都不动。
    /// </summary>
    public class AndroidSdkPlatform : SdkPlatformBase
    {
        public override void Assemble()
        {
            SYNativeStart.Register();   // 注册原生启动钩子，供 Bootstrap 调用

            var misc = new SYMiscSdk();
            Ad        = new SYAdSdk();
            Pay       = new SYPaySdk();
            Analytics = misc;
            Review    = misc;
            Consent   = misc;
        }
    }
}
