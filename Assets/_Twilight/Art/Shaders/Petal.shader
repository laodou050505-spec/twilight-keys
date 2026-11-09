// 花瓣（粒子）：一片两头尖的花瓣形，中间亮、边缘柔，带一条浅色中脉。颜色走顶点色（粒子颜色 × 寿命渐隐）。
Shader "Twilight/Petal"
{
    Properties { _Boost ("亮度", Float) = 1.35 }
    SubShader
    {
        Tags { "Queue" = "Transparent+29" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Boost;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = (i.uv - 0.5) * 2;
                // 花瓣：宽度随长度方向收尖（y 方向 -1..1），一头略圆
                float halfW = 0.55 * pow(saturate(1 - p.y * p.y), 0.8) * (1 + 0.15 * p.y);
                float d = abs(p.x) / max(halfW, 1e-3);
                float body = smoothstep(1.0, 0.75, d) * smoothstep(1.0, 0.9, abs(p.y));
                float vein = exp(-pow(p.x / 0.05, 2)) * 0.25 * (1 - abs(p.y));
                half3 c = (lerp(i.col.rgb, 1, 0.25 * (1 - d)) + vein) * _Boost;
                return half4(c, saturate(body * i.col.a));
            }
            ENDHLSL
        }
    }
}
