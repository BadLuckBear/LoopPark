// 绑带套管专用：与 LoopPark/Layered Car 同款光照与 _CarDepth（跨车排序），但双面渲染、且**不依赖切线**。
//
// 三个必须单独成一支的理由：
// 1) 双面：套管是开壳，单面渲染时斜看接缝会穿过管内壁（被背面剔除）看到背景，表现为管身中间一个透亮的缺口；
//    两端封口又会露出断面圆盘。Cull Off + 不建端盖时，接缝看到的是对面管的内壁（暗色），像真实软管对接。
// 2) 不写 o.Normal：Surface Shader 里一旦给 o.Normal 赋值，编译器就会强制用**网格切线**构造 TBN 矩阵
//    来求世界法线；而程序化生成的网格没有 tangents（全 0），TBN 退化 → 世界法线变成 (0,0,0)
//    → 平行光 NdotL = 0 → 整根套管只剩环境光、死暗（2026-11-24 真实事故，用户实测发现的）。
//    美术 FBX 导入时 Unity 自己算好了 tangents，所以美术弯头是亮的、代码套管是暗的，一眼能对比出来。
//    套管本身不需要法线贴图，这里直接用顶点法线受光即可（同时也省掉切线插值）。
// 3) 沿线双色接口（_Color → _ColorEnd，按 uv.y 插值）：当前调用方两端传同一个「本车车漆色」，
//    所以管子是纯色；保留这个接口只是为了将来要「两车双色过渡」时不用再改 shader。
//    注意：平均色/渐变都会造出既不属于 A 也不属于 B 的第三种颜色，用户明确否决过。
Shader "LoopPark/Layered Hose"
{
    Properties
    {
        _Color ("颜色（起点端）", Color) = (1,1,1,1)
        _ColorEnd ("颜色（终点端）", Color) = (1,1,1,1)
        _MainTex ("颜色贴图", 2D) = "white" {}
        _OcclusionMap ("环境遮蔽贴图", 2D) = "white" {}
        _OcclusionStrength ("遮蔽强度", Range(0,1)) = 1
        [HDR] _EmissionColor ("发光颜色", Color) = (0,0,0,1)
        _EmissionMap ("发光贴图", 2D) = "white" {}
        _SpecColor ("高光颜色", Color) = (0.08,0.08,0.08,1)
        _Glossiness ("光滑度", Range(0,1)) = 0.18
        _CarDepth ("整车深度", Float) = -2
        _NormalLift ("受光法线抬升", Range(0, 0.8)) = 0.42
        [HideInInspector] _CarOutlineCenter ("Car outline center", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        // 双面：管子是开壳，单面会在接缝处透出背景
        Cull Off

        CGPROGRAM
        #pragma surface surf StandardSpecular vertex:vert noforwardadd
        #pragma target 3.0
        fixed4 _Color;
        fixed4 _ColorEnd;
        half _Glossiness;
        sampler2D _MainTex, _OcclusionMap, _EmissionMap;
        half _OcclusionStrength;
        half4 _EmissionColor;
        float _CarDepth;
        // 注意：Input 里刻意没有 uv_BumpMap —— 没有法线贴图就不需要切线
        struct Input { float2 uv_MainTex; float2 uv_OcclusionMap; float2 uv_EmissionMap; };

        half _NormalLift;

        void vert(inout appdata_full v)
        {
            float4 world = mul(unity_ObjectToWorld, v.vertex);
            world.z = _CarDepth + world.z * 0.02;
            v.vertex = mul(unity_WorldToObject, world);

            // 直接朝场景主光源方向抬升法线，避免斜向管子与光线平行时吃不到直射光
            float3 lightDir = length(_WorldSpaceLightPos0.xyz) > 0.001
                ? normalize(_WorldSpaceLightPos0.xyz)
                : normalize(float3(0, 0.5, -1));
            v.normal = normalize(lerp(v.normal, lightDir, _NormalLift));
        }

        void surf(Input IN, inout SurfaceOutputStandardSpecular o)
        {
            // uv.y：0 = 管子起点（本车直口），1 = 管子终点（两车中点）。网格只有两圈顶点，插值即线性渐变。
            fixed3 tint = lerp(_Color.rgb, _ColorEnd.rgb, saturate(IN.uv_MainTex.y));
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * tint;
            // 不写 o.Normal：程序化网格没有 tangents，写了会让世界法线退化为零、平行光完全失效。
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
