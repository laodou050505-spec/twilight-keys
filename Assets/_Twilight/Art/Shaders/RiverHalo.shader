// 光河光晕：河面下方一条更宽、很淡的光带，模拟泛光（头显上不开后处理）。uv.x 0..1 横跨（比河宽 1.7 倍），uv.y 弧长。
Shader "Twilight/RiverHalo"
{
    Properties
    {
        _C0 ("轨道 1", Color) = (1.0, 0.77, 0.42, 1)
        _C1 ("轨道 2", Color) = (1.0, 0.56, 0.37, 1)
        _C2 ("轨道 3", Color) = (0.95, 0.40, 0.55, 1)
        _C3 ("轨道 4", Color) = (0.48, 0.72, 1.0, 1)
        _Strength ("强度", Range(0, 1)) = 0.22
        _Widen ("比河宽", Float) = 1.7
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-5" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float total : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a) { V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v); v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.total = a.uv2.x; return v; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // 映射回河面横向位置（-0.35..1.35 → 轨道），颜色平滑过渡
                float rx = (i.uv.x - 0.5) * _Widen + 0.5;
                float lp = saturate(rx) * 4 - 0.5;
                half3 c01 = lerp(_C0.rgb, _C1.rgb, saturate(lp)), c23 = lerp(_C2.rgb, _C3.rgb, saturate(lp - 2));
                half3 col = lerp(c01, c23, saturate(lp - 1));
                float x = abs(i.uv.x - 0.5) * 2;
                float a = exp(-x * x * 3.2) * _Strength * (1 + _TK_BeatPulse * 0.5);
                a *= smoothstep(-0.02, 0.12, i.uv.y) * smoothstep(i.total, i.total - 0.5, i.uv.y);
                a *= saturate((1 - _TK_RiverHide - 0.55) / 0.45);      // 开场：光河流到琴键时一起亮起来
                return half4(col * 1.5, a);
            }
            ENDHLSL
        }
    }
}
