using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LoopPark
{
    /// <summary>运行时 uGUI 构建工具（参考分辨率 1080x1920）。</summary>
    public static class UIKit
    {
        public static Sprite RoundSprite => SkinConfig.Instance.buttonSprite != null ? SkinConfig.Instance.buttonSprite : SpriteFactory.RoundedRect(96, 36, 10);
        public static Sprite PanelSprite => SkinConfig.Instance.panelSprite != null ? SkinConfig.Instance.panelSprite : SpriteFactory.RoundedRect(96, 40);
        public static Sprite FlatRound => SpriteFactory.RoundedRect(64, 28);

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>anchor: (0..1, 0..1) 锚点；pos: 相对锚点的偏移；size: 尺寸。</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Img(Transform parent, string name, Color c, Sprite sp = null, bool sliced = true)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sp;
            img.color = c;
            if (sp != null && sliced && sp.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        public static Text Txt(Transform parent, string text, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool outline = false)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = SpriteFactory.Font;
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = anchor;
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0, 0, 0, 0.45f);
                o.effectDistance = new Vector2(3, -3);
            }
            return t;
        }

        public static Button Btn(Transform parent, string label, Color c, Action onClick, int fontSize = 56)
        {
            var img = Img(parent, "Btn_" + label, c, RoundSprite);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            b.colors = colors;
            b.onClick.AddListener(() =>
            {
                Sfx.Play("button");
                onClick?.Invoke();
            });
            img.gameObject.AddComponent<PressScale>();
            var t = Txt(img.transform, label, fontSize, Color.white, TextAnchor.MiddleCenter, true);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(10, 8);
            t.rectTransform.offsetMax = new Vector2(-10, 0);
            return b;
        }

        public static Text Label(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>数字输入框。</summary>
        public static InputField NumberInput(Transform parent, string placeholder, int fontSize)
        {
            var img = Img(parent, "Input", Color.white, FlatRound);
            var inp = img.gameObject.AddComponent<InputField>();
            var t = Txt(img.transform, "", fontSize, new Color(0.2f, 0.2f, 0.25f));
            Stretch(t.rectTransform);
            t.supportRichText = false;
            var ph = Txt(img.transform, placeholder, fontSize, new Color(0.6f, 0.6f, 0.65f));
            Stretch(ph.rectTransform);
            ph.fontStyle = FontStyle.Normal;
            inp.textComponent = t;
            inp.placeholder = ph;
            inp.contentType = InputField.ContentType.IntegerNumber;
            inp.characterLimit = 5;
            return inp;
        }

        /// <summary>带图标的数值胶囊（金币、体力）。</summary>
        public static Text Pill(Transform parent, string name, Color iconColor, string iconText, Vector2 anchor, Vector2 pos, float width, out Image icon)
        {
            var bg = Img(parent, name, new Color(0, 0, 0, 0.35f), FlatRound);
            Place(bg.rectTransform, anchor, pos, new Vector2(width, 84));
            icon = Img(bg.transform, "Icon", iconColor, SpriteFactory.Circle(), false);
            Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(40, 0), new Vector2(92, 92));
            var it = Txt(icon.transform, iconText, 44, Color.white, TextAnchor.MiddleCenter, true);
            Stretch(it.rectTransform);
            var t = Txt(bg.transform, "0", 44, Color.white, TextAnchor.MiddleCenter, true);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(80, 0);
            t.rectTransform.offsetMax = new Vector2(-10, 0);
            return t;
        }

        /// <summary>模态面板：半透明遮罩 + 卡片。返回卡片。</summary>
        public static RectTransform Modal(Transform parent, string name, float height, out GameObject root)
        {
            var dim = Img(parent, name, new Color(0, 0, 0, 0.62f));
            Stretch(dim.rectTransform);
            root = dim.gameObject;
            var card = Img(dim.transform, "Card", SkinConfig.Instance.panelColor, PanelSprite);
            Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(900, height));
            card.gameObject.AddComponent<PopIn>();
            return card.rectTransform;
        }

        public static Text Title(RectTransform card, string text, Color headColor)
        {
            var head = Img(card, "Head", headColor, RoundSprite);
            Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -10), new Vector2(760, 130));
            var t = Txt(head.transform, text, 64, Color.white, TextAnchor.MiddleCenter, true);
            Stretch(t.rectTransform);
            return t;
        }

        public static Vector2 WorldToCanvas(RectTransform canvasRect, Vector3 world)
        {
            var sp = Camera.main.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out var lp);
            return lp;
        }
    }

    /// <summary>按下缩小、松开回弹。</summary>
    public class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
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

    /// <summary>弹出动画。</summary>
    public class PopIn : MonoBehaviour
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

    /// <summary>上下浮动（提示手指等）。</summary>
    public class Bob : MonoBehaviour
    {
        public float amp = 20f, speed = 5f;
        public Vector2 basePos;
        RectTransform rt;
        void Awake() => rt = (RectTransform)transform;
        void Update()
        {
            rt.anchoredPosition = basePos + new Vector2(0, -Mathf.Abs(Mathf.Sin(Time.unscaledTime * speed)) * amp);
        }
    }
}
