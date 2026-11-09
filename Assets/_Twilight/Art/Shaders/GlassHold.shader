// 长按光条：沿光河弯曲的玻璃条（uv.x 横向 0..1，uv.y 沿条 0 = 头（靠琴键）… 1 = 尾），中间一条亮芯。
// _Consumed：已按住吃掉的比例（从头开始变成明亮的光，像被弹奏点燃）；_Active：正在按着（整体更亮、脉动）。
Shader "Twilight/GlassHold"
{
    Properties
    {
        _Color ("轨道色", Color) = (1, 0.77, 0.42, 1)
        _Consumed ("已按住比例", Range(0, 1)) = 0
        _Active ("正在按着", Range(0, 1)) = 0
        _Fade ("淡入淡出", Range(0, 1)) = 1
        _Broken ("松手断开", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Consumed, _Active, _Fade, _Broken;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float3 n : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz); v.pos = TransformWorldToHClip(v.ws);
                v.uv = a.uv; v.n = TransformObjectToWorldNormal(a.n);
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float x = abs(i.uv.x - 0.5) * 2;                 // 0 中心 … 1 边
                float3 Vd = normalize(_WorldSpaceCameraPos - i.ws);
                float fr = pow(1 - saturate(abs(dot(normalize(i.n), Vd))), 3);
                float core = exp(-x * x * 9);                      // 中间亮芯
                float edge = smoothstep(0.75, 1, x);
                float t = _Time.y;
                // 亮芯里流动的光点（往琴键方向流）
                float flow = 0.5 + 0.5 * sin(i.uv.y * 40 + t * 7);
                half3 frost = lerp(_Color.rgb, 1, 0.25);
                half3 c = frost * 0.6 + _Color.rgb * core * (2.0 + flow * 0.6) + edge * 1.0 + fr * 0.5;
                half a = saturate(0.45 + core * 0.45 + edge * 0.5 + fr * 0.3);
                // 已按住的一段：点燃成明亮的光
                float lit = step(i.uv.y, _Consumed);
                c = lerp(c, (_Color.rgb * 1.6 + 0.5) * (1.2 + 0.3 * sin(t * 18)), lit * 0.85);
                a = lerp(a, saturate(a + 0.25), lit);
                c *= 1 + _Active * 0.4;
                c = lerp(c, half3(0.55, 0.55, 0.6), _Broken * 0.7);
                a *= lerp(1, 0.45, _Broken);
                // 尾部收尖一点、两端柔和
                a *= smoothstep(0, 0.04, i.uv.y) * smoothstep(1, 0.92, i.uv.y);
                return half4(c, a * _Fade);
            }
            ENDHLSL
        }
    }
}
