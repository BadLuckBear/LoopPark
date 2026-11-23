using UnityEngine;

namespace LoopPark
{
    /// <summary>Modal entrance animation.</summary>
    public sealed class PopIn : MonoBehaviour
    {
        float t;
        void OnEnable() { t = 0; transform.localScale = Vector3.one * 0.6f; }
        void Update()
        {
            if (t >= 1) return;
            t = Mathf.Min(1, t + Time.unscaledDeltaTime * 4f);
            float k = 1 + Mathf.Sin(t * Mathf.PI) * 0.12f * (1 - t);
            float s = Mathf.Lerp(0.6f, 1f, 1 - Mathf.Pow(1 - t, 3)) * k;
            transform.localScale = new Vector3(s, s, 1);
        }
    }
}
