using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    public class LoadingView : MonoBehaviour
    {
        [SerializeField] Image progressFill;

        public void SetProgress(float value) => progressFill.fillAmount = Mathf.Clamp01(value);
    }
}
