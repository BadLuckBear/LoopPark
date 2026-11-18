namespace Game.Sdk
{
    /// <summary>
    /// 商品初始化回调，携带商店返回的本地化价格。
    /// </summary>
    public class SdkProductEventArgs : SdkEventArgs
    {
        public SdkProductInfo[] Products;
    }
}
