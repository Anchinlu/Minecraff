Shader "Custom/SunShader"
{
    Properties
    {
        _Color ("Sun Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        // Vẽ ở Queue 2900 (Sau Skybox, nhưng trước Mây ở 3000)
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" }
        Cull Off 
        ZWrite Off 
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _Color;

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionHCS:SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN):SV_Target
            {
                return _Color; // KHÔNG có multi_compile_fog -> Sương mù không thể che khuất Mặt trời
            }
            ENDHLSL
        }
    }
}
