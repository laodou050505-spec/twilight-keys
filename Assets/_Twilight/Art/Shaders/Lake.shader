// 湖面：细碎波纹法线（离远渐平，防闪烁）+ 反射天空全景 + 反射探针里的村庄（探针 alpha = 有物体）+ 主光高光。
Shader "Twilight/Lake"
{
    Properties
    {
        _Deep ("深水色", Color) = (0.07, 0.13, 0.18, 1)
        _Shallow ("浅水色", Color) = (0.16, 0.28, 0.30, 1)
        _Ripple ("波纹强度", Range(0, 0.3)) = 0.08
        _RippleScale ("波纹尺度（米）", Float) = 3
        _RippleFar ("波纹消失距离（米）", Float) = 160
        _Speed ("流速", Float) = 0.25
        _Squash ("天空展开（同窗中天空）", Float) = 1.6
        _ReflCube ("村庄倒影·黄昏（带 alpha）", Cube) = "" {}
        _ReflCubeNight ("村庄倒影·夜", Cube) = "" {}
        _ReflStrength ("村庄反射强度", Range(0, 1)) = 0
        _Spec ("高光", Range(0, 4)) = 1.2
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
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
            #include "TKSky.hlsl"
            TEXTURECUBE(_ReflCube); SAMPLER(sampler_ReflCube);
            TEXTURECUBE(_ReflCubeNight);
            float _TK_Night;      // 全局：0 黄昏 → 1 夜（TwilightSky 设置）
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Shallow; float _Ripple, _RippleScale, _RippleFar, _Speed, _Squash, _ReflStrength, _Spec;
            CBUFFER_END
            struct Attr { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vary { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; float fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Vary vert(Attr a)
            {
                Vary v; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(v);
                v.ws = TransformObjectToWorld(a.pos.xyz);
                v.pos = TransformWorldToHClip(v.ws);
                v.fog = ComputeFogFactor(v.pos.z);
                return v;
            }
            // 几组方向不同的正弦波叠加出的斜率
            float2 Slope(float2 p, float t)
            {
                float2 s = 0;
                const float3 w[5] = { float3(1.0, 0.31, 1.0), float3(-0.43, 1.0, 1.37), float3(0.77, -0.64, 1.91), float3(-0.9, -0.2, 2.63), float3(0.2, 0.95, 3.7) };
                [unroll] for (int k = 0; k < 5; k++)
                {
                    float2 dir = normalize(w[k].xy); float f = w[k].z / _RippleScale;
                    float ph = dot(p, dir) * f * 6.2831 + t * (1.3 + k * 0.37);
                    s += dir * cos(ph) / w[k].z;
                }
                return s;
            }
            half4 frag(Vary i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 V = normalize(_WorldSpaceCameraPos - i.ws);
                float dist = distance(_WorldSpaceCameraPos, i.ws);
                float fade = saturate(1 - dist / _RippleFar);
                float2 s = Slope(i.ws.xz, _Time.y * _Speed * 4) * _Ripple * fade * fade;
                float3 N = normalize(float3(-s.x, 1, -s.y));
                float3 R = reflect(-V, N);
                R.y = abs(R.y);                                  // 波纹把反射打到水下时翻回天空
                half3 sky = TKSky(R, _Squash);
                // 倒影稍微模糊一点（水面不是完美镜子），波纹扰动方向
                // 倒影立方体贴图按编辑时的朝向烘焙：世界跟着窗户转了 yaw，采样前把方向转回去（_TK_SkyRows.w = -yaw / 360）
                float ya = _TK_SkyRows.w * 6.2831853;
                float3 Rc = float3(R.x * cos(ya) + R.z * sin(ya), R.y, -R.x * sin(ya) + R.z * cos(ya));
                half4 cube = lerp(SAMPLE_TEXTURECUBE_LOD(_ReflCube, sampler_ReflCube, Rc, 1), SAMPLE_TEXTURECUBE_LOD(_ReflCubeNight, sampler_ReflCube, Rc, 1), _TK_Night);
                half3 refl = lerp(sky, cube.rgb, cube.a * _ReflStrength);
                float ndv = saturate(dot(N, V));
                float F = 0.02 + 0.98 * pow(1 - ndv, 5);
                half3 body = lerp(_Shallow.rgb, _Deep.rgb, saturate(dist / 60)) * (TKHorizon() * 0.6 + 0.4);
                half3 c = lerp(body, refl, F);
                Light L = GetMainLight();
                float3 H = normalize(L.direction + V);
                c += L.color * pow(saturate(dot(N, H)), 220) * _Spec;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
