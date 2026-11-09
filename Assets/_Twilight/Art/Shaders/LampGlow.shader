// 路灯灯笼的光：面向相机的方片，里面一团暖色光核 + 一圈柔光，亮度随入夜（全局 _TK_Night）。叠加，不写深度（被房子、树正常遮住）。
Shader "Twilight/LampGlow"
{
    Properties
    {
        _Color ("颜色", Color) = (1, 0.72, 0.38, 1)
        _Boost ("亮度", Float) = 1.6
        _Day ("黄昏时亮度比例", Float) = 0.25
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+8" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
                half4 _Color; half _Boost, _Day;
            CBUFFER_END
            float _TK_Night;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                // 广告牌：以物体原点为中心，方片朝向相机
                float3 c = TransformObjectToWorld(float3(0, 0, 0));
                float s = length(TransformObjectToWorld(float3(1, 0, 0)) - c);
                float3 toCam = normalize(_WorldSpaceCameraPos - c);
                float3 r = normalize(cross(float3(0, 1, 0), toCam)), u = cross(toCam, r);
                float3 w = c + (r * (a.uv.x - 0.5) + u * (a.uv.y - 0.5)) * s + toCam * s * 0.3;
                v.pos = TransformWorldToHClip(w); v.uv = a.uv * 2 - 1;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.uv);
                float g = exp(-d * d * 18) * 1.4 + exp(-d * d * 3.5) * 0.35;
                g *= 1 - smoothstep(0.85, 1.0, d);
                float night = lerp(_Day, 1, saturate(_TK_Night * 1.3));
                return half4(_Color.rgb * g * _Boost * night, 0);
            }
            ENDHLSL
        }
    }
}
