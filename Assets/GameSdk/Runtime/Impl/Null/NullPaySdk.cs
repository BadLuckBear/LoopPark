using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Sdk
{
    /// <summary>
    /// PC / Editor 的内购实现：购买立即"成功"，并伪造一笔订单触发 OnPurchaseSucceeded，
    /// 让发货闭环在 Editor 下可完整验收（否则上层等不到事件，发货流程根本没法测）。
    /// </summary>
    internal class NullPaySdk : IPaySdk
    {
        public event Action<SdkPurchaseEventArgs> OnPurchaseSucceeded;

        private readonly Dictionary<string, string> _prices = new Dictionary<string, string>();

        public void Subscribe()
        {
            // Null 平台没有底层推送，空实现
        }

        public void InitProducts(List<string> productIds, Action<SdkProductEventArgs> onDone)
        {
            var list = new List<SdkProductInfo>();

            if (productIds != null)
            {
                foreach (var id in productIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;

                    _prices[id] = "$0.99";
                    list.Add(new SdkProductInfo
                    {
                        ProductId           = id,
                        Title               = id,
                        Description         = "null-impl",
                        Price               = 0.99d,
                        FullPrice           = "$0.99",
                        PriceCurrencyCode   = "USD",
                        PriceCurrencySymbol = "$",
                    });
                }
            }

            Debug.Log("[NullPay] InitProducts count=" + list.Count);

            if (onDone != null)
            {
                onDone(new SdkProductEventArgs
                {
                    Success  = true,
                    Code     = 0,
                    Message  = "null-impl",
                    Products = list.ToArray(),
                });
            }
        }

        public void Purchase(string productId, Action<SdkEventArgs> onImmediate)
        {
            Debug.Log("[NullPay] Purchase " + productId + " → 模拟成功");

            var ok = new SdkPurchaseEventArgs
            {
                Success = true,
                Code    = 0,
                Message = "null-impl",
                Items   = new[]
                {
                    new SdkPurchaseItem
                    {
                        Store            = "Null",
                        ProductId        = productId,
                        OrderId          = "NULL-" + DateTime.UtcNow.Ticks,
                        Currency         = "USD",
                        Revenue          = 0.99d,
                        ReceiptJson      = "{}",
                        ReceiptSign      = "",
                        PurchaseToken    = "null-token-" + productId,
                        DeveloperPayload = "",
                        PurchaseTime     = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    }
                }
            };

            if (onImmediate != null)
                onImmediate(new SdkPlainEventArgs { Success = true, Code = 0, Message = "null-impl" });

            if (OnPurchaseSucceeded != null) OnPurchaseSucceeded(ok);
        }

        public void Restore() => Debug.Log("[NullPay] Restore（无未消费订单）");

        public void Consume(string purchaseToken)
            => Debug.Log("[NullPay] Consume " + purchaseToken);

        public string GetLocalizedPrice(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;
            string p;
            return _prices.TryGetValue(productId, out p) ? p : null;
        }
    }
}
