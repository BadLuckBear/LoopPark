using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    public class HomeView : MonoBehaviour
    {
        public Text coins;
        public Text lives;
        public Text level;
        public Text sound;
        public Text vibrate;
        public Text bestLevel;
        public Button playButton;
        public Button settingsButton;
        public Button shopButton;
        public Button leaderboardButton;
        public Button soundButton;
        public Button vibrateButton;
        public Button resetButton;
        public Button confirmResetButton;
        public Button cancelResetButton;
        public Button settingsCloseButton;
        public Button shopCloseButton;
        public Button leaderboardCloseButton;
        public Button[] buyButtons;
        public Text[] buyLabels;
        public GameObject settingsPanel;
        public GameObject shopPanel;
        public GameObject leaderboardPanel;
        public GameObject resetPanel;
    }
}
