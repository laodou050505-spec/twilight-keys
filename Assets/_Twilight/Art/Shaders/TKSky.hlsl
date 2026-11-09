// 天空全景公共函数：天空图 / 当前过渡参数都是全局变量（TwilightSky 组件每帧设置），天空盒、湖面反射共用。
#ifndef TK_SKY_INCLUDED
#define TK_SKY_INCLUDED
TEXTURE2D_ARRAY(_TK_Sky); SAMPLER(sampler_TK_Sky);
float4 _TK_SkyRows;        // x = A 行, y = B 行, z = 过渡, w = 水平旋转（圈）
float4 _TK_ZenA, _TK_ZenB, _TK_HorA, _TK_HorB;
float4 _TK_Moon;           // xyz = 月亮方向（世界），w = 可见度（入夜后 1）

// 月亮：按真实三维方向画（不受全景映射拉伸影响，任何仰角都是正圆）。手绘风的满月偏一点（凸月）：
// 暖奶油色的盘面、边缘柔和；表面几块很淡的月海（低频、柔边，不是条纹）；亮面一侧边缘略亮（像画师提亮）；
// 暗面一小块柔和渐入（不是硬月牙）；外面两圈暖金色光晕，再一层很大很淡的辉光把周围的天空照亮一点。
float TKMoonNoise(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    float a = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.5453), b = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.5453);
    float c = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.5453), d = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.5453);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

half3 TKMoon(float3 d)
{
    if (_TK_Moon.w <= 0.001) return 0;
    float3 m = normalize(_TK_Moon.xyz);
    float c = dot(d, m);
    if (c < 0.9) return 0;
    float3 t1 = normalize(cross(float3(0, 1, 0), m)), t2 = cross(m, t1);
    const float R = 0.03;                                   // 角半径约 1.7°
    float2 p = float2(dot(d, t1), dot(d, t2)) / R;
    float r = length(p);
    float aa = fwidth(r) * 1.5 + 0.004;
    float disc = 1 - smoothstep(1 - aa, 1 + aa, r);
    // 凸月：一个偏移很大的软圆挡住右下一小条，过渡带宽（柔和的明暗交界）
    float2 sh = p - float2(1.55, -0.55);
    float lit = smoothstep(0.98, 1.38, length(sh));
    // 月海：两层低频噪声，柔边，只让亮度轻轻起伏
    float n = TKMoonNoise(p * 1.6 + 3.1) * 0.65 + TKMoonNoise(p * 3.4 + 7.7) * 0.35;
    float maria = 1 - 0.38 * smoothstep(0.38, 0.68, n);         // 月海更明显
    // 球面感：中间亮、边缘暗；亮面那侧边缘提亮一点
    float sphere = lerp(0.7, 1.0, sqrt(saturate(1 - r * r)));
    float rimLight = smoothstep(0.75, 1.0, r) * saturate(dot(normalize(p + 1e-4), float2(-0.7, 0.7))) * 0.08;
    half3 face = half3(0.92, 0.88, 0.72) * (sphere * maria + rimLight);    // 月面偏暖米色，不是白
    half3 dark = half3(0.18, 0.2, 0.32) * 0.2;                             // 暗面深一些
    half3 col = lerp(dark, face * 0.72, lit) * disc;                        // 整体压暗：看得见月海纹路和明暗交界
    // 光晕：贴边一圈暖光 + 一层大而淡的辉光
    float out_ = max(r - 1, 0);
    float halo = exp(-out_ * 7.0) * 0.14 + exp(-out_ * 1.5) * 0.05 + exp(-out_ * 0.35) * 0.025;
    col += half3(1.0, 0.86, 0.62) * halo * (1 - disc);
    return col * _TK_Moon.w;
}

half3 TKPanoRow(float row, float u, float v, float2 gx, float2 gy)
{
    float x = u * 4.0;
    float col = min(floor(x), 3.0);
    return SAMPLE_TEXTURE2D_ARRAY_GRAD(_TK_Sky, sampler_TK_Sky, float2(x - col, v), row * 4.0 + col, gx * float2(4, 1), gy * float2(4, 1)).rgb;
}

// d：世界空间方向（单位向量）；squash > 1 让地平线附近显示更多天空图（窗里视角窄时用）
half3 TKSky(float3 d, float squash)
{
    float az = atan2(d.x, d.z) / (2.0 * PI) + 0.5 + _TK_SkyRows.w;
    float u = frac(az), u2 = frac(az + 0.5);
    float el = asin(clamp(d.y, -1, 1)) / (0.5 * PI);
    float v = min(pow(saturate(el), 1.0 / squash), 0.995);
    float2 du = float2(ddx(u), ddy(u)), du2 = float2(ddx(u2), ddy(u2));
    if (dot(du2, du2) < dot(du, du)) du = du2;
    float2 dv = float2(ddx(v), ddy(v));
    float2 gx = float2(du.x, dv.x), gy = float2(du.y, dv.y);
    float m = _TK_SkyRows.z;
    half3 c = lerp(TKPanoRow(_TK_SkyRows.x, u, v, gx, gy), TKPanoRow(_TK_SkyRows.y, u, v, gx, gy), m);
    c = lerp(c, lerp(_TK_ZenA.rgb, _TK_ZenB.rgb, m), smoothstep(0.86, 0.99, v));
    c = lerp(c, lerp(_TK_HorA.rgb, _TK_HorB.rgb, m) * 0.8, smoothstep(0.0, -0.08, el));
    c += TKMoon(d) * smoothstep(-0.02, 0.03, d.y);
    return c;
}

half3 TKHorizon() { return lerp(_TK_HorA.rgb, _TK_HorB.rgb, _TK_SkyRows.z); }
#endif
