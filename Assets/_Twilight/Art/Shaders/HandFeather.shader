// 手部透出（第十九轮，用户：手的剪影太难看）：在所有虚拟物体画完以后，按手部关节画一组细胶囊，
// 把手所在位置的虚拟画面“擦淡”（颜色和透明度一起往 0 收），露出透视里的真手。
// 胶囊中间（正对视线）擦得最干净，往边缘越来越淡 —— 软边，不是原来那种一刀切的胶囊形硬边；
// 只擦在手前面的东西后面的部分（ZTest：虚拟物体比手近就保留，比如手上方飘过的音块）。
Shader "Twilight/HandFeather"
{
    Properties
    {
        _Strength ("擦除强度", Range(0, 1)) = 1
        _Soft ("边缘柔和（越大越软）", Range(0.05, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend Zero OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Strength, _Soft;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz); v.pos = TransformWorldToHClip(v.ws); v.n = TransformObjectToWorldNormal(a.n);
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float nv = saturate(dot(normalize(i.n), normalize(_WorldSpaceCameraPos - i.ws)));
                half a = smoothstep(0.0, _Soft, nv) * _Strength;     // 正对视线 = 1（完全透出真手），边缘 → 0（虚拟画面）
                return half4(0, 0, 0, a);
            }
            ENDHLSL
        }
    }
}
