Shader "Custom/GradientSkybox"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.3, 0.5, 0.9, 1)
        _HorizonColor ("Horizon Color", Color) = (0.7, 0.85, 1, 1)
        _Exponent ("Blend Exponent", Range(0.1, 5)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _TopColor;
            half4 _HorizonColor;
            half _Exponent;

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 dir:TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz; // hướng từ tâm skybox
                return OUT;
            }
            half4 frag(Varyings IN):SV_Target
            {
                float t = saturate(normalize(IN.dir).y); // 0 = chân trời, 1 = đỉnh trời
                t = pow(t, _Exponent);
                return lerp(_HorizonColor, _TopColor, t);
            }
            ENDHLSL
        }
    }
}
