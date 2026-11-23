using UnityEngine;
using UnityEngine.EventSystems;

namespace LoopPark
{
    /// <summary>Button press feedback.</summary>
    public sealed class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        float target = 1f;
        public void OnPointerDown(PointerEventData e) => target = 0.92f;
        public void OnPointerUp(PointerEventData e) => target = 1f;
        void Update()
        {
            float s = Mathf.Lerp(transform.localScale.x, target, 1 - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            transform.localScale = new Vector3(s, s, 1);
        }
    }
}
