// 立体草叶：一张网格里上万片草叶（顶点色 = 叶尖颜色，uv.y = 离根高度比例，uv2.x = 随机数）。
// 风吹摆动在顶点着色器里（越往叶尖摆得越大），根部压暗做出层次。双面、不透明（不排序、不混合，便宜）。
Shader "Twilight/GrassBlades"
{
    Properties
    {
        _Wind ("风力（米）", Float) = 0.07
        _WindSpeed ("风速", Float) = 1.4
        _Root ("根部暗度", Range(0, 1)) = 0.42
        _Wrap ("背光面提亮", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+5" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Wind, _WindSpeed, _Root, _Wrap;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; half4 col : COLOR; float h : TEXCOORD0; float fog : TEXCOORD1; float3 ws : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float3 w = TransformObjectToWorld(a.pos.xyz);
                float h = a.uv.y, t = _Time.y * _WindSpeed, s = a.uv2.x * 6.28;
                // 两层风：大片的阵风 + 每片自己的抖动
                float gust = sin(t + w.x * 0.21 + w.z * 0.13) * 0.6 + sin(t * 1.9 + w.x * 0.47 - w.z * 0.31) * 0.3;
                float jit = sin(t * 3.1 + s) * 0.25;
                w.xz += float2(0.8, 0.45) * (gust + jit) * _Wind * h * h;
                w.y -= abs(gust) * _Wind * 0.3 * h * h;
                v.ws = w;
                v.pos = TransformWorldToHClip(w);
                v.col = a.col; v.h = h;
                v.fog = ComputeFogFactor(v.pos.z);
                return v;
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                Light L = GetMainLight();
                // 草叶近似朝上的法线：光照柔和，不会一片亮一片黑
                float3 N = float3(0, 1, 0);
                half nl = saturate((dot(N, L.direction) + _Wrap) / (1 + _Wrap));
                half3 alb = i.col.rgb * lerp(1 - _Root, 1, saturate(i.h * 1.3));
                half3 c = alb * (L.color * nl * 0.85 + SampleSH(N));
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
