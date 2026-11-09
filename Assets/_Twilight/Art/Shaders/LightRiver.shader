// 光河：四条半透明彩带（琥珀 / 珊瑚 / 蔷薇 / 暮蓝）。
// 带内三层流动星云（域扭曲 fbm，向玩家流）+ 闪烁星点和拖尾火花 + 轨道边缘亮线；
// 拍子光栅：每拍一道柔光横线跟音块同步流向琴键（全局 _TK_BeatPhase / _TK_BeatPulse）；判定线随拍子呼吸；
// 击中时该轨道闪光（_LaneFlash）。两端淡出。MR：alpha 通道累加（透视画面里不会出黑点）。
// uv.x：0..1 横跨四条轨道；uv.y：离琴键前沿的弧长（米）；uv2.x：总长（米）。
Shader "Twilight/LightRiver"
{
    Properties
    {
        _C0 ("轨道 1", Color) = (1.0, 0.77, 0.42, 1)
        _C1 ("轨道 2", Color) = (1.0, 0.56, 0.37, 1)
        _C2 ("轨道 3", Color) = (0.95, 0.40, 0.55, 1)
        _C3 ("轨道 4", Color) = (0.48, 0.72, 1.0, 1)
        _Alpha ("透明度", Range(0, 1)) = 0.3
        _Glow ("亮度", Float) = 1.45
        _Flow ("流速（米 / 秒）", Float) = 0.14
        _Stars ("星点密度", Float) = 110
        _HitLen ("判定区长度（米）", Float) = 0.11
        _HitS ("判定线（米）", Float) = 0.055
        _BeatLen ("每拍长度（米）", Float) = 0.18
        _LaneFlash ("各轨闪光（玩法设置）", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
                half4 _C0, _C1, _C2, _C3; half _Alpha, _Glow, _Flow, _Stars, _HitLen, _HitS, _BeatLen; half4 _LaneFlash;
            CBUFFER_END
            float _TK_BeatPhase, _TK_BeatPulse, _TK_Heat, _TK_Surge, _TK_RiverHide;
            struct Attr { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float total : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.pos = TransformObjectToHClip(a.pos.xyz);
                v.uv = a.uv; v.total = a.uv2.x;
                return v;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            float Fbm(float2 p) { return VNoise(p) * 0.55 + VNoise(p * 2.07 + 3.1) * 0.3 + VNoise(p * 4.3 + 7.7) * 0.15; }
            half3 LaneColor(int lane) { return lane == 0 ? _C0.rgb : lane == 1 ? _C1.rgb : lane == 2 ? _C2.rgb : _C3.rgb; }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float lanePos = i.uv.x * 4.0;
                int lane = (int)clamp(floor(lanePos), 0, 3);
                float inLane = frac(lanePos);
                float s = i.uv.y, t = _Time.y;
                half3 col = LaneColor(lane);
                // 相邻轨道颜色在边上互相渗一点
                half3 nb = LaneColor(clamp(lane + (inLane > 0.5 ? 1 : -1), 0, 3));
                col = lerp(col, nb, pow(abs(inLane - 0.5) * 2, 4) * 0.35);
                // 星云：域扭曲 fbm，向玩家流动；沿河颜色缓慢偏移（远处更紫、近处更暖）
                float2 q = float2(i.uv.x * 6.0, (s + t * _Flow) * 4.0);
                float2 w = float2(Fbm(q + t * 0.05), Fbm(q * 1.3 - 2.0));
                float neb = Fbm(q + w * 1.6);
                float wisps = smoothstep(0.42, 0.9, neb);
                float deep = smoothstep(0.25, 0.6, Fbm(q * 0.6 + w));
                half3 hueShift = lerp(half3(1.05, 0.95, 1.12), half3(1.08, 1.0, 0.9), saturate(1 - s / max(0.3, i.total)));
                // 星点（闪烁）+ 拖尾火花（沿流向拉长）
                float2 g = float2(i.uv.x * _Stars * 0.33, (s + t * _Flow) * _Stars);
                float2 gi = floor(g), gf = frac(g) - 0.5;
                float h = Hash(gi);
                float star = step(0.92, h) * smoothstep(0.24, 0.0, length(gf)) * (0.45 + 0.55 * sin(t * (2 + h * 6) + h * 40));
                // 流速线：连击越高越多、越长、流得越快，左右一点彩虹色散（红 / 蓝错开）
                float sp = 2.4 + _TK_Heat * 2.2;
                float2 g2 = float2(i.uv.x * 20.0, (s + t * _Flow * sp) * lerp(9.0, 5.0, _TK_Heat));
                float2 gi2 = floor(g2), gf2 = frac(g2) - 0.5;
                float h2 = Hash(gi2 + 17.0);
                float on2 = step(0.965 - 0.05 * _TK_Heat, h2) * smoothstep(0.5, 0.0, abs(gf2.y)) * (0.6 + 0.4 * sin(t * 3 + h2 * 30));
                float streak = on2 * smoothstep(0.08, 0.0, abs(gf2.x));
                half3 streakRGB = on2 * half3(smoothstep(0.1, 0.0, abs(gf2.x - 0.06)), smoothstep(0.08, 0.0, abs(gf2.x)), smoothstep(0.1, 0.0, abs(gf2.x + 0.06)));
                // 轨道边缘亮线（贴着黄铜线的那一圈光）
                float edgeLine = exp(-pow(min(inLane, 1 - inLane) / 0.05, 2));
                float center = 1 - pow(abs(inLane - 0.5) * 2, 3) * 0.45;
                // 拍子光栅：每拍一道，跟音块同步流向琴键；靠近琴键时随拍子脉冲更亮
                float x = (s - _HitS) / _BeatLen + _TK_BeatPhase;
                float grid = exp(-pow((frac(x + 0.5) - 0.5) / 0.06, 2)) * smoothstep(-0.02, 0.15, s);
                float nearKeys = saturate(1 - (s - _HitS) / 1.2);
                grid *= 0.06 + 0.32 * nearKeys * _TK_BeatPulse;
                // 判定线：随拍子呼吸
                float hitLine = exp(-pow((s - _HitS) / 0.006, 2)) * (0.6 + 0.6 * _TK_BeatPulse);
                float hit = smoothstep(_HitLen + 0.07, _HitLen - 0.03, s);          // 判定区变透明：过渡拉宽，不再是一条横切的硬边
                float flash = lane == 0 ? _LaneFlash.x : lane == 1 ? _LaneFlash.y : lane == 2 ? _LaneFlash.z : _LaneFlash.w;
                half3 c = col * hueShift * (0.32 + 0.25 * deep + 0.6 * wisps) * center
                        + star * 1.6 + streakRGB * lerp(col, 1, 0.5) * 2.0 + wisps * 0.1
                        + lerp(col, 1, 0.3) * edgeLine * 0.55 + lerp(col, 1, 0.5) * grid + hitLine * lerp(col, 1, 0.6);
                float a = _Alpha * (0.5 + 0.25 * deep + 0.35 * wisps) * center + star * 0.6 + streak * 0.5 + edgeLine * 0.22 + grid * 0.25 + hitLine * 0.45;
                // 热度（连击越高越亮、火花越多）+ 里程碑涌光：一道光从琴键往窗口推过去
                c *= 1 + _TK_Heat * 0.4;
                c += (star * 1.2 + streak * lerp(col, 1, 0.5) * 1.5) * _TK_Heat;
                float surgeAt = (1 - _TK_Surge) * i.total;
                float wave = exp(-pow((s - surgeAt) / 0.1, 2)) * _TK_Surge;
                c += lerp(col, 1, 0.45) * wave * 1.6;
                a += wave * 0.35 + (star + streak) * _TK_Heat * 0.3;
                c *= 1 + hit * 0.25 + flash * 1.8;
                a = saturate(a * lerp(1, 0.4, hit) + flash * 0.3);      // 琴键段更透：看得清键被按下
                // 两端淡出：前沿 1 cm、窗口端 8 cm（外面接着天上的那段，交接处互相淡入淡出）
                float farFade = smoothstep(i.total, i.total - 0.08, s);
                // 琴键前沿：用噪声把淡入边打散（像光从琴键上慢慢漫出来），8 cm 渐入，不是一条直线
                float edgeN = Fbm(float2(i.uv.x * 9.0, t * 0.15)) - 0.5;
                float frontFade = smoothstep(-0.01, 0.085, s + edgeN * 0.035);
                a *= frontFade * farFade;
                c *= lerp(0.55, 1.0, frontFade);
                // 开场：从天上那段流下来之后，显示前沿从窗口往琴键推，前沿发亮
                float drop = saturate((1 - _TK_RiverHide - 0.45) / 0.55);
                float front = (i.total + 0.12) * (1 - drop) - 0.08;     // drop = 1 时前沿在琴键前面，整条都看得见
                float vis = smoothstep(front - 0.04, front + 0.03, s);
                float frontGlow = exp(-pow((s - front) / 0.03, 2)) * (drop > 0.001 && drop < 0.999);
                c += lerp(col, 1, 0.5) * frontGlow * 2.0;
                a = a * vis + frontGlow * 0.5;
                return half4(c * _Glow, a);
            }
            ENDHLSL
        }
    }
}
