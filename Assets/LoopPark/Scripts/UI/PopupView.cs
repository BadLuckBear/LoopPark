using System;
using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    public enum PopupTemplate { Y1, Y2, Y3, Y4, Y5 }

    public sealed class PopupView : MonoBehaviour
    {
        const string PrefabPath = "UI/Popup";
        const string CardPath = "_Prpup_D";
        const string ContentPath = "_Prpup_D_1";

        Text message;
        Text countdown;
        RectTransform card;
        RectTransform primary;
        Button secondary;
        Text secondaryLabel;
        Text secondaryAmount;
        PopupTemplate template;
        float countdownRefresh;

        public static PopupView Create(Transform parent, string title, string description, Sprite icon, string amount,
            string actionLabel, Action onAction, Action onClose, int? price = null, int balance = 0,
            PopupTemplate template = PopupTemplate.Y1)
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (!prefab)
            {
                Debug.LogError($"[LoopPark] Missing popup prefab at Resources/{PrefabPath}.");
                return null;
            }

            var root = Instantiate(prefab, parent, false);
            var view = root.AddComponent<PopupView>();
            if (view.Bind(title, description, icon, amount, actionLabel, onAction, onClose, price, balance, template)) return view;

            root.SetActive(false);
            Destroy(root);
            Debug.LogError($"[LoopPark] Invalid control hierarchy in Resources/{PrefabPath}.");
            return null;
        }
        
        bool Bind(string title, string description, Sprite icon, string amount, string actionLabel,
            Action onAction, Action onClose, int? price, int balance, PopupTemplate template)
        {
            this.template = template;
            card = transform.Find(CardPath) as RectTransform;
            if (!card) return false;
            var content = card.Find(ContentPath);
            var variant = content ? content.Find($"Y_{(int)template + 1}") as RectTransform : null;
            if (!variant) return false;
            for (int i = 0; i < content.childCount; i++)
                content.GetChild(i).gameObject.SetActive(content.GetChild(i) == variant);

            var heading = Control<Text>(variant, "_D_Title/_D_Title_Text");
            message = Control<Text>(variant, "_Prpup_D_1_Text");
            var close = Control<Button>(card, "_Close_Btn");
            string actionPath = template == PopupTemplate.Y4 ? "_Prpup_D_Buy_RightButton" : "_Prpup_D_Buy_Button";
            var action = Control<Button>(variant, actionPath);
            var label = Control<Text>(variant, actionPath + "/_Prpup_D_Buy_Button_LeftText");
            var cost = template == PopupTemplate.Y4
                ? Control<Text>(variant, actionPath + "/Image/Text (Legacy)")
                : Control<Text>(variant, actionPath + "/_Prpup_D_Buy_Button_RightText");
            var coinIcon = variant.Find(actionPath + "/_Prpup_D_Buy_Button_icon");
            var coins = Control<Text>(transform, "CoinsBg/CoinsText");
            var grid = action ? action.GetComponent<GridLayoutGroup>() : null;
            if (!heading || !message || !close || !action || !label || !coins) return false;
            // GridLayoutGroup is a visual layout aid; the popup remains usable if a designer removes it.
            if (price.HasValue && template != PopupTemplate.Y5 && !cost) return false;

            Image art = null;
            Text count = null;
            if (template == PopupTemplate.Y1)
            {
                art = Control<Image>(variant, "_Prpup_D_1_D/_Prpup_D_1_D_Icon");
                count = Control<Text>(variant, "_Prpup_D_1_D/_Prpup_D_1_D_Icon_NumBer");
            }
            else if (template == PopupTemplate.Y5)
                art = Control<Image>(variant, "Image");
            else if (template == PopupTemplate.Y3 || template == PopupTemplate.Y4)
                count = Control<Text>(variant, "_Prpup_D_1_D/_Prpup_D_1_D_Icon_NumBer");
            if ((template == PopupTemplate.Y1 && (!art || !count)) ||
                (template == PopupTemplate.Y3 && !count) ||
                (template == PopupTemplate.Y4 && !count)) return false;

            secondary = template == PopupTemplate.Y4 ? Control<Button>(variant, "_Prpup_D_Buy_LeftButton") : null;
            secondaryLabel = secondary ? Control<Text>(variant, "_Prpup_D_Buy_LeftButton/_Prpup_D_Buy_Button_LeftText") : null;
            secondaryAmount = secondary ? Control<Text>(variant, "_Prpup_D_Buy_LeftButton/Image/Text (Legacy)") : null;
            countdown = template == PopupTemplate.Y4 ? Control<Text>(variant, "_Prpup_D_1_Text_2/Image/Countdown") : null;
            if (template == PopupTemplate.Y4 && (!secondary || !secondaryLabel || !countdown)) return false;

            if (price.HasValue && template == PopupTemplate.Y1 && !coinIcon) return false;

            primary = (RectTransform)action.transform;
            var center = new Vector2(.5f, .5f);
            card.anchorMin = card.anchorMax = center;
            card.anchoredPosition = new Vector2(0, 40);
            if (TryGetComponent<Button>(out var backdrop))
                backdrop.navigation = new Navigation { mode = Navigation.Mode.None };
            coins.transform.parent.gameObject.SetActive(price.HasValue);
            coins.text = balance.ToString();
            SetText(heading, title);
            SetMessage(description);
            if (count) SetText(count, amount);
            if (art && icon)
            {
                art.sprite = icon;
                art.preserveAspect = true;
                art.enabled = true;
                art.rectTransform.sizeDelta = new Vector2(180, 180);
            }

            bool separateCost = template == PopupTemplate.Y1 || template == PopupTemplate.Y4;
            if (cost) cost.gameObject.SetActive(price.HasValue && separateCost);
            if (coinIcon) coinIcon.gameObject.SetActive(price.HasValue);
            if (price.HasValue && separateCost) cost.text = price.Value.ToString();
            if (grid)
            {
                grid.enabled = price.HasValue && separateCost;
                if (!grid.enabled) UIKit.Stretch(label.rectTransform);
            }
            SetText(label, template == PopupTemplate.Y5 && price.HasValue
                ? $"{actionLabel}     {price.Value}" : actionLabel);
            close.gameObject.SetActive(onClose != null);
            if (onClose != null) BindButton(close, onClose);
            BindButton(action, onAction);
            if (secondary) secondary.gameObject.SetActive(false);
            card.gameObject.AddComponent<PopIn>();
            RefreshCountdown();
            return true;
        }

        public void SetMessage(string value) => SetText(message, value);

        public void SetCountdown(string value) => SetText(countdown, value);

        public Button SetSecondary(string label, Action onClick, string amount = null)
        {
            if (!secondary) return null;
            SetText(secondaryLabel, label);
            if (secondaryAmount && amount != null) SetText(secondaryAmount, amount);
            secondary.gameObject.SetActive(true);
            BindButton(secondary, onClick);
            return secondary;
        }

        void Update()
        {
            if (template != PopupTemplate.Y4) return;
            countdownRefresh -= Time.unscaledDeltaTime;
            if (countdownRefresh > 0) return;
            countdownRefresh = 1;
            RefreshCountdown();
        }

        void RefreshCountdown()
        {
            if (template != PopupTemplate.Y4) return;
            SaveSystem.TickLives();
            int seconds = SaveSystem.SecondsToNextLife();
            SetCountdown($"{seconds / 60:00}:{seconds % 60:00}");
        }

        public Button AddSecondary(string text, Action onClick, float x = 0)
        {
            primary.anchoredPosition = new Vector2(0, -245);
            primary.sizeDelta = new Vector2(primary.sizeDelta.x, 154);
            var button = UIKit.Btn(card, text, Color.white, () =>
            {
                if (isActiveAndEnabled) onClick();
            }, 38);
            UIKit.Paint(button, "common_btn_yellow");
            UIKit.Place((RectTransform)button.transform, new Vector2(.5f, .5f), new Vector2(x, -400), new Vector2(370, 118));
            return button;
        }

        void BindButton(Button button, Action onClick)
        {
            button.onClick.AddListener(() =>
            {
                if (!isActiveAndEnabled || !button.interactable) return;
                Sfx.Play("button");
                onClick();
            });
        }

        static T Control<T>(Transform parent, string path) where T : Component => parent.Find(path)?.GetComponent<T>();

        static void SetText(Text text, string value)
        {
            if (!text) return;
            text.font = SpriteFactory.Font;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 28;
            text.resizeTextMaxSize = text.fontSize;
            text.text = value;
        }
    }
}
