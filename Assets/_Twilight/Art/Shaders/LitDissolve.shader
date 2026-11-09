// 消散：窗户 / 石墙 / 百叶窗 / 花箱在世界铺满后慢慢淡去（全局 _TK_WindowFade：0 完整 … 1 消失）。
// 第十六轮（用户：一点点消散别那么丑，像着火了）：不再镂空、不再有发光烧边——整件东西从下往上一大片柔和地变透明，
// 过渡带占物体高度的 45%，只带一点很淡的云状起伏，看起来像雾散开。
Shader "Twilight/LitDissolve"
{
    Properties
    {
        _BaseMap ("贴图", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1, 1, 1, 1)
        _Smoothness ("光滑", Range(0, 1)) = 0.3
        _EmissionMap ("自发光图", 2D) = "black" {}
        _EmissionColor ("自发光", Color) = (0, 0, 0, 1)
        _EdgeColor ("消散边缘光", Color) = (1.0, 0.82, 0.5, 1)
        _MinY ("物体底（世界 y）", Float) = 0
        _MaxY ("物体顶（世界 y）", Float) = 2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor, _EmissionColor, _EdgeColor; half _Smoothness; float _MinY, _MaxY;
            CBUFFER_END
            float _TK_WindowFade;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float3 n : TEXCOORD2; float fog : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz); v.pos = TransformWorldToHClip(v.ws);
                v.n = TransformObjectToWorldNormal(a.n); v.uv = TRANSFORM_TEX(a.uv, _BaseMap); v.fog = ComputeFogFactor(v.pos.z);
                return v;
            }
            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + 1), f.x), f.y), f.z);
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // 从下往上（高度占 85%）+ 很淡的大块云状起伏（15%），过渡带宽 0.45：一大片慢慢变透明
                float h = saturate((i.ws.y - _MinY) / max(0.01, _MaxY - _MinY));
                float n = Noise(i.ws * 2.2);
                float k = h * 0.85 + n * 0.15;                  // 0..1
                float edge = _TK_WindowFade * 1.5 - 0.45;       // 0 → 全在；1 → 全散
                float alpha = smoothstep(0.0, 0.45, k - edge);
                clip(alpha - 0.004);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 N = normalize(i.n), Vd = normalize(_WorldSpaceCameraPos - i.ws);
                Light L = GetMainLight();
                half nl = saturate(dot(N, L.direction) * 0.8 + 0.2);
                half spec = pow(saturate(dot(N, normalize(L.direction + Vd))), 8 + _Smoothness * 90) * _Smoothness;
                half3 c = tex.rgb * (L.color * nl + SampleSH(N)) + L.color * spec * 0.4;
                c += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, i.uv).rgb * _EmissionColor.rgb;
                // 快散完的地方稍微提亮一点点（像被暮光照透），不发光、不烧边
                c = lerp(c, c * 1.12 + _EdgeColor.rgb * 0.04, (1 - alpha) * step(0.001, _TK_WindowFade));
                return half4(MixFog(c, i.fog), alpha * tex.a);
            }
            ENDHLSL
        }
    }
}
