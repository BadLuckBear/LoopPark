using System;
using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    /// <summary>Adapter for the authored victory modal prefab.</summary>
    public sealed class SettlementModalView : MonoBehaviour
    {
        const string RewardTextName = "TextNum";
        const string DoubleButtonName = "Btn_Click";
        const string ContinueButtonName = "Btn_J";

        Text rewardText;
        Button doubleButton;
        Button continueButton;

        public bool Bind(int reward, string doubleLabel, string continueLabel,
            Action onDouble, Action onContinue)
        {
            rewardText = FindNamed(RewardTextName)?.GetComponent<Text>();
            doubleButton = FindNamed(DoubleButtonName)?.GetComponent<Button>();
            continueButton = FindNamed(ContinueButtonName)?.GetComponent<Button>();
            if (!rewardText || !doubleButton || !continueButton)
            {
                Debug.LogError("[LoopPark] Invalid settlement modal hierarchy. Expected TextNum, Btn_Click and Btn_J.");
                return false;
            }

            rewardText.text = reward.ToString();
            SetButtonLabel(doubleButton, doubleLabel);
            SetButtonLabel(continueButton, continueLabel);
            doubleButton.onClick.RemoveAllListeners();
            continueButton.onClick.RemoveAllListeners();
            doubleButton.onClick.AddListener(() => Invoke(onDouble));
            continueButton.onClick.AddListener(() => Invoke(onContinue));
            return true;
        }

        public void SetDoubleButtonInteractable(bool value)
        {
            if (doubleButton) doubleButton.interactable = value;
        }

        Transform FindNamed(string childName)
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child.name == childName) return child;
            return null;
        }

        static void SetButtonLabel(Button button, string value)
        {
            var label = button.GetComponentInChildren<Text>(true);
            if (label) label.text = value;
        }

        static void Invoke(Action action)
        {
            Sfx.Play("button");
            action?.Invoke();
        }
    }
}
