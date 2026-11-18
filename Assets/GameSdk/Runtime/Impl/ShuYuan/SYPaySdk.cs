using System;
using System.Collections.Generic;
using ShuYuan.SYSDK;
using UnityEngine;

namespace Game.Sdk.Impl.ShuYuan
{
    /// <summary>
    /// 中台支付通道。
    ///
    /// 只负责与底层 SDK 交互：初始化商品、发起购买、消费、抛出购买事件、查本地化价格。
    /// 验单 / 发货 / 留单 / 去重等业务编排一律在上层（PurchaseFlow）——
    /// 这样换支付厂商时业务编排一行不用动。
    /// </summary>
    internal class SYPaySdk : IPaySdk
    {
        public event Action<SdkPurchaseEventArgs> OnPurchaseSucceeded;

        /// <summary>
        /// 商品本地化价格缓存，InitProducts 成功后填充。
        /// </summary>
        private readonly Dictionary<string, string> _localizedPrice = new Dictionary<string, string>();

        private bool _subscribed;

        private static ShuYuanSDK SDK { get { return ShuYuanSDK.Instance; } }

        private static void Log(string msg)  { Debug.Log("[SYPay] " + msg); }
        private static void Warn(string msg) { Debug.LogWarning("[SYPay] " + msg); }

        /// <summary>
        /// 订阅 SDK 全局购买推送。
        ///
        /// 注意：ShuYuanSDK.OnPurchaseSucceeded 的 add 访问器在**首次订阅时**才向原生层
        /// 注册回调（ShuYuanSDK.cs:36-47），所以不订阅就收不到任何推送。
        /// 必须尽早调用，避免漏接冷启动补单。
        /// </summary>
        public void Subscribe()
        {
            if (_subscribed) return;

            if (SDK == null)
            {
                Warn("Subscribe: SDK.Instance 为空，订阅失败");
                return;
            }

            SDK.OnPurchaseSucceeded += OnSdkPurchaseSucceeded;
            _subscribed = true;
            Log("已订阅 ShuYuanSDK.OnPurchaseSucceeded（唯一发货入口）");
        }

        private void OnSdkPurchaseSucceeded(SYPurchaseItem[] items)
        {
            var list = new List<SdkPurchaseItem>();

            if (items != null)
            {
                foreach (var it in items)
                {
                    var dto = SYEventParser.ToPurchaseItem(it);
                    if (dto != null) list.Add(dto);
                }
            }

            Log("OnPurchaseSucceeded count=" + list.Count);

            if (OnPurchaseSucceeded != null)
            {
                OnPurchaseSucceeded(new SdkPurchaseEventArgs
                {
                    Success = true,
                    Code    = 0,
                    Message = "ok",
                    Items   = list.ToArray(),
                });
            }
        }

        public void InitProducts(List<string> productIds, Action<SdkProductEventArgs> onDone)
        {
            if (SDK == null)
            {
                Warn("InitProducts: SDK.Instance 为空");
                if (onDone != null)
                {
                    onDone(new SdkProductEventArgs
                    {
                        Success  = false,
                        Code     = -1,
                        Message  = "sdk instance null",
                        Products = new SdkProductInfo[0],
                    });
                }
                return;
            }

            Log("InitProducts count=" + (productIds == null ? 0 : productIds.Count));

            SDK.InitProducts(productIds, (state, json) =>
            {
                var e = SYEventParser.ToProducts(state, json);

                _localizedPrice.Clear();
                foreach (var p in e.Products)
                {
                    if (!string.IsNullOrEmpty(p.ProductId) && !string.IsNullOrEmpty(p.FullPrice))
                        _localizedPrice[p.ProductId] = p.FullPrice;
                }

                Log("InitProducts 回调 success=" + e.Success + " 价格缓存=" + _localizedPrice.Count);
                if (onDone != null) onDone(e);
            });
        }

        public void Purchase(string productId, Action<SdkEventArgs> onImmediate)
        {
            if (SDK == null)
            {
                Warn("Purchase: SDK.Instance 为空");
                if (onImmediate != null)
                    onImmediate(new SdkPlainEventArgs { Success = false, Code = -1, Message = "sdk instance null" });
                return;
            }

            Log("Purchase " + productId);

            SDK.PurchaseAsync(productId, string.Empty, (state, json) =>
            {
                // 发货统一走 OnPurchaseSucceeded，这里只回 UX（关 loading 等）
                var e = SYEventParser.ToBase(state, json);
                Log("Purchase 回调 success=" + e.Success + " code=" + e.Code + "（发货走全局事件）");
                if (onImmediate != null) onImmediate(e);
            });
        }

        public void Restore()
        {
            if (SDK == null) return;

            Log("Restore（未消费订单将逐笔走 OnPurchaseSucceeded）");
            SDK.RestorePurchases((state, json) => Log("Restore 回调 state=" + state));
        }

        public void Consume(string purchaseToken)
        {
            if (SDK == null || string.IsNullOrEmpty(purchaseToken))
            {
                Warn("Consume: SDK 为空或 purchaseToken 为空");
                return;
            }

            SDK.ConsumePurchase(purchaseToken, (state, json) => Log("Consume 回调 state=" + state));
        }

        public string GetLocalizedPrice(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;

            string p;
            return _localizedPrice.TryGetValue(productId, out p) ? p : null;
        }
    }
}
