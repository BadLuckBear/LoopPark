namespace Game.Sdk
{
    /// <summary>
    /// 商店商品信息，用于展示本地化价格。
    /// </summary>
    public class SdkProductInfo
    {
        public string ProductId;
        public string Title;
        public string Description;
        public double Price;
        public string FullPrice;            // 带货币符号，如 "$0.99"
        public string PriceCurrencyCode;
        public string PriceCurrencySymbol;
    }
}
