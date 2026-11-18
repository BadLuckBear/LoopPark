namespace Game.Sdk
{
    /// <summary>
    /// 平台装配根：全工程唯一 new 具体实现的地方。
    /// 换 SDK 厂商 = 新增一个实现 + 改这里一行。
    ///
    /// 必须是 public：将来 UPM 化后 Bootstrap 在包外，要 new 它。
    ///
    /// 具体实现见 <c>Runtime/Impl/</c> 下各平台目录。
    /// </summary>
    public abstract class SdkPlatformBase
    {
        public IAdSdk        Ad        { get; protected set; }
        public IPaySdk       Pay       { get; protected set; }
        public IAnalyticsSdk Analytics { get; protected set; }
        public IReviewSdk    Review    { get; protected set; }
        public IConsentSdk   Consent   { get; protected set; }

        /// <summary>
        /// 装配五个能力。实现里必须给全，任何字段不得为 null。
        /// </summary>
        public abstract void Assemble();
    }
}
