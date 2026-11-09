// 天上的光河（银河）：从窗口接着屋里的光河，往村子的天空升上去、越来越宽。挂在 Village 层（只在窗洞 / 已铺开的地方看得见）。
// uv.x 横向 0..1，uv.y = t（0 窗口 → 1 天上的源头），uv2 = (离窗口的米数, 总长)。
// 近窗口：四条轨道的颜色；越往上越融成一整片星云；源头用噪声慢慢碎成星点、消失在夜空里（不是一刀切）。
// 开场“从天上飘下来”：_TK_RiverHide 1 → 0，显示前沿从源头往窗口推（屋里那段之后接着往琴键推）。
Shader "Twilight/SkyRiver"
{
    Properties
    {
        _C0 ("轨道 1", Color) = (1.0, 0.77, 0.42, 1)
        _C1 ("轨道 2", Color) = (1.0, 0.56, 0.37, 1)
        _C2 ("轨道 3", Color) = (0.95, 0.40, 0.55, 1)
        _C3 ("轨道 4", Color) = (0.48, 0.72, 1.0, 1)
        _Nebula ("高处星云色", Color) = (0.62, 0.55, 1.0, 1)
        _Alpha ("透明度", Range(0, 1)) = 0.7
        _Glow ("亮度", Float) = 1.9
        _Flow ("流速（t / 秒）", Float) = 0.012
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+2" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha One, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _C0, _C1, _C2, _C3, _Nebula; half _Alpha, _Glow, _Flow;
            CBUFFER_END
            float _TK_RiverHide, _TK_BeatPulse, _TK_Heat;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 m : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz); v.uv = a.uv; v.m = a.uv2;
                return v;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            float Fbm(float2 p) { return VNoise(p) * 0.55 + VNoise(p * 2.07 + 3.1) * 0.3 + VNoise(p * 4.3 + 7.7) * 0.15; }
            half3 Lane(int l) { return l == 0 ? _C0.rgb : l == 1 ? _C1.rgb : l == 2 ? _C2.rgb : _C3.rgb; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float x = i.uv.x, t = i.uv.y, tm = _Time.y;
                // 往下流（朝窗口）
                float2 q = float2(x * 5.0, (t + tm * _Flow) * 26.0);
                float2 w = float2(Fbm(q * 0.7 + tm * 0.03), Fbm(q * 0.9 - 2.3));
                float neb = Fbm(q + w * 1.8);
                float wisps = smoothstep(0.38, 0.9, neb);
                // 颜色：近窗口四条轨道 → 高处整片星云
                float lp = x * 4.0; int l = (int)clamp(floor(lp), 0, 3);
                half3 laneCol = lerp(Lane(l), Lane(clamp(l + (frac(lp) > 0.5 ? 1 : -1), 0, 3)), pow(abs(frac(lp) - 0.5) * 2, 3) * 0.4);
                half3 col = lerp(laneCol, lerp(_Nebula.rgb, laneCol, 0.35 + 0.3 * neb), smoothstep(0.01, 0.16, t));
                // 星点：越往上越密、越小
                float2 g = float2(x * 60.0, (t + tm * _Flow * 1.6) * 900.0);
                float2 gi = floor(g), gf = frac(g) - 0.5;
                float h = Hash(gi);
                float star = step(0.94, h) * smoothstep(0.3, 0.0, length(gf)) * (0.5 + 0.5 * sin(tm * (1.5 + h * 5) + h * 50));
                // 横向：中间实、边缘羽化（高处边缘更散）
                float edge = 1 - pow(abs(x - 0.5) * 2, lerp(3.0, 1.4, t));
                edge *= smoothstep(0.0, 0.08 + 0.25 * t, min(x, 1 - x) + (wisps - 0.5) * 0.12 * t);
                // 源头：噪声打碎、慢慢消失在夜空（自然过渡）
                float head = smoothstep(1.0, 0.55, t + (neb - 0.5) * 0.35);
                float headStars = smoothstep(1.0, 0.7, t) * (1 - head);
                // 窗口端：和屋里那段交接（前 8 cm 淡入）
                float joint = smoothstep(0.0, 0.08, i.m.x);
                // 开场：显示前沿从源头往窗口推，前沿发亮
                float drop = saturate((1 - _TK_RiverHide) / 0.5);
                float front = 1.08 * (1 - drop) - 0.05;
                float vis = smoothstep(front - 0.03, front + 0.02, t);
                float frontGlow = exp(-pow((t - front) / 0.025, 2)) * (drop < 0.999);
                half3 c = col * (0.35 + 0.65 * wisps) * edge * head + star * (1.2 + headStars * 2) + col * frontGlow * 1.5;
                c *= 1 + _TK_BeatPulse * 0.15 * (1 - t) + _TK_Heat * 0.3;
                half a = (_Alpha * (0.35 + 0.5 * wisps) * edge * head + star * 0.6 * (head + headStars) + frontGlow * 0.4) * joint * vis;
                return half4(c * _Glow, saturate(a));
            }
            ENDHLSL
        }
    }
}
