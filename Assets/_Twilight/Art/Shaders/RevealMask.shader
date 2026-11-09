// 世界铺开的模板遮罩：球面上已铺开的部分写模板 1（和窗洞遮罩一样），村庄就画在那里。不画颜色。放 Portal 层。
Shader "Twilight/RevealMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Front
            Stencil { Ref 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TKReveal.hlsl"
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
                clip(-TKRevealDist(normalize(i.ws - _WorldSpaceCameraPos)));
                return 0;
            }
            ENDHLSL
        }
    }
}
