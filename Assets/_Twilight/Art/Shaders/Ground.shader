// 村庄地面：草 / 土路 / 岩石三张贴图按顶点色混合（R = 土路，G = 岩石），B = 环境遮蔽（1 = 不遮）。
// 世界坐标平铺；主光漫反射 + 球谐环境光 + 雾。远景不投射 / 不接收实时阴影（省移动端开销）。
Shader "Twilight/Ground"
{
    Properties
    {
        _Grass ("草", 2D) = "white" {}
        _Dirt ("土路", 2D) = "white" {}
        _Rock ("岩石", 2D) = "white" {}
        _Tile ("远处平铺（米 / 张）", Float) = 7
        _TileNear ("近处平铺（米 / 张）", Float) = 1.8
        _NearDist ("近处范围（米）", Float) = 14
        _Tint ("整体颜色", Color) = (1, 1, 1, 1)
        _Wrap ("背光面提亮", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Grass); SAMPLER(sampler_Grass);
            TEXTURE2D(_Dirt); TEXTURE2D(_Rock);
            CBUFFER_START(UnityPerMaterial)
                float _Tile, _TileNear, _NearDist, _Wrap; half4 _Tint;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; float3 n : NORMAL; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; half4 col : COLOR; float fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz);
                v.pos = TransformWorldToHClip(v.ws);
                v.n = TransformObjectToWorldNormal(a.n);
                v.col = a.col;
                v.fog = ComputeFogFactor(v.pos.z);
                return v;
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 N0 = normalize(i.n);
                // 近处细密、远处大尺度，按距离过渡（近看不糊，远看不重复）
                float dist = distance(_WorldSpaceCameraPos, i.ws);
                float k = smoothstep(_NearDist * 0.6, _NearDist, dist);
                float2 uvN = i.ws.xz / _TileNear, uvF = i.ws.xz / _Tile;
                half3 g = lerp(SAMPLE_TEXTURE2D(_Grass, sampler_Grass, uvN).rgb, SAMPLE_TEXTURE2D(_Grass, sampler_Grass, uvF).rgb, k);
                // 大尺度明暗变化：同一张草图换个尺度再乘一次，打散重复感
                g *= lerp(0.82, 1.12, SAMPLE_TEXTURE2D(_Grass, sampler_Grass, i.ws.xz / (_Tile * 7.7) + 0.37).g);
                half3 d = lerp(SAMPLE_TEXTURE2D(_Dirt, sampler_Grass, uvN * 0.8).rgb, SAMPLE_TEXTURE2D(_Dirt, sampler_Grass, uvF * 1.3).rgb, k);
                // 岩石：三向投影（陡坡不拉伸）
                float3 w = pow(abs(N0), 4); w /= (w.x + w.y + w.z);
                float rt = lerp(_TileNear * 1.6, _Tile * 1.4, k);
                half3 r = SAMPLE_TEXTURE2D(_Rock, sampler_Grass, i.ws.zy / rt).rgb * w.x + SAMPLE_TEXTURE2D(_Rock, sampler_Grass, i.ws.xz / rt).rgb * w.y
                        + SAMPLE_TEXTURE2D(_Rock, sampler_Grass, i.ws.xy / rt).rgb * w.z;
                half3 alb = lerp(lerp(g, d, i.col.r), r, i.col.g) * _Tint.rgb;
                float3 N = normalize(i.n);
                Light L = GetMainLight();
                half nl = saturate((dot(N, L.direction) + _Wrap) / (1 + _Wrap));
                half3 c = alb * (L.color * nl + SampleSH(N)) * i.col.b;
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
