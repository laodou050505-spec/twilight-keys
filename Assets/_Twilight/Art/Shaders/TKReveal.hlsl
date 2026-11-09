// 世界铺开：从窗户方向向外，按进度 _TK_Reveal（0..1）铺开的球冠，边界用两层噪声打碎成笔触状。
// 遮罩（写模板，村庄画在里面）和光边（边界上一圈暖金光）共用这里的边界计算。
#ifndef TK_REVEAL_INCLUDED
#define TK_REVEAL_INCLUDED
float _TK_Reveal;          // 0 = 只有窗洞，1 = 整个世界
float4 _TK_RevealDir;      // 铺开中心（世界方向，眼睛 → 窗户）
#define TK_REVEAL_SOFT 0.42

float TKRHash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
float TKRNoise(float3 p)
{
    float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(TKRHash(i), TKRHash(i + float3(1, 0, 0)), f.x), lerp(TKRHash(i + float3(0, 1, 0)), TKRHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(TKRHash(i + float3(0, 0, 1)), TKRHash(i + float3(1, 0, 1)), f.x), lerp(TKRHash(i + float3(0, 1, 1)), TKRHash(i + 1), f.x), f.y), f.z);
}

// 返回“到边界的角距离”（弧度，<0 = 已铺开）。d：世界方向（单位向量）
float TKRevealDist(float3 d)
{
    float ang = acos(clamp(dot(d, normalize(_TK_RevealDir.xyz)), -1, 1));
    float edge = _TK_Reveal * (PI + TK_REVEAL_SOFT * 2) - TK_REVEAL_SOFT;
    float t = _Time.y * 0.03;
    float n = TKRNoise(d * 5.0 + t) * 0.65 + TKRNoise(d * 17.0 - t) * 0.35;      // 大块笔触 + 细碎边
    return ang + (n - 0.5) * TK_REVEAL_SOFT - edge;
}
#endif
