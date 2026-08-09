Shader "Custom/CloudShader"
{
    Properties
    {
        _CloudBaseColor ("Cloud Base Color", Color) = (0.75, 0.8, 0.9, 1.0)
        _CloudSunColor ("Cloud Sun Color", Color) = (1.0, 0.95, 0.85, 1.0)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.4, 0.45, 0.55, 1.0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 _CloudBaseColor;
            half4 _CloudSunColor;
            half4 _CloudShadowColor;

            // Global Variables
            float _NoonFactor;
            float _SunVisibility;
            
            float _FogHeight;
            float _FogHeightFalloff;
            half4 _AtmosphereColor;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : NORMAL;
                half   fogFactor    : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                half3 normalWS = normalize(IN.normalWS);
                half NdotL = dot(normalWS, mainLight.direction);
                half NdotU = normalWS.y;
                
                // Mây mặt trên sáng, mặt dưới nhận màu shadow
                half3 baseLight = lerp(_CloudShadowColor.rgb, _CloudBaseColor.rgb, NdotU * 0.5 + 0.5);
                
                // Wrap Lighting mềm từ Mặt trời (chỉ khi có mặt trời)
                half wrapNdotL = saturate((NdotL + 0.4) * 0.714);
                half3 sunLight = _CloudSunColor.rgb * wrapNdotL * mainLight.shadowAttenuation * _NoonFactor * _SunVisibility;
                
                half3 finalColor = baseLight + sunLight;
                
                // --- Cinematic Fog ---
                float distanceFog = 1.0 - IN.fogFactor; // In URP, fogFactor goes from 1 to 0. 0 means fully fogged.
                
                // Mây thường ở trên cao, ít bị ảnh hưởng bởi height fog, nhưng ta thêm vào cho đồng bộ
                float heightFog = exp(-max(IN.positionWS.y - _FogHeight, 0.0) * _FogHeightFalloff);
                float heightInfluence = 0.5; // Ảnh hưởng nhẹ hơn so với terrain
                float cinematicFogAmount = saturate(distanceFog * lerp(1.0, heightFog, heightInfluence));
                
                finalColor = lerp(finalColor, _AtmosphereColor.rgb, cinematicFogAmount);
                
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, GetMainLight().direction));
                
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                
                OUT.positionHCS = positionCS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
