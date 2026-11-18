using UnityEngine;

namespace LoopPark
{
    /// <summary>
    /// 3D 车模型皮肤：按颜色给“车漆”材质上色；问号车用统一的问号色；
    /// 绑带等附件通过 accessories 开关。动画事件做简单的点头/弹跳（有 Animator 时转发 Trigger）。
    /// </summary>
    public class CarSkin3D : MonoBehaviour, ICarSkin
    {
        [Tooltip("可选：绑带等附件，按名字开关")]
        public GameObject[] accessories;
        [Tooltip("可选：Animator，收到事件时 SetTrigger(事件名)")]
        public Animator animator;
        [Tooltip("模型节点（用于点头/侧倾动画）")]
        public Transform body;
        [Tooltip("车辆外观共享配置；颜色、车窗、灯条和光泽均以此资源为准，可在播放时调整。")]
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
            // 首次使用时缓存，之后实时调节无需重复获取材质数组。
            if (renderers == null)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                materials = new Material[renderers.Length][];
                for (int i = 0; i < renderers.Length; i++) materials[i] = renderers[i].sharedMaterials;
            }
            mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length; i++)
            {
                var applied = renderers[i].sharedMaterials;
                bool changed = false;
                for (int slot = 0; slot < materials[i].Length; slot++)
                {
                    if (materials[i][slot] == null) continue;
                    var replacement = CarMaterialColors.ResolveMaterial(materials[i][slot].name, profile);
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
                        renderers[i].SetPropertyBlock(mpb, slot);
                        continue;
                    }
                    var c = CarMaterialColors.Resolve(materials[i][slot].name, color, profile);
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

        public void SetAccessory(string name, bool on)
        {
            if (accessories == null) return;
            foreach (var a in accessories) if (a != null && a.name == name) a.SetActive(on);
        }

        public void PlayEvent(string evt)
        {
            if (animator != null) animator.SetTrigger(evt);
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
            float dt = Time.deltaTime;
            tiltVel += (-tilt * 180f - tiltVel * 14f) * dt;
            tilt += tiltVel * dt;
            body.localRotation = bodyBase * Quaternion.Euler(tilt, 0, 0);
        }
    }
}
