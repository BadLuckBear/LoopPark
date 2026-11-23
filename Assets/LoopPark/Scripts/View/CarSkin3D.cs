using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 3D 车模型皮肤：按颜色给“车漆”材质上色；问号车用统一的问号色；
    /// 绑带等附件通过 accessories 开关。动画事件做简单的点头/弹跳（有 Animator 时转发 Trigger）。
    /// </summary>
    public class CarSkin3D : MonoBehaviour, ICarSkin
    {
        [Tooltip("Optional: attachments such as straps, toggled by name")]
        public GameObject[] accessories;
        [Tooltip("Optional: Animator; receives SetTrigger(event name) on events")]
        public Animator animator;
        [Tooltip("Model node (for nod / tilt animation)")]
        public Transform body;
        [Tooltip("Mystery car cloth cover layer; bound automatically by the car model setup tool.")]
        public CarRevealCover revealCover;
        [Tooltip("Strap roof attachment (suction cups + hose); bound automatically by the car model setup tool.")]
        public CarStrap strap;
        [Tooltip("Shared car appearance config; colors, windows, light strips and gloss all come from this asset and can be tuned while playing.")]
        public SkinConfig appearance;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Glossiness");
        static readonly int SpecularId = Shader.PropertyToID("_SpecColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int DepthId = Shader.PropertyToID("_CarDepth");
        // 多网格模型必须逐个材质槽处理；只染第一个网格会留下 FBX 的橙色。
        Renderer[] renderers;
        Material[][] materials;
        MaterialPropertyBlock mpb;
        float tilt, tiltVel;
        Quaternion bodyBase;

        void Awake()
        {
            if (body != null) bodyBase = body.localRotation;
        }

        public void SetCarColor(Color color, bool hidden, int colorIndex)
        {
            if (appearance == null) return;
            var profile = appearance.GetCarAppearance(hidden ? -1 : colorIndex);
            // 显色期间先保留布料，等待 reveal 动画结束后再隐藏覆盖层。
            revealCover ??= GetComponentInChildren<CarRevealCover>(true);
            if (revealCover != null)
            {
                if (hidden) revealCover.SetCovered(true);
                else if (!revealCover.IsRevealing) revealCover.SetCovered(false);
            }
            // 绑带附件跟随车漆：颜色与光泽取该颜色编号的外观配置，问号车被盖住时同步隐藏。
            strap ??= GetComponentInChildren<CarStrap>(true);
            if (strap != null)
            {
                strap.SetCovered(hidden);
                strap.SetPaint(color, profile.carSmoothness, profile.carSpecular);
            }
            // 首次使用时缓存，之后实时调节无需重复获取材质数组；覆盖层与绑带附件各自管理材质。
            if (renderers == null)
            {
                var allRenderers = GetComponentsInChildren<Renderer>(true);
                int count = 0;
                foreach (var candidate in allRenderers)
                    if (!IsOverlayRenderer(candidate.transform)) count++;
                renderers = new Renderer[count];
                int index = 0;
                foreach (var candidate in allRenderers)
                    if (!IsOverlayRenderer(candidate.transform)) renderers[index++] = candidate;
                materials = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++) materials[i] = renderers[i].sharedMaterials;
            }
            // 问号车的隐藏状态由车衣单独承担，车身渲染器必须关闭，避免车衣网格边缘露出车头或车尾。
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = !hidden;
            mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var applied = renderers[i].sharedMaterials;
                bool changed = false;
                for (int slot = 0; slot < materials[i].Length; slot++)
                {
                    if (materials[i][slot] == null) continue;
                    string materialName = materials[i][slot].name;
                    var replacement = CarMaterialColors.ResolveMaterial(materialName, profile);
                    // 深度 Shader 是车辆跨网格排序的必要条件，拒绝不兼容材质。
                    if (replacement != null && replacement.shader.name != CarMaterialColors.ShaderName)
                        replacement = null;
                    var target = replacement != null ? replacement : materials[i][slot];
                    if (applied[slot] != target) { applied[slot] = target; changed = true; }
                    renderers[i].GetPropertyBlock(mpb, slot);
                    float depth = mpb.HasFloat(DepthId) ? mpb.GetFloat(DepthId) : -2f;
                    mpb.Clear(); // 清掉旧色号覆盖，让材质自身参数立即生效。
                    mpb.SetFloat(DepthId, depth);
                    if (replacement != null)
                    {
                        // Replacement materials keep the authored parts and shaders,
                        // while the normalized level palette remains the source of truth
                        // for the visible body color.
                        if (materialName == "car_base" || materialName == "car_jingzi")
                        {
                            mpb.SetColor(ColorId, color);
                            mpb.SetColor(BaseColorId, color);
                        }
                        renderers[i].SetPropertyBlock(mpb, slot);
                        continue;
                    }
                    var c = CarMaterialColors.Resolve(materialName, color, profile);
                    c.a = 1;
                    mpb.SetColor(ColorId, c);
                    mpb.SetColor(BaseColorId, c);
                    mpb.SetFloat(SmoothnessId, profile.carSmoothness);
                    mpb.SetColor(SpecularId, Color.white * profile.carSpecular);
                    mpb.SetColor(EmissionId, Color.black);
                    renderers[i].SetPropertyBlock(mpb, slot);
                }
                if (changed) renderers[i].sharedMaterials = applied;
            }
        }

        /// <summary>按车辆所在区域选择问号车布料的初始姿态。</summary>
        public void SetRevealArea(bool upperArea)
        {
            revealCover ??= GetComponentInChildren<CarRevealCover>(true);
            revealCover?.SetUpperArea(upperArea);
        }

        /// <summary>绑带配对状态：CarView 配对成功后转发，只有成对车辆才显示车顶吸盘。</summary>
        public void SetLinked(bool on, CarStrap partner)
        {
            strap ??= GetComponentInChildren<CarStrap>(true);
            strap?.SetLinked(on, partner);
        }

        /// <summary>覆盖层与绑带附件各自管理材质，不参与车漆批量上色。</summary>
        bool IsOverlayRenderer(Transform t) =>
            (revealCover != null && t.IsChildOf(revealCover.transform)) ||
            (strap != null && t.IsChildOf(strap.transform));

        public void SetAccessory(string name, bool on)
        {
            if (accessories == null) return;
            foreach (var a in accessories) if (a != null && a.name == name) a.SetActive(on);
        }

        public void PlayEvent(string evt)
        {
            if (animator != null) animator.SetTrigger(evt);
            if (evt == "reveal" && revealCover != null) revealCover.PlayReveal();
            switch (evt)
            {
                case "tap": tiltVel -= 260f; break;   // 起步抬头
                // "park" 的刹车前倾由 CarView.Brake 统一处理（所有停车都会触发），这里不再叠加
                case "push": tiltVel += 300f; break;
                case "reveal": tiltVel -= 150f; break;
            }
        }

        void Update()
        {
            if (body == null) return;
            // 弹簧：车身绕左右轴前后摆动
            float rest = Mathf.Min(Time.deltaTime, 0.1f);
            while (rest > 1e-5f)
            {
                float h = Mathf.Min(rest, 1f / 120f);
                tiltVel += (-tilt * 180f - tiltVel * 14f) * h;
                tilt += tiltVel * h;
                rest -= h;
            }
            body.localRotation = bodyBase * Quaternion.Euler(tilt, 0, 0);
        }
    }
}
