namespace Game.Sdk
{
    /// <summary>
    /// 广告回调载体。格式与阶段见 <see cref="SdkAdFormat"/> / <see cref="SdkAdPhase"/>。
    /// </summary>
    public class SdkAdEventArgs : SdkEventArgs
    {
        public SdkAdFormat Format;
        public SdkAdPhase Phase;

        /// <summary>
        /// 广告源唯一Id
        /// </summary>
        public string AdUnitId;

        /// <summary>
        /// 收益
        /// </summary>
        public double Revenue;

        /// <summary>
        /// 广告源
        /// </summary>
        public string MediationName;
    }
}
