namespace Game.Sdk
{
    /// <summary>
    /// 返回单个标识串的回调（如广告归因 ID）。取不到时 Id 为 null。
    /// </summary>
    public class SdkIdEventArgs : SdkEventArgs
    {
        public string Id;
    }
}
