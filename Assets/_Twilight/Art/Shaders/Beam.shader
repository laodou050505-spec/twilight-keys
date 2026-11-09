// 击中光柱：从轨道往上冲的一道竖光，底部最亮、往上渐隐，左右两边一点色散。uv.x 横向 0..1，uv.y 0 底 → 1 顶。顶点色 = 色调 × 透明度。
Shader "Twilight/Beam"
{
    Properties { _Boost ("亮度", Float) = 2.0 }
    SubShader
    {
        Tags { "Queue" = "Transparent+31" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha One, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Boost;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.col = a.col;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float x = (i.uv.x - 0.5) * 2, y = i.uv.y;
                float fall = pow(saturate(1 - y), 1.6) * smoothstep(0.0, 0.04, y);
                float r = exp(-pow((x - 0.06) / 0.3, 2)), g = exp(-pow(x / 0.25, 2)), b = exp(-pow((x + 0.06) / 0.3, 2));
                float coreLine = exp(-pow(x / 0.06, 2));
                half3 c = (lerp(i.col.rgb, 1, 0.3) * half3(r, g, b) + coreLine * 0.8) * fall * _Boost;
                half a = saturate((g * 0.6 + coreLine * 0.4) * fall * i.col.a);
                return half4(c * i.col.a, a);
            }
            ENDHLSL
        }
    }
}
