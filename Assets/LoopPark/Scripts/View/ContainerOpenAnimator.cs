using System;
using System.Collections;
using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 集装箱开箱动画（方案 B：上移 + 顶边世界 Y 裁切）。
    /// 三段：①解锁提示物弹起消失 ②顶盖沿 +Y 上移并被容器顶边裁掉（车由下往上逐辆露出）
    /// ③外框沿 +Y 快速上移、同样被顶边裁掉（不再向内塌缩穿进车里）。
    /// 数据流：ContainerView.Setup → Initialize(高度, 裁切世界 Y)；GameController.GarageProgress → PlayOpen()。
    /// 生命周期：只驱动本预制体内部节点与顶盖/外框渲染器的 MaterialPropertyBlock，不持有场景引用。
    /// </summary>
    public sealed class ContainerOpenAnimator : MonoBehaviour
    {
        [Header("Hierarchy references")]
        [Tooltip("Roof motion node; moves along world +Y during the animation")]
        [SerializeField] Transform _roofPivot;
        [Tooltip("Frame (walls / posts / corner rings) node; retracts along +Y into the top edge after the roof folds")]
        [SerializeField] Transform _frameRoot;
        [Tooltip("Unlock prompt prop root; pops up and shrinks in phase one")]
        [SerializeField] Transform _iconRoot;

        // 动画时长与行程倍率统一在 LoopParkSkin（SkinConfig）「集装箱外壳：开箱动画时长」里调，
        // 每次 Initialize 时读取一次（改配置对新生成的集装箱立即生效）。
        float _iconHideDuration;
        float _roofDelay;
        float _roofRollDuration;
        float _frameHideDuration;
        float _roofTravelMultiplier;

        static readonly int ClipEnabledId = Shader.PropertyToID("_ClipEnabled");
        static readonly int ClipTopYId = Shader.PropertyToID("_ClipTopY");

        Renderer[] _roofRenderers;
        Renderer[] _frameRenderers;
        MaterialPropertyBlock _block;
        float _fittedHeight;
        float _clipTopWorldY;
        bool _ready;
        bool _opening;
        Vector3 _roofStartWorldPos;
        Vector3 _frameStartWorldPos;

        /// <summary>ContainerView 适配完尺寸后调用：写入行程、裁切平面与两段渲染器缓存。</summary>
        public void Initialize(float fittedHeight, float clipTopWorldY)
        {
            _fittedHeight = Mathf.Max(0.01f, fittedHeight);
            _clipTopWorldY = clipTopWorldY;

            var skin = SkinConfig.Instance;
            _iconHideDuration = Mathf.Max(0f, skin.containerIconHideDuration);
            _roofDelay = Mathf.Max(0f, skin.containerRoofDelay);
            _roofRollDuration = Mathf.Max(0.01f, skin.containerRoofRollDuration);
            _frameHideDuration = Mathf.Max(0f, skin.containerFrameHideDuration);
            _roofTravelMultiplier = Mathf.Max(0.1f, skin.containerTravelMultiplier);
            _roofRenderers = _roofPivot != null
                ? _roofPivot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            _frameRenderers = _frameRoot != null
                ? _frameRoot.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            _block ??= new MaterialPropertyBlock();
            if (_roofPivot != null) _roofStartWorldPos = _roofPivot.position;
            if (_frameRoot != null) _frameStartWorldPos = _frameRoot.position;

            // 静止状态两段都不裁切，避免预览/上场瞬间被切掉
            SetRenderersClip(_roofRenderers, false, _clipTopWorldY);
            SetRenderersClip(_frameRenderers, false, _clipTopWorldY);
            _ready = true;
        }

        public bool IsPlaying => _opening;

        /// <summary>播放三段式开启动画；结束后销毁自身（由 GameController 决定何时调用）。</summary>
        public void PlayOpen(Action onComplete = null)
        {
            if (_opening) return;
            if (!_ready)
            {
                Debug.LogError("[ContainerOpenAnimator] Not initialized, open animation skipped", this);
                onComplete?.Invoke();
                return;
            }
            _opening = true;
            StartCoroutine(OpenRoutine(onComplete));
        }

        IEnumerator OpenRoutine(Action onComplete)
        {
            // 阶段一：解锁提示物弹起缩小消失（与阶段二有 0.06s 重叠）
            if (_iconRoot != null && _iconRoot.gameObject.activeSelf)
                StartCoroutine(HideIconRoutine());

            if (_roofDelay > 0f) yield return new WaitForSeconds(_roofDelay);

            Vector3 upDir = transform.TransformDirection(Vector3.up).normalized;
            float travel = _fittedHeight * _roofTravelMultiplier * Mathf.Abs(transform.lossyScale.y);

            // 阶段二：顶盖沿 +Y 上移，顶边裁切固定在世界 Y，形成卷起的观感
            if (_roofPivot != null)
            {
                SetRenderersClip(_roofRenderers, true, _clipTopWorldY);

                Vector3 startPos = _roofStartWorldPos;
                Vector3 endPos = startPos + upDir * travel;

                float elapsed = 0f;
                while (elapsed < _roofRollDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / _roofRollDuration);
                    // EaseInOutCubic：起止自然、中段利落
                    float ease = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                    _roofPivot.position = Vector3.LerpUnclamped(startPos, endPos, ease);
                    yield return null;
                }
                _roofPivot.position = endPos;
                _roofPivot.gameObject.SetActive(false);
            }

            // 阶段三：外框沿 +Y 快速上移并被顶边裁切线裁掉（不向内塌缩穿模）
            if (_frameRoot != null && _frameHideDuration > 0f)
            {
                SetRenderersClip(_frameRenderers, true, _clipTopWorldY);

                Vector3 startPos = _frameStartWorldPos;
                Vector3 endPos = startPos + upDir * travel;

                float elapsed = 0f;
                while (elapsed < _frameHideDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / _frameHideDuration);
                    float ease = t * t; // EaseInQuad，干脆收走
                    _frameRoot.position = Vector3.LerpUnclamped(startPos, endPos, ease);
                    yield return null;
                }
                _frameRoot.position = endPos;
                _frameRoot.gameObject.SetActive(false);
            }

            onComplete?.Invoke();
            Destroy(gameObject);
        }

        IEnumerator HideIconRoutine()
        {
            Vector3 startScale = _iconRoot.localScale;
            float elapsed = 0f;
            while (elapsed < _iconHideDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _iconHideDuration);
                // 先弹到 1.25 再缩到 0
                float s = t < 0.35f
                    ? Mathf.Lerp(1f, 1.25f, t / 0.35f)
                    : Mathf.Lerp(1.25f, 0f, (t - 0.35f) / 0.65f);
                _iconRoot.localScale = startScale * s;
                yield return null;
            }
            _iconRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 用 MaterialPropertyBlock 驱动裁切，避免为每个集装箱实例生成材质副本。
        /// 顶盖与外框共用：静态态关裁切，动画期间开裁切并锁在容器顶边。
        /// </summary>
        void SetRenderersClip(Renderer[] renderers, bool enabled, float worldTopY)
        {
            if (renderers == null || renderers.Length == 0) return;
            _block ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetFloat(ClipEnabledId, enabled ? 1f : 0f);
                _block.SetFloat(ClipTopYId, worldTopY);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
