using System;

namespace Game.Sdk
{
    /// <summary>
    /// 一组格式级广告位 id。
    /// 常规位与新用户位各一组，由业务侧在 Init 时决定传哪组，
    /// SDK 层不需要知道"新用户"这个概念。
    /// </summary>
    [Serializable]
    public class SdkAdSlots
    {
        public string RewardId;
        public string InterstitialId;
        public string BannerId;
    }
}
