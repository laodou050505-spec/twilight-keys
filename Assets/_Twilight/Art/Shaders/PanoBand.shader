// 远山带：圆弧面片上的抠图（不受光），颜色乘 _Tint 再朝地平线色雾化（越靠底越雾）。
Shader "Twilight/PanoBand"
{
    Properties
    {
        _MainTex ("贴图（带透明）", 2D) = "white" {}
        _Tint ("颜色", Color) = (1, 1, 1, 1)
        _Haze ("雾化", Range(0, 1)) = 0.25
        _HazeLow ("底部额外雾化", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-200" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TKSky.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            half4 _TK_FarTint;     // 全局：随天色变暗（TwilightSky 设置）
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _Tint; half _Haze, _HazeLow;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz);
                v.uv = TRANSFORM_TEX(a.uv, _MainTex);
                return v;
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 c = t.rgb * _Tint.rgb;
                c = lerp(c, TKHorizon(), saturate(_Haze + _HazeLow * (1 - i.uv.y)));
                c *= _TK_FarTint.rgb;
                return half4(c, t.a * _Tint.a);
            }
            ENDHLSL
        }
    }
}
