using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    public class HudView : MonoBehaviour
    {
        public Text level;
        public Text coins;
        public Text difficulty;
        public Image difficultyBackground;
        public Button settingsButton;
        public Button[] boosterButtons;
        public Text[] boosterCounts;
        public Image[] boosterBadges;
    }
}
