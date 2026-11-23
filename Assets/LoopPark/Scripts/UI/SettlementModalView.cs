using System;
using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    /// <summary>
    /// 「LEVEL COMPLETE」结算弹窗（Resources/UI/Modal）的视图适配层：只负责取子节点、灌数值、转发按钮回调，
    /// 不含经济/存档判断（奖励计算与入账由 UIManager.OpenSettlementModal 负责）。
    ///
    /// prefab 层级约定（由美术/设计师在 Modal 里维护，改动层级要同步下面的路径常量）。
    /// 注意两个按钮都挂在 Card 下，路径必须从 prefab 根算起：
    ///   Card/Text              金币堆右侧的大号奖励数字 —— 运行时写入本关奖励
    ///   Card/Btn_Click/Text    双倍按钮标题（"X2 Coin"）—— 固定在 prefab 里，运行时不覆盖，设计师可直接改文案
    ///   Card/Btn_Click/TextNum 双倍按钮里的数值行 —— 运行时写入本关奖励（与金币堆数字一致）
    ///   Card/Btn_J/Text        继续按钮标题 —— 代码按关卡切换（Continue / Back to Home）
    /// </summary>
    public sealed class SettlementModalView : MonoBehaviour
    {
        const string RewardNumberPath = "Card/Text";
        const string DoubleNumberPath = "Card/Btn_Click/TextNum";
        const string DoubleButtonPath = "Card/Btn_Click";
        const string ContinueButtonPath = "Card/Btn_J";
        const string ContinueLabelPath = "Card/Btn_J/Text";

        Text rewardNumber;
        Text doubleNumber;
        Text continueLabel;
        Button doubleButton;
        Button continueButton;

        /// <summary>
        /// 绑定一关的结算数据。reward 为本次过关奖励，同时写到金币堆数字与双倍按钮数值行，
        /// 保证「看得到 30、点双倍再拿 30」两处一致。返回 false 表示 prefab 结构已被改动，调用方应关掉弹窗。
        /// </summary>
        public bool Bind(int reward, string continueText, Action onDouble, Action onContinue)
        {
            rewardNumber = FindByPath<Text>(RewardNumberPath);
            doubleNumber = FindByPath<Text>(DoubleNumberPath);
            continueLabel = FindByPath<Text>(ContinueLabelPath);
            doubleButton = FindByPath<Button>(DoubleButtonPath);
            continueButton = FindByPath<Button>(ContinueButtonPath);
            if (!rewardNumber || !doubleNumber || !continueLabel || !doubleButton || !continueButton)
            {
                Debug.LogError("[LoopPark] Invalid settlement modal hierarchy. " +
                               "Expected Card/Text, Btn_Click/TextNum, Btn_J/Text, Btn_Click and Btn_J.");
                return false;
            }

            // 奖励数字：金币堆右侧 + 双倍按钮数值行，两处共用同一个值，避免设计师改了一处忘另一处。
            string amount = Mathf.Max(0, reward).ToString();
            rewardNumber.text = amount;
            doubleNumber.text = amount;
            continueLabel.text = continueText;

            doubleButton.onClick.RemoveAllListeners();
            continueButton.onClick.RemoveAllListeners();
            doubleButton.onClick.AddListener(() => Invoke(onDouble));
            continueButton.onClick.AddListener(() => Invoke(onContinue));
            return true;
        }

        /// <summary>广告看完（或失败）后禁用双倍按钮，防止重复领取。</summary>
        public void SetDoubleButtonInteractable(bool value)
        {
            if (doubleButton) doubleButton.interactable = value;
        }

        /// <summary>按层级路径取组件，不依赖遍历全子节点，避免同名节点（如两处 Text）串台。</summary>
        T FindByPath<T>(string path) where T : Component
        {
            var node = transform.Find(path);
            return node ? node.GetComponent<T>() : null;
        }

        /// <summary>统一入口：先播音效再执行回调，保证所有按钮点击手感一致。</summary>
        static void Invoke(Action action)
        {
            Sfx.Play("button");
            action?.Invoke();
        }
    }
}
