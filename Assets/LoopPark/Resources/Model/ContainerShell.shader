// 集装箱外壳着色器（Built-in 表面着色器）。
// 用途：3D 集装箱的顶盖/墙体/立柱/角环；开启 _ClipEnabled 后按世界 Y 裁切，
// 让「顶盖沿 +Y 上移 + 超出集装箱顶边的像素被裁掉」形成卷帘门收起的效果。
// 深度：本工程车辆 shader 会把 world.z 压到固定排序带（CarLayered.shader），
// 集装箱不参与该压缩，直接使用真实 Z —— 由 ContainerView 按列内车辆深度算出
// 「背板在所有车之后、顶盖在列内最前物体之前」的深度带。
Shader "LoopPark/Container Shell"
{
    Properties
    {
        _Color ("颜色", Color) = (0.42, 0.48, 0.95, 1)
        _MainTex ("贴图", 2D) = "white" {}
        _Glossiness ("光滑度", Range(0, 1)) = 0.22
        _Metallic ("金属度", Range(0, 1)) = 0
        [Toggle] _ClipEnabled ("开启顶边裁切", Float) = 0
        _ClipTopY ("裁切世界 Y", Float) = 1000
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;
        float _ClipEnabled;
        float _ClipTopY;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // 方案：顶盖上移后，超出集装箱顶边的像素直接丢弃（addshadow 让阴影同样被裁切）。
            if (_ClipEnabled > 0.5)
                clip(_ClipTopY - IN.worldPos.y);

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
