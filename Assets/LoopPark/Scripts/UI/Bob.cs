using UnityEngine;

namespace LoopPark
{
    /// <summary>Small vertical idle motion for tutorial hand.</summary>
    public sealed class Bob : MonoBehaviour
    {
        public float amp = 20f, speed = 5f;
        public Vector2 basePos;
        RectTransform rt;
        void Awake() => rt = (RectTransform)transform;
        void Update() => rt.anchoredPosition = basePos + new Vector2(0, -Mathf.Abs(Mathf.Sin(Time.unscaledTime * speed)) * amp);
    }
}
