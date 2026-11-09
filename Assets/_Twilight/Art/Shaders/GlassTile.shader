// 音块：磨砂玻璃块，中心透出轨道色的暖光，边缘菲涅尔亮边，顶面接主光高光。
// _Color / _Hit 走实例化属性（每块不同颜色、敲中时闪亮），可 GPU Instancing 合批。
Shader "Twilight/GlassTile"
{
    Properties
    {
        _Color ("轨道色", Color) = (1, 0.77, 0.42, 1)
        _Hit ("敲中闪光", Range(0, 1)) = 0
        _Fade ("淡入淡出", Range(0, 1)) = 1
        _Frost ("磨砂白", Range(0, 1)) = 0.3
        _Core ("内部光", Float) = 2.6
        _Size ("块尺寸（米，物体空间 x 宽 / y 长 / z 高）", Vector) = (0.066, 0.05, 0.018, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Frost, _Core; float4 _Size;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(half, _Hit)
                UNITY_DEFINE_INSTANCED_PROP(half, _Fade)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct Attr { float4 pos : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float3 os : TEXCOORD0; float3 ws : TEXCOORD1; float3 n : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.os = a.pos.xyz;
                v.ws = TransformObjectToWorld(a.pos.xyz);
                v.pos = TransformWorldToHClip(v.ws);
                v.n = TransformObjectToWorldNormal(a.n);
                return v;
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                half hit = UNITY_ACCESS_INSTANCED_PROP(Props, _Hit);
                half fade = UNITY_ACCESS_INSTANCED_PROP(Props, _Fade);
                float3 N = normalize(i.n), V = normalize(_WorldSpaceCameraPos - i.ws);
                float fr = pow(1 - saturate(dot(N, V)), 3);
                // 内部光：离块中心越近越亮（按块尺寸归一）
                float3 c = i.os - float3(0, 0, _Size.z * 0.5);       // Tile.fbx 物体空间：z 朝上
                float r = length(c / (_Size.xyz * 0.5));
                float core = exp(-r * r * 1.6);
                Light L = GetMainLight();
                float spec = pow(saturate(dot(N, normalize(L.direction + V))), 90);
                half3 frost = lerp(tint.rgb, 1, _Frost);
                // 顶面一圈白色亮边（块在河面上一眼可辨）
                float3 cn = abs(c / (_Size.xyz * 0.5));
                float rimTop = smoothstep(0.75, 0.95, max(cn.x, cn.y)) * step(0.3, N.y);
                half3 col = frost * 0.6 + tint.rgb * core * _Core + fr * 0.8 + spec * L.color * 0.6 + rimTop * 0.9;
                col *= 1 + hit * 2.2;
                half a = saturate(0.5 + core * 0.4 + fr * 0.45 + rimTop * 0.4 + hit * 0.3) * fade;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
