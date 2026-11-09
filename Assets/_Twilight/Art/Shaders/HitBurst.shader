// 击中光效：贴在河面上的一张方片——扩散的光环 + 一圈放射火花 + 中心闪光。_Age 0..1（约 0.5 秒）。
Shader "Twilight/HitBurst"
{
    Properties
    {
        _Color ("颜色", Color) = (1, 0.8, 0.5, 1)
        _Age ("进度", Range(0, 1)) = 0
        _Power ("强度（PERFECT 更强）", Range(0, 1.5)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+30" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
                half4 _Color; half _Age, _Power;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv * 2 - 1;
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r = length(i.uv), ang = atan2(i.uv.y, i.uv.x);
                float age = _Age, life = 1 - age;
                float ringR = lerp(0.15, 0.95, 1 - (1 - age) * (1 - age));
                float ring = exp(-pow((r - ringR) / 0.06, 2)) * life;
                // 放射火花：12 道，长度随时间伸出
                float rays = pow(saturate(cos(ang * 6) * 0.5 + 0.5), 24) + pow(saturate(cos(ang * 6 + 1.57) * 0.5 + 0.5), 40) * 0.6;
                float rayR = lerp(0.1, 1.0, age);
                float spark = rays * smoothstep(rayR, rayR - 0.25, r) * smoothstep(rayR - 0.5, rayR - 0.2, r) * life;
                float flash = exp(-r * r * 18) * saturate(1 - age * 3);
                float a = saturate((ring * 0.9 + spark * 0.8 + flash) * _Power);
                half3 c = lerp(_Color.rgb, 1, 0.35 + flash * 0.5) * (1.4 + flash);
                clip(a - 0.004);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
