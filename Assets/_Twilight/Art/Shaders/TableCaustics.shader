// 光河投在木桌上的彩色光影：轨道色 + 晃动的焦散纹理，边缘柔和。uv.x 0..1 横跨（比河宽一些），uv.y 0..1 沿河（近 → 远）。
Shader "Twilight/TableCaustics"
{
    Properties
    {
        _C0 ("轨道 1", Color) = (1.0, 0.77, 0.42, 1)
        _C1 ("轨道 2", Color) = (1.0, 0.56, 0.37, 1)
        _C2 ("轨道 3", Color) = (0.95, 0.40, 0.55, 1)
        _C3 ("轨道 4", Color) = (0.48, 0.72, 1.0, 1)
        _Strength ("强度", Range(0, 1)) = 0.32
        _Widen ("比河宽", Float) = 1.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
                half4 _C0, _C1, _C2, _C3; half _Strength, _Widen;
            CBUFFER_END
            float _TK_BeatPulse, _TK_RiverHide;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.ws = TransformObjectToWorld(a.pos.xyz); v.pos = TransformWorldToHClip(v.ws); v.uv = a.uv; return v; }
            float Caustic(float2 p, float t)
            {
                // 两层错开的“水波网”：sin 网格扭一下，取亮线
                float2 q = p + float2(sin(p.y * 1.7 + t), cos(p.x * 1.3 - t * 0.8)) * 0.6;
                float c = abs(sin(q.x * 2.1) * sin(q.y * 2.3 + t * 0.5));
                return pow(1 - c, 6);
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float rx = (i.uv.x - 0.5) * _Widen + 0.5;
                float lp = saturate(rx) * 4 - 0.5;
                half3 c01 = lerp(_C0.rgb, _C1.rgb, saturate(lp)), c23 = lerp(_C2.rgb, _C3.rgb, saturate(lp - 2));
                half3 col = lerp(c01, c23, saturate(lp - 1));
                float t = _Time.y;
                float2 p = i.ws.xz * 38.0 + float2(0, t * 1.2);
                float caus = Caustic(p, t * 1.3) * 0.7 + Caustic(p * 1.6 + 5.0, -t) * 0.5;
                float edge = smoothstep(0, 0.25, i.uv.x) * smoothstep(1, 0.75, i.uv.x) * smoothstep(0, 0.15, i.uv.y) * smoothstep(1, 0.7, i.uv.y);
                float a = (0.35 + caus) * edge * _Strength * (1 + _TK_BeatPulse * 0.35);
                a *= saturate((1 - _TK_RiverHide - 0.55) / 0.45);      // 开场：光河流到琴键时一起亮起来
                return half4(col * (1.0 + caus * 1.2), a);
            }
            ENDHLSL
        }
    }
}
