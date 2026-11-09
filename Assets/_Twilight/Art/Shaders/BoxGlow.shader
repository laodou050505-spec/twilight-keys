// 八音盒的“舞台光”：一张平放（或竖放）的方片，uv -1..1，中心最亮的暖色光晕，边缘柔和淡出。叠加，不写深度。
// 用在：盒子正下方的地面光晕（让它落在空中也像有“地”）、盒子里往上打的暖光、小岛背后的光环。
Shader "Twilight/BoxGlow"
{
    Properties
    {
        _Color ("颜色", Color) = (1, 0.72, 0.4, 1)
        _Strength ("强度", Float) = 0.5
        _Falloff ("衰减", Float) = 2.2
        _Ring ("光环（0 = 实心光晕，>0 = 圆环半径）", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off Cull Off
            Blend One One, Zero One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Strength, _Falloff, _Ring;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv * 2 - 1; return v; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.uv);
                float g = _Ring > 0 ? exp(-pow((d - _Ring) / 0.12, 2)) : pow(saturate(1 - d), _Falloff);
                g *= 1 - smoothstep(0.92, 1.0, d);
                return half4(_Color.rgb * g * _Strength, 0);
            }
            ENDHLSL
        }
    }
}
