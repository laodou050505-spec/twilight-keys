// 世界铺开的光边：边界外侧一圈暖金色柔光 + 闪点，像颜料在纸上洇开时的亮边。画在真实房间（透视）和村庄之上，琴和桌子之下。
Shader "Twilight/RevealRim"
{
    Properties
    {
        _Color ("颜色", Color) = (1.0, 0.93, 0.8, 1)
        _Width ("宽度（弧度）", Float) = 0.018
        _Intensity ("亮度", Float) = 1.4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TKReveal.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Width, _Intensity;
            CBUFFER_END
            float _TK_BeatPulse;
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz);
                v.pos = TransformWorldToHClip(v.ws);
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_TK_Reveal <= 0.001 || _TK_Reveal >= 0.999) discard;
                float3 d = normalize(i.ws - _WorldSpaceCameraPos);
                float x = TKRevealDist(d);
                if (x < 0) discard;                                       // 只在还没铺开的一侧发光
                float band = exp(-x * x / (_Width * _Width));
                float3 g = d * 220.0; float3 gi = floor(g);
                float h = TKRHash(gi);
                float star = step(0.97, h) * saturate(1 - length(frac(g) - 0.5) * 2.4) * (0.5 + 0.5 * sin(_Time.y * (3 + h * 6) + h * 50));
                float a = saturate(band * 0.3 * (0.7 + _TK_BeatPulse * 0.6) + star * saturate(band * 3.0) * 1.6);
                clip(a - 0.003);
                return half4(_Color.rgb * _Intensity * (0.8 + star * 1.5), a);
            }
            ENDHLSL
        }
    }
}
