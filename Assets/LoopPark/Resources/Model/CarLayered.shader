// 保留卡通车的光照与网格内部深度，跨车辆使用统一的二维遮挡顺序。
Shader "LoopPark/Layered Car"
{
    Properties
    {
        _Color ("车身颜色", Color) = (1,1,1,1)
        _SpecColor ("高光颜色", Color) = (0.08,0.08,0.08,1)
        _Glossiness ("光滑度", Range(0,1)) = 0.18
        _CarDepth ("整车深度", Float) = -2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf StandardSpecular vertex:vert noforwardadd
        #pragma target 3.0
        fixed4 _Color;
        half _Glossiness;
        float _CarDepth;
        struct Input { float3 worldPos; };
        void vert(inout appdata_full v)
        {
            float4 world = mul(unity_ObjectToWorld, v.vertex);
            // 压缩 Z 范围，使整辆车各部件落在同一深度带，避免尾翼跨车穿透。
            world.z = _CarDepth + world.z * 0.001;
            v.vertex = mul(unity_WorldToObject, world);
        }
        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            o.Albedo = _Color.rgb;
            o.Specular = _SpecColor.rgb;
            o.Smoothness = _Glossiness;
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
