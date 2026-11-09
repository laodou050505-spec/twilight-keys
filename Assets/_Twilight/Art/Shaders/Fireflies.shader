// 萤火虫：一张静态网格，每只是一个四边形（顶点位置 = 中心，uv = 角，uv2 = 随机数）。
// 飘动、朝向相机、闪烁全在着色器里，CPU 零开销。亮度随天色：黄昏淡、入夜亮（全局 _TK_Night）。
Shader "Twilight/Fireflies"
{
    Properties
    {
        _Color ("颜色", Color) = (1.0, 0.85, 0.42, 1)
        _Size ("大小（米）", Float) = 0.28
        _Drift ("飘动幅度（米）", Float) = 1.2
        _DayGlow ("黄昏亮度", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend One One, One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Size, _Drift, _DayGlow;
            CBUFFER_END
            float _TK_Night;
            struct Attr { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 seed : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half glow : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float t = _Time.y, s = a.seed.x * 61.7, r = a.seed.y;
                float3 c = TransformObjectToWorld(a.pos.xyz);
                c += float3(sin(t * 0.31 + s), sin(t * 0.47 + s * 1.3) * 0.45, cos(t * 0.27 + s * 0.7)) * _Drift;
                float3 toCam = normalize(_WorldSpaceCameraPos - c);
                float3 right = normalize(cross(float3(0, 1, 0), toCam)), up = cross(toCam, right);
                float2 o = (a.uv - 0.5) * _Size * (0.7 + r * 0.6);
                float3 w = c + right * o.x + up * o.y;
                v.pos = TransformWorldToHClip(w);
                v.uv = a.uv;
                // 闪烁：每只自己的节奏，缓慢呼吸
                float blink = saturate(sin(t * (0.8 + r * 1.4) + s) * 0.8 + 0.45);
                v.glow = blink * lerp(_DayGlow, 1.0, saturate(_TK_Night * 1.5));
                return v;
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.uv - 0.5) * 2;
                float g = saturate(1 - d); g = g * g * (0.4 + 0.6 * smoothstep(0.75, 1, 1 - d * 0.6));
                half a = g * i.glow;
                return half4(_Color.rgb * a * 2.2, a);
            }
            ENDHLSL
        }
    }
}
