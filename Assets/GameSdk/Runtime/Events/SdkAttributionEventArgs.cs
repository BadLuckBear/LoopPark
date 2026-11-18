namespace Game.Sdk
{
    /// <summary>
    /// 归因信息回调。取不到的字段为 null。
    /// </summary>
    public class SdkAttributionEventArgs : SdkEventArgs
    {
        public string Network;      // 投放渠道，如 Organic / Unity Ads / Facebook Installs
        public string Campaign;
        public string TrackerToken;
    }
}
