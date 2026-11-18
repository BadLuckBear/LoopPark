namespace Game.Sdk
{
    /// <summary>
    /// 一笔购买订单。字段对齐中台返回，但类型归我们所有。
    /// </summary>
    public class SdkPurchaseItem
    {
        public string Store;
        public string ProductId;
        public string OrderId;
        public string Currency;
        public double Revenue;
        public string ReceiptJson;
        public string ReceiptSign;
        public string PurchaseToken;
        public string DeveloperPayload;
        public long   PurchaseTime;
    }
}
