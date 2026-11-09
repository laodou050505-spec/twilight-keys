// 眨眼遮罩（第三十六轮，用户：挪桌子时整个场景跟着动很别扭）：琴被挪得太偏、场景要搬到琴的正前方时，
// 眼前一个小球从里面画成黑色——0.25 秒变暗、场景在全黑时换位置、0.35 秒亮回来，看不到场景滑动或旋转（VR 瞬移常用的舒适做法）。
// 永远画在最前面（ZTest Always、Overlay 队列）；透明度通道按“叠加”写，全黑时连透视里的真实房间也一起盖住。
Shader "Twilight/Veil"
{
    Properties
    {
        _Color ("颜色", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay+50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz);
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return _Color;
            }
            ENDHLSL
        }
    }
}
