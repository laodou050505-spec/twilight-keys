// 字（墨字 / 浅色字）：字体贴图 alpha × 顶点色。边缘用噪声轻微吃墨（不是机器般的平滑边）；
// 浅色字压在亮光河上时，背后垫一圈很淡的深色晕（_HaloColor）保证看得清——不是发光描边。
Shader "Twilight/Letter"
{
    Properties
    {
        _MainTex ("字体贴图", 2D) = "white" {}
        _HaloColor ("衬底晕", Color) = (0.08, 0.05, 0.04, 0)
        _HaloSize ("晕半径（贴图 uv）", Float) = 0.004
        _Rough ("墨边粗糙", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+40" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _HaloColor; half _HaloSize, _Rough;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; float2 op : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col; v.op = a.pos.xy;
                return v;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half raw = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                // 吃墨：阈值随物体空间噪声抖一点（毫米级）
                float n = VNoise(i.op * 900.0) * 0.6 + VNoise(i.op * 2600.0) * 0.4;
                half core = smoothstep(0.5 - 0.22 * _Rough * n, 0.62, raw);
                half halo = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float an = k * 0.785398 + 0.39;
                    halo += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(cos(an), sin(an)) * _HaloSize).a;
                }
                halo = saturate(halo / 8 * 1.4) * _HaloColor.a;
                half a = saturate(core + halo * (1 - core)) * i.col.a;
                half3 c = lerp(_HaloColor.rgb, i.col.rgb, core / max(core + halo * (1 - core), 1e-3));
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
