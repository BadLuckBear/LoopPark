using System;
using UnityEngine;

namespace LoopPark
{
    static class BoosterPurchasePopup
    {
        public static PopupView Open(Transform parent, Booster booster, Action close)
        {
            int index = (int)booster;
            int price = Economy.BoosterPrice[index];
            var art = booster == Booster.ExtraSlot ? "icon_add cart" : booster == Booster.Shuffle ? "icon_Green light" : "icon_banner 3";
            PopupView popup = null;
            popup = PopupView.Create(parent, Economy.BoosterName[index], Economy.BoosterDesc[index], UIKit.Art(art), "×1", "Buy", () =>
            {
                if (SaveSystem.TryBuyBooster(booster)) close();
                else popup.SetMessage("Not enough coins");
            }, close, price, SaveSystem.D.coins);
            return popup;
        }
    }
}
