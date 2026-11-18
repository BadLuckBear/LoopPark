namespace Game.Sdk
{
    /// <summary>
    /// PC / Editor：完整 Null Object，任何能力字段都不为 null。
    /// </summary>
    public class NullSdkPlatform : SdkPlatformBase
    {
        public override void Assemble()
        {
            var misc = new NullMiscSdk();
            Ad        = new NullAdSdk();
            Pay       = new NullPaySdk();
            Analytics = misc;
            Review    = misc;
            Consent   = misc;
        }
    }
}
