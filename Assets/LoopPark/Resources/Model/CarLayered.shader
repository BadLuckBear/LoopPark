// 保留卡通车的光照与网格内部深度，跨车辆使用统一的二维遮挡顺序。
Shader "LoopPark/Layered Car"
{
    Properties
    {
        _Color ("车身颜色", Color) = (1,1,1,1)
        _MainTex ("颜色贴图", 2D) = "white" {}
        [Normal] _BumpMap ("法线贴图", 2D) = "bump" {}
        _BumpScale ("法线强度", Range(0,2)) = 1
        _OcclusionMap ("环境遮蔽贴图", 2D) = "white" {}
        _OcclusionStrength ("遮蔽强度", Range(0,1)) = 1
        [HDR] _EmissionColor ("发光颜色", Color) = (0,0,0,1)
        _EmissionMap ("发光贴图", 2D) = "white" {}
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
        sampler2D _MainTex, _BumpMap, _OcclusionMap, _EmissionMap;
        half _BumpScale, _OcclusionStrength;
        half4 _EmissionColor;
        float _CarDepth;
        struct Input { float2 uv_MainTex; float2 uv_BumpMap; float2 uv_OcclusionMap; float2 uv_EmissionMap; };
        void vert(inout appdata_full v)
        {
            float4 world = mul(unity_ObjectToWorld, v.vertex);
            // 保留足够深度精度，避免车身细节 Z-fighting；厚度仍小于相邻车的排序间隔。
            world.z = _CarDepth + world.z * 0.02;
            v.vertex = mul(unity_WorldToObject, world);
        }
        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb;
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_BumpMap), _BumpScale);
            o.Specular = _SpecColor.rgb;
            o.Smoothness = _Glossiness;
            o.Occlusion = lerp(1, tex2D(_OcclusionMap, IN.uv_OcclusionMap).g, _OcclusionStrength);
            o.Emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb * _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
