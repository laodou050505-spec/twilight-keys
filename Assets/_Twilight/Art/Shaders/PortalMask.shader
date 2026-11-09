// 窗洞遮罩：不画颜色，只在模板缓冲里写 1。村庄（Village 层）只在模板 = 1 的像素里画（URP 渲染器里的 RenderObjects 步骤）。
Shader "Twilight/PortalMask"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "PortalMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            Cull [_Cull]
            Stencil { Ref 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz);
                return v;
            }
            half4 frag(V v) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
