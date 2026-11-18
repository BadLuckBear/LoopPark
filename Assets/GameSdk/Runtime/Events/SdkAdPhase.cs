namespace Game.Sdk
{
    /// <summary>
    /// 广告生命周期阶段，标明事件来自哪一环。
    /// </summary>
    public enum SdkAdPhase
    {
        /// <summary>
        /// 播放完成
        /// </summary>
        Loaded,

        /// <summary>
        /// 失败
        /// </summary>
        Failed,

        /// <summary>
        /// 展示成功（已露出）；banner 的 Show 回调也用它
        /// </summary>
        Shown,

        /// <summary>
        /// 关闭
        /// </summary>
        Closed,
    }
}
