Shader "Custom/VoxelTextured"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

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

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float3 positionWS   : TEXCOORD1;
                float3 normalWS     : NORMAL;
                half   fogFactor    : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // === GLOBAL SHADER VARIABLES (Từ DayNightCycle.cs, mô phỏng COBBLEVERSE) ===
            float _NoonFactor;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                
                // Base Color from Texture * Vertex Color (AO + Baked Sunlight)
                half3 albedo = texColor.rgb * IN.color.rgb;
                
                // Real-time Lighting Calculation
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half NdotL = saturate(dot(normalize(IN.normalWS), normalize(mainLight.direction)));
                
                // === DIRECTIONAL SHADING (Từ COBBLEVERSE mainLighting.glsl L632-668) ===
                half NdotN = dot(IN.normalWS, half3(0, 0, 1)); // Trục Bắc Nam (Z)
                half absNdotN = abs(NdotN);
                half NdotE = dot(IN.normalWS, half3(1, 0, 0)); // Trục Đông Tây (X)
                half absNdotE = abs(NdotE);
                half absNdotE2 = absNdotE * absNdotE;
                half NdotU = dot(IN.normalWS, half3(0, 1, 0)); // Trục Trên Dưới (Y)
                half NdotUmax0 = max(NdotU, 0.0);
                
                half NdotUM = 0.75 + NdotU * 0.25;
                half NdotNM = 1.0 + 0.075 * absNdotN;
                half NdotEM = 1.0 - 0.1 * absNdotE2;
                half directionShade = NdotUM * NdotEM * NdotNM;
                
                // === NÂNG CẤP ÁNH SÁNG (Từ COBBLEVERSE mainLighting.glsl) ===
                
                // Mặt Đông-Tây sáng hơn (COBBLEVERSE L648)
                half3 lightColorM = lightColor * (1.0 + absNdotE2 * 0.75);
                
                // 1. Ambient ánh sáng môi trường động
                half3 ambient = SampleSH(IN.normalWS) * 0.8;
                
                // 2. Fake Bounced Light (COBBLEVERSE L663)
                half3 bouncedLight = lightColorM * 0.05 * absNdotN;
                ambient += bouncedLight;

                // 3. Wrap Lighting mềm (COBBLEVERSE Side Shadowing L236)
                half wrapNdotL = saturate((NdotL + 0.4) * 0.714);
                
                // 4. Fake Rim Light
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                half rim = 1.0 - saturate(dot(viewDir, normalize(IN.normalWS)));
                rim = pow(rim, 3.0);
                half3 rimColor = lightColor * rim * 0.15;
                
                // 5. Fake Sky Light
                half skyFactor = saturate(NdotU) * 0.5;
                half3 skyLight = SampleSH(half3(0,1,0)) * skyFactor;
                
                // 6. Noon Contrast Boost (COBBLEVERSE L666)
                half noonPow = _NoonFactor * _NoonFactor * _NoonFactor * _NoonFactor * _NoonFactor;
                noonPow *= noonPow;
                lightColorM *= 1.0 + noonPow * (absNdotN * absNdotN * 0.8 - absNdotE2 * 0.2);
                
                // Tổng hợp ánh sáng và áp dụng Directional Shading
                half3 finalLight = (ambient + skyLight + (lightColorM * wrapNdotL) + rimColor) * directionShade;
                
                // === VANILLA AO NÂNG CAO (Từ COBBLEVERSE mainLighting.glsl L744-780) ===
                half vanillaAO = IN.color.a;
                vanillaAO = min(vanillaAO + 0.08, 1.0);
                half dotSceneLighting = dot(finalLight, finalLight);
                half aoExponent = 1.0 + dotSceneLighting * 0.02 + NdotUmax0 * (0.15 + 0.25 * _NoonFactor * _NoonFactor);
                vanillaAO = pow(pow(max(vanillaAO, 0.001), 1.5), aoExponent);
                vanillaAO = vanillaAO * 0.9 + 0.1;
                
                // Màu cuối cùng = Albedo * Real-time Light * AO
                half3 finalColor = albedo * finalLight * vanillaAO;
                
                // Trộn sương mù (Fog)
                finalColor = MixFog(finalColor, IN.fogFactor);
                
                return half4(finalColor, texColor.a * IN.color.a);
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
