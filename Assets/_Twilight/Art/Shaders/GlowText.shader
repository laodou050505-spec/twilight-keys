// 判定字 / 里程碑大字：字体贴图 alpha × 顶点色，外加一圈柔光（字体贴图没有距离场，用多次偏移采样近似）。MR 用的 alpha 覆盖混合。
Shader "Twilight/GlowText"
{
    Properties
    {
        _MainTex ("字体贴图", 2D) = "white" {}
        _Glow ("外发光", Float) = 0.8
        _GlowSize ("发光半径（贴图 uv）", Float) = 0.0035
        _Shimmer ("流光（封面标题）", Range(0, 1)) = 0
        _ShimmerSpan ("流光扫过的半宽（米）", Float) = 0.25
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+40" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half _Glow, _GlowSize, _Shimmer, _ShimmerSpan;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; float ox : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col; v.ox = a.pos.x;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half core = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                // 柔光：两圈、各 8 个方向错开采样（一圈不够会出重影）
                half halo = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float an = k * 0.785398;
                    float2 d = float2(cos(an), sin(an));
                    float2 d2 = float2(cos(an + 0.3927), sin(an + 0.3927));
                    halo += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + d * _GlowSize * 0.55).a * 0.6;
                    halo += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + d2 * _GlowSize).a * 0.4;
                }
                halo = saturate(halo / 8 * 1.5);
                halo *= halo;
                half3 c = lerp(i.col.rgb, 1, core * 0.35);
                half a = saturate(core + halo * _Glow * (1 - core)) * i.col.a;
                c = lerp(i.col.rgb * 1.2, c, core);
                // 流光：一道斜光每 6 秒从左扫到右
                float sweep = (frac(_Time.y / 6.0) * 2.6 - 1.3) * _ShimmerSpan;
                c += _Shimmer * exp(-pow((i.ox - sweep) / (0.12 * _ShimmerSpan), 2)) * core * 0.9;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
