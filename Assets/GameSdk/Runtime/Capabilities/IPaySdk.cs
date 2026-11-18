using System;
using System.Collections.Generic;

namespace Game.Sdk
{
    /// <summary>
    /// 支付通道。只负责与底层 SDK 交互——
    /// 验单、发货、留单、去重等业务编排一律在上层（PurchaseFlow）。
    /// </summary>
    public interface IPaySdk
    {
        /// <summary>
        /// 唯一发货入口，含正常购买与 Restore 补单。
        /// </summary>
        event Action<SdkPurchaseEventArgs> OnPurchaseSucceeded;

        /// <summary>
        /// 订阅底层 SDK 的全局购买推送。由 Bootstrap 在就绪队列首位调用，
        /// 必须早于任何购买动作，避免漏接冷启动补单。Null 实现为空方法。
        /// </summary>
        void Subscribe();

        void InitProducts(List<string> productIds, Action<SdkProductEventArgs> onDone);

        /// <summary>
        /// 发起购买。onImmediate 仅用于 UX（关 loading），发货走 OnPurchaseSucceeded。
        /// </summary>
        void Purchase(string productId, Action<SdkEventArgs> onImmediate);

        void Restore();
        void Consume(string purchaseToken);

        /// <summary>
        /// 商店本地化价格串（含货币符号）。取不到返回 null。
        /// </summary>
        string GetLocalizedPrice(string productId);
    }
}
