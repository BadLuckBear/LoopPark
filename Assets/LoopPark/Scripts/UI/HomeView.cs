using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    public class HomeView : MonoBehaviour
    {
        public Text coins;
        public Text lives;
        public Text lifeTimer;
        public Text difficulty;
        public Text refillMessage;
        public Sprite[] playStyles;
        public Text level;
        public Text sound;
        public Text music;
        public Text vibrate;
        public Text language;
        public Button musicButton;
        public Button contactButton;
        public Button languageButton;
        public GameObject languageMenu;
        public Button englishButton;
        public Button chineseButton;
        public Button[] languageOptions;
        public Text uid;
        public Text version;
        public Button privacyButton;
        public Button playButton;
        public Button lifeAddButton;
        public Button refillButton;
        public Button livesCloseButton;
        public Button settingsButton;
        public Button shopButton;
        public Button soundButton;
        public Button vibrateButton;
        public Button settingsCloseButton;
        public Button shopCloseButton;
        public Button[] buyButtons;
        public Text[] buyLabels;
        public GameObject settingsPanel;
        public GameObject shopPanel;
        public GameObject livesPanel;

        public void RefreshLanguageOptions(string currentLanguage)
        {
            if (languageOptions == null) return;

            string selectedLanguage = string.IsNullOrEmpty(currentLanguage) ? "English" : currentLanguage;
            var selectedColor = new Color32(0x21, 0x8F, 0xF1, 0xFF);
            var unselectedColor = new Color32(0xA5, 0xA5, 0xA5, 0xFF);
            foreach (var option in languageOptions)
            {
                if (!option) continue;
                var label = option.GetComponentInChildren<Text>(true);
                if (label) label.color = label.text == selectedLanguage ? selectedColor : unselectedColor;
            }
        }
    }
}

