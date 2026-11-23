namespace LoopPark
{
    /// <summary>
    /// 关卡流程策略：前若干关（默认 1~10）构成「连续闯关区」，这一段流程不经过主界面。
    /// 数据流：启动落点（LoadingController）与过关后的去向（UIManager）都只读这里，
    /// 因此改阈值只改 <see cref="DirectPlayLastLevel"/> 一处，两个场景不会各写一遍。
    /// </summary>
    public static class LevelFlow
    {
        /// <summary>连续闯关区最后一关的关号：1..该值时，启动直接进关卡、过关直接进下一关。</summary>
        public const int DirectPlayLastLevel = 10;

        /// <summary>该关是否落在连续闯关区内。</summary>
        public static bool InDirectPlayRange(int level) =>
            level >= 1 && level <= DirectPlayLastLevel;

        /// <summary>
        /// 过关后是否原地进入下一关：仍在连续闯关区且后面还有关卡。
        /// 超出区间或已是最后一关时返回 false，由调用方回主界面。
        /// </summary>
        public static bool ShouldContinueInPlace(int level) =>
            InDirectPlayRange(level) && level < LevelRepository.MaxLevel;

        /// <summary>
        /// 启动时是否跳过主界面直接进关卡：存档进度仍在连续闯关区，且还有体力。
        /// 体力为 0 时仍走主界面，让补体力弹窗按原逻辑生效。
        /// </summary>
        public static bool ShouldBootIntoLevel =>
            InDirectPlayRange(SaveSystem.D.level) && SaveSystem.D.lives > 0;
    }
}
