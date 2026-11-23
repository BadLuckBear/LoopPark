using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 集装箱顶部解锁提示物。
    /// 取图优先级（由 SkinConfig 配置，美术出图后直接拖进 LoopParkSkin 即可）：
    ///   有颜色：containerColorIcons[unlockColor] → _carIconPrefab → 用解锁色染色的锁图标
    ///   任意颜色：containerAnyColorIcon → _lockSpriteOverride → SpriteFactory 程序生成的锁
    /// 数据流：ContainerView.Setup → Configure(解锁颜色, 尺寸, 世界位置)。
    /// 配置里的 Sprite 一律按原图显示（color = white），不做二次染色。
    /// </summary>
    public sealed class ContainerColorIcon : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] SpriteRenderer _lockSprite;
        [Tooltip("美术正式锁图标；留空则用 SpriteFactory 程序生成的锁")]
        [SerializeField] Sprite _lockSpriteOverride;
        [Tooltip("有颜色解锁时使用的提示物预制体（优先级低于 SkinConfig 图标，暂未配置即走染色锁）")]
        [SerializeField] GameObject _carIconPrefab;

        [Header("渲染")]
        [SerializeField] int _sortingOrder = 42;

        GameObject _spawnedCarIcon;

        /// <summary>按解锁颜色切换提示物，并摆到箱体中心（worldPosition 已由 ContainerView 算在顶盖之前）。</summary>
        public void Configure(int unlockColor, float width, Vector3 worldPosition)
        {
            transform.position = worldPosition;
            transform.localScale = Vector3.one;
            float size = Mathf.Max(0.01f, width);

            bool anyColor = unlockColor < 0;
            var skin = SkinConfig.Instance;

            // 1) LoopParkSkin 里按颜色配好的图标优先，保持原图颜色
            Sprite configured = anyColor
                ? skin.containerAnyColorIcon
                : skin.GetContainerColorIcon(unlockColor);
            if (configured != null)
            {
                SetCarIconActive(false);
                ShowSprite(configured, Color.white, size);
                return;
            }

            // 2) 有颜色且挂了提示物预制体 → 用预制体
            if (!anyColor && _carIconPrefab != null)
            {
                if (_spawnedCarIcon == null)
                    _spawnedCarIcon = Instantiate(_carIconPrefab, transform, false);
                _spawnedCarIcon.SetActive(true);
                _spawnedCarIcon.transform.localPosition = Vector3.zero;
                _spawnedCarIcon.transform.localScale = Vector3.one * size;
                if (_lockSprite != null) _lockSprite.gameObject.SetActive(false);
                return;
            }

            // 3) 回退：锁图标（有颜色时用解锁色染色，便于玩家识别）
            SetCarIconActive(false);
            var fallback = _lockSpriteOverride != null ? _lockSpriteOverride : SpriteFactory.LockIcon();
            ShowSprite(fallback, anyColor ? Color.white : Palette.Get(unlockColor), size);
        }

        void ShowSprite(Sprite sprite, Color color, float size)
        {
            if (_lockSprite == null) return;
            _lockSprite.gameObject.SetActive(true);
            _lockSprite.sprite = sprite;
            _lockSprite.color = color;
            _lockSprite.sortingOrder = _sortingOrder;
            _lockSprite.transform.localScale = Vector3.one * size;
        }

        void SetCarIconActive(bool on)
        {
            if (_spawnedCarIcon != null) _spawnedCarIcon.SetActive(on);
        }
    }
}
