// 远景替身：颜色在顶点色里（一个材质画所有替身），漫反射 + 环境光 + 雾。树 / 花丛最远一级 LOD 用。
Shader "Twilight/VertexLit"
{
    Properties { _Tint ("整体颜色", Color) = (1, 1, 1, 1) }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial) half4 _Tint; CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; half3 c : COLOR; float fog : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float3 ws = TransformObjectToWorld(a.pos.xyz); v.pos = TransformWorldToHClip(ws);
                float3 N = TransformObjectToWorldNormal(a.n);
                Light L = GetMainLight();
                half nl = saturate(dot(N, L.direction) * 0.6 + 0.4);
                v.c = a.col.rgb * _Tint.rgb * (L.color * nl + SampleSH(N));
                v.fog = ComputeFogFactor(v.pos.z);
                return v;
            }
            half4 frag(V i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return half4(MixFog(i.c, i.fog), 1); }
            ENDHLSL
        }
    }
}
