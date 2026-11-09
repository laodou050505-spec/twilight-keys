// 小岛周围的“可以转”提示：贴着岛沿的一圈金色细环，上面两段带箭头的弧（像旋转符号），随小岛转向慢慢流动；手靠近时变亮。
// 网格是一个平放的圆环：uv.x 绕一圈 0..1，uv.y 从内圈 0 到外圈 1。
Shader "Twilight/SpinHint"
{
    Properties
    {
        _Color ("颜色", Color) = (1, 0.82, 0.45, 1)
        _Glow ("亮度", Float) = 0.6
        _Phase ("流动相位（随小岛转角）", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+12" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha One, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Glow, _Phase;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float w = i.uv.y - 0.5;                                 // 横向：-0.5..0.5
                // 两段弧：每半圈里 0..0.78 是弧身，0.78..0.92 是箭头，其余空着
                float a = frac(i.uv.x * 2 + _Phase * 2);
                float aa = fwidth(i.uv.x * 2) * 1.5 + 1e-4, ww = fwidth(i.uv.y) * 1.2 + 1e-4;
                float bodyW = 0.09;
                float inBody = smoothstep(0.0, 0.06, a) * (1 - smoothstep(0.78 - aa, 0.78, a));
                float lineBody = 1 - smoothstep(bodyW - ww, bodyW + ww, abs(w));
                float k = saturate((a - 0.78) / 0.14);                  // 箭头：从宽到尖
                float headW = lerp(0.42, 0.0, k);
                float inHead = step(0.78, a) * (1 - smoothstep(0.92 - aa, 0.92, a));
                float lineHead = 1 - smoothstep(headW - ww, headW + ww, abs(w));
                float shape = max(inBody * lineBody * (0.55 + 0.45 * smoothstep(0.0, 0.78, a)), inHead * lineHead);
                // 整圈一条极淡的细线，告诉人“这是一圈”
                float ring = (1 - smoothstep(0.03 - ww, 0.03 + ww, abs(w))) * 0.25;
                float m = saturate(max(shape, ring));
                half3 c = lerp(_Color.rgb, half3(1, 0.97, 0.88), shape * 0.35) * _Glow;
                return half4(c * m, m * saturate(_Glow));
            }
            ENDHLSL
        }
    }
}
