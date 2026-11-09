// 光河流光：一张网格里的小光点，沿光河流向琴键。光河路径烘在 _Path（128×1，xyz = 本地中心线，w = 宽度），全部在 GPU 算。
// uv = 方片角，uv2.x = 随机种子，uv2.y = 横向位置（-0.5..0.5，乘河宽）。
Shader "Twilight/RiverMotes"
{
    Properties
    {
        _Path ("路径", 2D) = "black" {}
        _Len ("光河总长（米）", Float) = 2.3
        _Speed ("流速（米 / 秒）", Float) = 0.22
        _Size ("大小（米）", Float) = 0.009
        _C0 ("轨道 1", Color) = (1.0, 0.77, 0.42, 1)
        _C1 ("轨道 2", Color) = (1.0, 0.56, 0.37, 1)
        _C2 ("轨道 3", Color) = (0.95, 0.40, 0.55, 1)
        _C3 ("轨道 4", Color) = (0.48, 0.72, 1.0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
            TEXTURE2D(_Path); SAMPLER(sampler_Path);
            CBUFFER_START(UnityPerMaterial)
                float _Len, _Speed, _Size; half4 _C0, _C1, _C2, _C3;
            CBUFFER_END
            float _TK_BeatPulse, _TK_RiverHide;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                float seed = a.uv2.x, lat = a.uv2.y;
                float sp = _Speed * (0.7 + 0.6 * frac(seed * 7.13));
                float k = 1 - frac(seed + _Time.y * sp / _Len);          // 1 = 窗口端 … 0 = 琴键
                float4 p = SAMPLE_TEXTURE2D_LOD(_Path, sampler_Path, float2(k * 0.996 + 0.002, 0.5), 0);
                float hgt = 0.004 + frac(seed * 3.71) * 0.035 + sin(_Time.y * 1.7 + seed * 40) * 0.004;
                float3 local = p.xyz + float3(lat * p.w, hgt, 0);
                float3 c = TransformObjectToWorld(local);
                float3 toCam = normalize(_WorldSpaceCameraPos - c);
                float3 right = normalize(cross(float3(0, 1, 0), toCam)), up = cross(toCam, right);
                float size = _Size * (0.6 + 0.8 * frac(seed * 5.3)) * (1 + _TK_BeatPulse * 0.4);
                float2 o = (a.uv - 0.5) * size;
                v.pos = TransformWorldToHClip(c + right * o.x + up * o.y);
                v.uv = a.uv;
                int lane = (int)clamp(floor((lat + 0.5) * 4), 0, 3);
                half3 col = lane == 0 ? _C0.rgb : lane == 1 ? _C1.rgb : lane == 2 ? _C2.rgb : _C3.rgb;
                float tw = 0.55 + 0.45 * sin(_Time.y * (3 + seed * 5) + seed * 60);
                float fade = smoothstep(0.0, 0.03, k) * smoothstep(1.0, 0.85, k);
                v.col = half4(lerp(col, 1, 0.45), tw * fade);
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.uv - 0.5) * 2;
                float g = saturate(1 - d); g = g * g;
                float a = g * i.col.a;
                clip(a - 0.004);
                a *= saturate((1 - _TK_RiverHide - 0.55) / 0.45);      // 开场：光河流到琴键时一起亮起来
                return half4(i.col.rgb * (1.2 + g), a);
            }
            ENDHLSL
        }
    }
}
