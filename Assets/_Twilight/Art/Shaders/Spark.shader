// 击中火花（粒子）：软圆点，颜色走顶点色（粒子颜色 × 寿命渐隐）。MR：颜色叠加发光，alpha 正常覆盖（透视画面里看得见）。
Shader "Twilight/Spark"
{
    Properties { _Boost ("亮度", Float) = 2.8 }
    SubShader
    {
        Tags { "Queue" = "Transparent+30" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha One, One OneMinusSrcAlpha
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
                float d = length(i.uv - 0.5) * 2;
                float core = saturate(1 - d); core *= core;
                float hot = smoothstep(0.45, 0.0, d);
                half3 c = lerp(i.col.rgb, 1, hot * 0.7) * _Boost;
                return half4(c, saturate(core * i.col.a));
            }
            ENDHLSL
        }
    }
}
