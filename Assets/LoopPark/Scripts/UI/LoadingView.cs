using UnityEngine;
using UnityEngine.UI;

namespace LoopPark
{
    public class LoadingView : MonoBehaviour
    {
        [SerializeField] Image progressFill;
        [SerializeField] RectTransform progressCar;

        public void SetProgress(float value)
        {
            float progress = Mathf.Clamp01(value);
            if (!progressCar)
            {
                progressFill.fillAmount = progress;
                return;
            }

            var fill = progressFill.rectTransform;
            float width = ((RectTransform)fill.parent).rect.width;
            float inset = fill.anchoredPosition.x;
            float end = Mathf.Lerp(inset, width - inset, progress);
            fill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, end - inset);
            var position = progressCar.anchoredPosition;
            float halfCar = progressCar.rect.width * .5f;
            position.x = Mathf.Lerp(halfCar, width - halfCar, progress);
            progressCar.anchoredPosition = position;
        }
    }
}
