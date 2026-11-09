// 天空盒：无限远，按视线方向取全局天空全景（TKSky.hlsl）。接缝在 -Z（玩家背后）。
// 窗中村的天空用 Village 层 + 传送门模板；房间里的天穹（玩法阶段）用 _StencilComp = NotEqual 避开窗洞。
Shader "Twilight/SkyPano"
{
    Properties
    {
        _Exposure ("亮度", Float) = 1
        _Squash ("地平线附近展开（窗里视角窄用 >1）", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("模板比较", Float) = 8
        _StencilRef ("模板值", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Pass
        {
            Name "SkyPano"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Front
            Stencil { Ref [_StencilRef] Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TKSky.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Exposure, _Squash;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz);
                v.pos = TransformWorldToHClip(v.ws);
                return v;
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(TKSky(normalize(i.ws - _WorldSpaceCameraPos), _Squash) * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
