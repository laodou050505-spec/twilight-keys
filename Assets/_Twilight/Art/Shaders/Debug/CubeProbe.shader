// 调试：按 _Dir 方向采样立方体贴图输出（Blit 到 1×1 读回），用来核对烘焙的立方体贴图朝向。
Shader "Hidden/Twilight/CubeProbe"
{
    Properties { _Cube ("Cube", Cube) = "" {} _Dir ("Dir", Vector) = (0, 0, 1, 0) }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            samplerCUBE _Cube; float4 _Dir;
            float4 vert(float4 p : POSITION) : SV_POSITION { return UnityObjectToClipPos(p); }
            float4 frag() : SV_Target { return texCUBElod(_Cube, float4(normalize(_Dir.xyz), 0)); }
            ENDHLSL
        }
    }
}
