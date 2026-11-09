// 光点写字（萤火虫 / 花瓣）：每个点一个方片，四个顶点位置都是点中心，uv = 方片角，uv2 = (大小米, 旋转弧度)，顶点色 = 颜色 × 透明度。
// 在视空间里展开成朝向玩家的方片（双眼各自正确）。_Mode 0 = 萤火虫（发光核 + 光晕，叠加）；1 = 花瓣（两头尖的花瓣形，正常混合）。
Shader "Twilight/Dots"
{
    Properties
    {
        _Mode ("0 萤火虫 / 1 花瓣", Float) = 0
        _Boost ("亮度", Float) = 2.2
        [Enum(UnityEngine.Rendering.BlendMode)] _Src ("源混合", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("目标混合", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+33" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend [_Src] [_Dst], One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Mode, _Boost, _Src, _Dst;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float3 vc = TransformWorldToView(TransformObjectToWorld(a.pos.xyz));
                float2 c = (a.uv - 0.5) * a.uv2.x;
                float s = sin(a.uv2.y), co = cos(a.uv2.y);
                vc.xy += float2(c.x * co - c.y * s, c.x * s + c.y * co);
                v.pos = TransformWViewToHClip(vc);
                v.uv = a.uv; v.col = a.col;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = (i.uv - 0.5) * 2;
                if (_Mode < 0.5)
                {
                    float r = length(p);
                    float core = exp(-r * r / 0.03), halo = exp(-r * 3.2) * 0.45;
                    half3 c = lerp(i.col.rgb, 1, core * 0.6) * (core + halo) * _Boost;
                    return half4(c * i.col.a, saturate((core * 0.9 + halo * 0.5) * i.col.a));
                }
                float halfW = 0.55 * pow(saturate(1 - p.y * p.y), 0.8) * (1 + 0.15 * p.y);
                float d = abs(p.x) / max(halfW, 1e-3);
                float body = smoothstep(1.0, 0.78, d) * smoothstep(1.0, 0.9, abs(p.y));
                float vein = exp(-pow(p.x / 0.06, 2)) * 0.18 * (1 - abs(p.y));
                half3 c = (lerp(i.col.rgb, 1, 0.08 * (1 - d)) + vein * 0.6) * _Boost;
                return half4(c, saturate(body * i.col.a));
            }
            ENDHLSL
        }
    }
}
