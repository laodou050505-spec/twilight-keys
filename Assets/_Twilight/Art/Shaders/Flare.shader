// 击中星芒：柔和光核 + 十字长光芒 + 斜向短光芒，光芒边缘带一点彩虹色散。顶点色 = 色调 × 透明度。uv 0..1，面向玩家的方片。
Shader "Twilight/Flare"
{
    Properties { _Boost ("亮度", Float) = 2.4 }
    SubShader
    {
        Tags { "Queue" = "Transparent+32" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            ZTest Always
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
            float Rays(float2 p, float w)
            {
                float2 q = abs(p);
                float cross = exp(-q.y / w) * smoothstep(1.0, 0.0, q.x) + exp(-q.x / w) * smoothstep(1.0, 0.0, q.y);
                float2 d = float2(p.x + p.y, p.x - p.y) * 0.7071;
                float2 qd = abs(d);
                float diag = (exp(-qd.y / w) * smoothstep(0.5, 0.0, qd.x) + exp(-qd.x / w) * smoothstep(0.5, 0.0, qd.y)) * 0.45;
                return cross + diag;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = (i.uv - 0.5) * 2;
                float r = length(p);
                float core = exp(-r * r / 0.012) + exp(-r / 0.12) * 0.45;
                // 色散：红 / 蓝的光芒稍微粗细不同
                float rr = Rays(p, 0.022), rg = Rays(p, 0.017), rb = Rays(p, 0.013);
                half3 rays = half3(rr, rg, rb) * smoothstep(1.0, 0.2, r);
                half3 c = (i.col.rgb * core + lerp(i.col.rgb, 1, 0.6) * rays) * _Boost;
                half a = saturate((core + rg) * i.col.a);
                return half4(c * i.col.a, a * 0.8);
            }
            ENDHLSL
        }
    }
}
