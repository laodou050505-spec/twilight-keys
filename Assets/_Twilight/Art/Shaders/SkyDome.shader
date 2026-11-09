// 房间天穹：随歌曲进度，窗外的天空从窗户方向向外铺开，逐渐盖满房间上方（MR 透视之上）。
// _Reveal 0..1：铺开的角度（0 = 不显示，1 = 整个天空）；边缘是带笔触噪声的软边。
// 地平线以下 _FloorCut（默认 -10°）不画，不盖住桌子和琴；模板 ≠ 1，避开窗洞（窗里是村庄自己的天空）。
// alpha 写入透明度通道（MR 合成用），没铺到的地方 alpha = 0 → 真实房间。
Shader "Twilight/SkyDome"
{
    Properties
    {
        _Reveal ("铺开进度", Range(0, 1)) = 0
        _RevealDir ("铺开中心（世界方向，一般是窗户）", Vector) = (0, 0.1, 1, 0)
        _Soft ("边缘软度（弧度）", Float) = 0.35
        _FloorCut ("下沿（sin 仰角）", Float) = -0.17
        _Exposure ("亮度", Float) = 1
        _Opacity ("不透明度", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+460" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Stencil { Ref 1 Comp NotEqual }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TKSky.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Reveal, _Soft, _FloorCut, _Exposure, _Opacity; float4 _RevealDir;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz);
                v.pos = TransformWorldToHClip(v.ws);
                return v;
            }
            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + 1), f.x), f.y), f.z);
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.ws - _WorldSpaceCameraPos);
                float ang = acos(clamp(dot(d, normalize(_RevealDir.xyz)), -1, 1));        // 离窗户方向的角度
                float edge = _Reveal * (PI + _Soft) - _Soft * 0.5;
                float n = Noise(d * 6.0) * 0.6 + Noise(d * 15.0) * 0.4;                 // 笔触感的不规则边
                float a = smoothstep(edge + _Soft * 0.5, edge - _Soft * 0.5, ang + (n - 0.5) * _Soft);
                a *= smoothstep(_FloorCut - 0.04, _FloorCut + 0.04, d.y);
                a *= _Opacity;
                clip(a - 0.002);
                return half4(TKSky(d, 1.0) * _Exposure, a);
            }
            ENDHLSL
        }
    }
}
