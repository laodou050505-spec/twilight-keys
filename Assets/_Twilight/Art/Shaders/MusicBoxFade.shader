// 八音盒材质的“消散版”（第三十轮，用户：前面窗框消散后，前面的歌名也要散掉）。
// 光照和 Twilight/MusicBoxLit 一模一样（换材质那一下不跳色），只多一样：_Fade 0 → 1 时从下往上整片柔和变透明，
// 公式和窗框的 Twilight/LitDissolve 相同（过渡带 45%、带一点云状起伏），看起来是同一阵雾把它散掉。
// 八音盒专用材质：用 Blender Cycles 渲出来的材质球（matcap）给出“棚拍”的质感，不靠场景灯光（MR 里没有好的环境光）。
// 金 / 钢：直接按法线查 matcap 当颜色（金属的高光、暗部、轮廓光都在球上画好了）。
// 木 / 深木 / 面板：手绘木纹 × 柔和的半兰伯特暖光 + 清漆高光 matcap（叠加）+ 菲涅尔边光；底下随高度微微压暗（像落在桌上的环境遮蔽）。
// _Mode：0 木（带清漆），1 金属（matcap 即颜色），2 哑光（丝绒、深木：无清漆）。
Shader "Twilight/MusicBoxFade"
{
    Properties
    {
        _MainTex ("木纹 / 底色贴图", 2D) = "white" {}
        _Color ("颜色", Color) = (1, 1, 1, 1)
        _Matcap ("材质球", 2D) = "black" {}
        _Mode ("0 木 / 1 金属 / 2 哑光 / 3 金浮雕 / 4 油画", Float) = 0
        _Relief ("浮雕（RGB 法线 A 高度）", 2D) = "bump" {}
        _ReliefDepth ("浮雕深度", Float) = 1
        _Gloss ("清漆强度", Float) = 0.55
        _Rim ("边光", Float) = 0.35
        _RimColor ("边光颜色", Color) = (1, 0.82, 0.6, 1)
        _AOHeight ("压暗高度（模型本地 Z，米）", Float) = 0.03
        _Lift ("暗部抬亮", Float) = 0.35
        _NoAO ("关掉底部压暗（平板 UI 用）", Float) = 0
        _Fade ("消散 0 完整 … 1 消失", Range(0, 1)) = 0
        _MinY ("物体底（世界 y）", Float) = 0
        _MaxY ("物体顶（世界 y）", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Matcap); SAMPLER(sampler_Matcap);
            TEXTURE2D(_Relief); SAMPLER(sampler_Relief);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Relief_ST; half4 _Color, _RimColor; half _Mode, _Gloss, _Rim, _AOHeight, _Lift, _ReliefDepth, _NoAO, _Fade; float _MinY, _MaxY;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 nw : TEXCOORD1; float3 ws : TEXCOORD2; float h : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };
            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }
            V vert(A a)
            {
                V v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz);
                v.pos = TransformWorldToHClip(v.ws);
                v.nw = TransformObjectToWorldNormal(a.n);
                v.uv = TRANSFORM_TEX(a.uv, _MainTex);
                v.h = a.pos.z;                                    // FBX 导进来物体本地 Z = Blender 的竖直
                return v;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 N = normalize(i.nw);
                float3 Vd = normalize(_WorldSpaceCameraPos - i.ws);
                half relH = 1;
                if (_Mode > 2.5 && _Mode < 3.5)
                {
                    // 金浮雕：RGB = 切线空间法线、A = 高度。没有导切线，用屏幕导数现搭 TBN（余切框架）
                    half4 rl = SAMPLE_TEXTURE2D(_Relief, sampler_Relief, i.uv * _Relief_ST.xy + _Relief_ST.zw);
                    float3 dp1 = ddx(i.ws), dp2 = ddy(i.ws);
                    float2 du1 = ddx(i.uv), du2 = ddy(i.uv);
                    float3 dp2p = cross(dp2, N), dp1p = cross(N, dp1);
                    float3 T = dp2p * du1.x + dp1p * du2.x, B = dp2p * du1.y + dp1p * du2.y;
                    float inv = rsqrt(max(1e-12, max(dot(T, T), dot(B, B))));
                    float3 tn = rl.rgb * 2 - 1; tn.xy *= _ReliefDepth;
                    N = normalize(T * inv * tn.x + B * inv * tn.y + N * tn.z);
                    relH = rl.a;
                }
                // matcap 的“观察方向”：用盒子中心 → 眼睛的方向 + 世界竖直搭基（不用相机朝向）。
                // 原来用视图矩阵：一转头，镜面细高光就整条扫过盒面 = 闪白；现在只有人走动才变，转头 / 歪头都不变
                float3 objC = TransformObjectToWorld(float3(0, 0, 0));
                float3 bf = normalize(_WorldSpaceCameraPos - objC);
                float3 br = normalize(cross(float3(0, 1, 0), bf)), bu = cross(bf, br);
                float3 nv = float3(dot(N, br), dot(N, bu), dot(N, bf));
                float2 muv = nv.xy * 0.485 + 0.5;
                // 高光防闪：法线在一个像素里变化越大（窄倒角、细金边），材质球就取越模糊的 mip
                float2 dm = fwidth(muv) * 512;
                float lod = clamp(log2(max(dm.x, dm.y)) + 1.5, 0, 6);
                half3 mc = SAMPLE_TEXTURE2D_LOD(_Matcap, sampler_Matcap, muv, lod).rgb;
                half3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Color.rgb;
                float fres = pow(1 - saturate(dot(N, Vd)), 3);
                // 柔和的暖色主光（左上前方）+ 冷一点的天光：半兰伯特，暗部抬亮，不会死黑
                float3 L = normalize(float3(-0.45, 0.75, -0.5));
                float hl = saturate(dot(N, L) * 0.5 + 0.5);
                half3 diffuse = lerp(half3(0.42, 0.33, 0.32) * (1 + _Lift), half3(1.08, 0.96, 0.84), hl * hl);
                diffuse += saturate(N.y) * half3(0.06, 0.06, 0.08);
                float ao = lerp(lerp(0.72, 1.0, saturate(i.h / max(1e-4, _AOHeight))), 1.0, _NoAO);
                half3 c;
                if (_Mode > 0.5 && _Mode < 1.5)
                {
                    c = mc * _Color.rgb * (0.68 + 0.12 * tex);   // 金：压暗，不要那么闪
                }
                else if (_Mode > 2.5 && _Mode < 3.5)
                {
                    // 做旧鎏金：凸起亮、凹底压成深古铜（像参考图里刻花之间的暗缝），不再是一整片亮金
                    half cav = smoothstep(0.22, 0.42, relH);   // 第十三轮：边缘更利，凹底接近黑 = 镂空透出去的感觉
                    c = mc * _Color.rgb * lerp(half3(0.3, 0.22, 0.13), half3(0.9, 0.86, 0.8), cav);   // 第十五轮：对比收小（不要太闪）   // 浮雕：凸起不要太亮
                }
                else if (_Mode > 3.5)
                {
                    // 盖内油画：只吃柔光（油画是哑光的），四周微微压暗像嵌在画框里
                    float2 e = abs(i.uv - 0.5) * 2;
                    half vig = 1 - 0.28 * pow(saturate(max(e.x, e.y)), 4);
                    c = tex * lerp(half3(0.78, 0.72, 0.7), half3(1.02, 0.97, 0.9), hl) * vig;
                }
                else
                {
                    c = tex * diffuse;
                    if (_Mode < 0.5) c += mc * _Gloss;                // 清漆高光
                }
                c += _RimColor.rgb * fres * _Rim * (_Mode > 1.5 && _Mode < 2.5 ? 0.4 : 1);
                float hh = saturate((i.ws.y - _MinY) / max(0.001, _MaxY - _MinY));
                float k = hh * 0.85 + Noise(i.ws * 22.0) * 0.15;
                float alpha = smoothstep(0.0, 0.45, k - (_Fade * 1.5 - 0.45));
                clip(alpha - 0.004);
                c = lerp(c, c * 1.12 + half3(1.0, 0.82, 0.5) * 0.04, (1 - alpha) * step(0.001, _Fade));
                return half4(c * ao, alpha);
            }
            ENDHLSL
        }
    }
}
