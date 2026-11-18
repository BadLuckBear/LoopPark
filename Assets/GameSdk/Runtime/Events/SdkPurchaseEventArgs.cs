namespace Game.Sdk
{
    /// <summary>
    /// 购买成功推送，一次可含多笔（Restore 补单）。
    /// </summary>
    public class SdkPurchaseEventArgs : SdkEventArgs
    {
        public SdkPurchaseItem[] Items;
    }
}
