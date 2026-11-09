// 小岛上的湖面（几厘米大的小湖，近看）：真实一点的水——
// 深浅：中间深青、岸边浅（按到湖心的距离，uv 0..1 是 Blender 里 TRIFAN 圆盘的平面投影）；
// 细波纹：两层流动的程序法线（很细、很慢），带出高光闪点；
// 菲涅尔：斜着看反射暖色夜空、正对看透出湖底；一条柔和的月光高光；岸边一圈淡淡的白边。
Shader "Twilight/IsleWater"
{
    Properties
    {
        _Deep ("深水色", Color) = (0.08, 0.26, 0.32, 1)
        _Shallow ("浅水色", Color) = (0.3, 0.6, 0.62, 1)
        _Sky ("反射的天色", Color) = (0.95, 0.72, 0.55, 1)
        _Ripple ("波纹尺度", Float) = 90
        _Speed ("流速", Float) = 0.035
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+5" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Shallow, _Sky; half _Ripple, _Speed;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; float3 lp : TEXCOORD1; float3 nw : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz); v.pos = TransformWorldToHClip(v.ws);
                v.lp = a.pos.xyz; v.nw = TransformObjectToWorldNormal(a.n);
                return v;
            }
            float H(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float N(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H(i), H(i + float2(1, 0)), f.x), lerp(H(i + float2(0, 1)), H(i + float2(1, 1)), f.x), f.y);
            }
            float Waves(float2 p, float t)
            {
                return N(p + float2(t, t * 0.6)) * 0.6 + N(p * 2.1 - float2(t * 0.8, -t * 0.4)) * 0.3 + N(p * 4.3 + t) * 0.1;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 Nw = normalize(i.nw);
                // 网格本地：圆盘在 xy 平面（Blender 坐标），半径约 0.029
                float2 p = i.lp.xy;
                float r = saturate(length(p) / 0.029);
                float t = _Time.y * _Speed * 40;
                float2 q = p * _Ripple;
                float e = 0.02;
                float h0 = Waves(q, t), hx = Waves(q + float2(e, 0), t), hy = Waves(q + float2(0, e), t);
                float3 bump = normalize(float3(-(hx - h0) / e * 0.06, -(hy - h0) / e * 0.06, 1));
                // 把扰动加到世界法线上（水面朝上）
                float3 n = normalize(Nw + float3(bump.x, 0, bump.y) * 0.5);
                float3 Vd = normalize(_WorldSpaceCameraPos - i.ws);
                float fres = 0.04 + 0.96 * pow(1 - saturate(dot(n, Vd)), 5);
                half3 body = lerp(_Deep.rgb, _Shallow.rgb, smoothstep(0.35, 1.0, r));
                // 反射：上方暖、地平线更亮；月光高光（从右上来）
                float3 R = reflect(-Vd, n);
                half3 sky = lerp(_Sky.rgb * 0.55, _Sky.rgb * 1.15, saturate(1 - R.y));
                float3 Ld = normalize(float3(0.4, 0.75, 0.5));
                float spec = pow(saturate(dot(R, Ld)), 220) * 3 + pow(saturate(dot(R, Ld)), 30) * 0.25;
                half3 c = lerp(body, sky, saturate(fres * 1.4));
                c += half3(1, 0.93, 0.8) * spec;
                // 岸边一圈淡淡的白边（泡沫 / 浅滩反光），随波纹轻轻动
                float shore = smoothstep(0.82, 0.98, r) * (0.55 + 0.45 * h0);
                c = lerp(c, half3(0.86, 0.9, 0.86), shore * 0.35);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
