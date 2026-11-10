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
                // 第三十八轮（用户：歌单 / 按钮上的字一直在闪）：字体贴图没有 mipmap，远处缩得很小时一个像素里跨好几个字形像素，
                // 头一动就在像素之间跳。改成在像素覆盖的范围里取 4 个点平均（超采样），边缘宽度按屏幕导数自适应（字小时边更软）。
                float2 dx = ddx(i.uv), dy = ddy(i.uv);
                half raw = 0.25 * (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + 0.3 * (dx + dy)).a + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + 0.3 * (dx - dy)).a
                                 + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - 0.3 * (dx + dy)).a + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - 0.3 * (dx - dy)).a);
                // 吃墨：阈值随物体空间噪声抖一点（毫米级）。噪声比像素还细时会闪烁——按每像素覆盖多大把粗糙度淡掉
                float px = max(length(ddx(i.op)), length(ddy(i.op)));               // 一个像素覆盖多少米（物体空间）
                half rough = _Rough * saturate(1.0 - px * 1500.0);                  // 像素 > 0.7 mm 就不再吃墨
                float n = VNoise(i.op * 900.0);
                half w = max(fwidth(raw) * 0.75, 0.05);
                half mid = 0.56 - 0.18 * rough * n;
                half core = smoothstep(mid - w, mid + w, raw);
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
