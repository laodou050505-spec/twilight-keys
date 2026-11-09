// 指尖光点：uv -1..1 的方片，uv2.x = 空心程度（0 实心点 + 柔光，1 细圈）。顶点色 = 颜色 × 透明度。叠加发光，永远画在最上面（手是透视里的真手，不会被挡）。
Shader "Twilight/FingerMark"
{
    SubShader
    {
        Tags { "Queue" = "Transparent+40" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha One, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float ring : TEXCOORD1; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.ring = a.uv2.x; v.col = a.col;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.uv), aa = fwidth(d) * 1.2 + 1e-4;
                float ringLine = 1 - smoothstep(0.08 - aa, 0.08 + aa, abs(d - 0.82));
                float dot_ = (1 - smoothstep(0.45 - aa, 0.45 + aa, d)) + exp(-d * d * 4) * 0.35;
                float m = lerp(dot_, ringLine * 0.8, i.ring) * (1 - smoothstep(0.92, 1.0, d));
                half a = saturate(m) * i.col.a;
                return half4(i.col.rgb * a * 1.6, a);
            }
            ENDHLSL
        }
    }
}
