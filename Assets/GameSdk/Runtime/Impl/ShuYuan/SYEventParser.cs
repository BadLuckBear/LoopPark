using System.Collections.Generic;
using LitJson;
using ShuYuan.SYSDK;
using UnityEngine;

namespace Game.Sdk.Impl.ShuYuan
{
    /// <summary>
    /// 包内唯一解析 JsonData 的地方——换 SDK 时只需重写本文件。
    ///
    /// 所有 Impl 通过它把底层的 (bool state, JsonData json) 转成我们自己的事件类，
    /// 保证 LitJson 类型不会流出 Impl 目录。
    /// </summary>
    internal static class SYEventParser
    {
        private static void FillBase(SdkEventArgs e, bool state, JsonData json)
        {
            e.Success = state;
            e.Code    = json.GetInt("Code") ?? -1;
            e.Message = json.GetString("Message");
            // 存 string 而非 JsonData，避免第三方类型外泄
            e.RawJson = json == null ? null : json.ToJson();
        }

        internal static SdkEventArgs ToBase(bool state, JsonData json)
        {
            var e = new SdkPlainEventArgs();
            FillBase(e, state, json);
            return e;
        }

        internal static SdkIdEventArgs ToId(bool state, JsonData json)
        {
            var e = new SdkIdEventArgs();
            FillBase(e, state, json);
            e.Id = json.GetString("Data", "Id");
            return e;
        }

        internal static SdkAttributionEventArgs ToAttribution(bool state, JsonData json)
        {
            var e = new SdkAttributionEventArgs();
            FillBase(e, state, json);
            e.Network      = GetDataString(json, "network");
            e.Campaign     = GetDataString(json, "campaign");
            e.TrackerToken = GetDataString(json, "trackerToken");
            return e;
        }

        /// <summary>
        /// SDK 注释只写了"Data 含 trackerToken、network、campaign"，没说大小写，首字母大写的也认
        /// </summary>
        private static string GetDataString(JsonData json, string key)
        {
            var value = json.GetString("Data", key);
            if (string.IsNullOrEmpty(value))
                value = json.GetString("Data", char.ToUpperInvariant(key[0]) + key.Substring(1));
            return string.IsNullOrEmpty(value) ? null : value;
        }

        internal static SdkAdEventArgs ToAd(bool state, JsonData json,
                                            SdkAdFormat format, SdkAdPhase phase,
                                            string fallbackAdUnitId)
        {
            var e = new SdkAdEventArgs { Format = format, Phase = phase };
            FillBase(e, state, json);

            // JsonData 不能交给 Newtonsoft 序列化（会把标量子节点当字典遍历而抛异常），用 LitJson 自己的 ToJson
            //Debug.Log($"[GameEventTrack] => 中台信息: {e.RawJson}");
            e.AdUnitId = json.GetString("Data", "AdUnitId");
            if (string.IsNullOrEmpty(e.AdUnitId))
                e.AdUnitId = fallbackAdUnitId;

            e.Revenue       = json.GetDouble("Data", "Revenue") ?? 0d;
            e.MediationName = json.GetString("Data", "MediationName");
            return e;
        }

        internal static SdkPurchaseItem ToPurchaseItem(SYPurchaseItem src)
        {
            if (src == null) return null;

            return new SdkPurchaseItem
            {
                Store            = src.Store,
                ProductId        = src.ProductId,
                OrderId          = src.OrderId,
                Currency         = src.Currency,
                Revenue          = src.Revenue,
                ReceiptJson      = src.ReceiptJson,
                ReceiptSign      = src.ReceiptSign,
                PurchaseToken    = src.PurchaseToken,
                DeveloperPayload = src.DeveloperPayload,
                PurchaseTime     = src.PurchaseTime,
            };
        }

        internal static SdkProductEventArgs ToProducts(bool state, JsonData json)
        {
            var e = new SdkProductEventArgs();
            FillBase(e, state, json);

            var result = new List<SdkProductInfo>();
            List<JsonData> list = json.GetList("Data", "List");

            if (list != null)
            {
                foreach (var item in list)
                {
                    if (item == null) continue;

                    result.Add(new SdkProductInfo
                    {
                        ProductId           = item.GetString("ProductId"),
                        Title               = item.GetString("Title"),
                        Description         = item.GetString("Description"),
                        Price               = item.GetDouble("Price") ?? 0d,
                        FullPrice           = item.GetString("FullPrice"),
                        PriceCurrencyCode   = item.GetString("PriceCurrencyCode"),
                        PriceCurrencySymbol = item.GetString("PriceCurrencySymbol"),
                    });
                }
            }

            e.Products = result.ToArray();
            return e;
        }
    }
}
