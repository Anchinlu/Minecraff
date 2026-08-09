Shader "Custom/VoxelWater"
{
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.2, 0.6, 0.8, 1)
        _DeepColor("Deep Color", Color) = (0.05, 0.2, 0.4, 1)
        _FoamColor("Foam Color", Color) = (0.9, 0.95, 1.0, 1)
        _BaseAlpha("Base Alpha", Range(0.0, 1.0)) = 0.6
        _DepthFadeDistance("Depth Fade Distance", Range(0.1, 50.0)) = 4.0
        _RefractionStrength("Refraction Strength", Range(0.0, 0.1)) = 0.01
        _ReflectionStrength("Reflection Strength", Range(0.0, 1.0)) = 0.5
        _FresnelPower("Fresnel Power", Range(0.1, 10.0)) = 5.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.9
        _NormalStrength("Normal Strength", Range(0.0, 2.0)) = 0.5
        _WaveScale("Wave Scale", Range(0.1, 10.0)) = 2.0
        _WaveSpeed("Wave Speed", Range(0.0, 5.0)) = 0.5
        _FoamDistance("Foam Distance", Range(0.0, 5.0)) = 0.5
        _FogStrength("Fog Strength", Range(0.0, 1.0)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _FoamColor;
                half  _BaseAlpha;
                float _DepthFadeDistance;
                float _RefractionStrength;
                half  _ReflectionStrength;
                half  _FresnelPower;
                half  _Smoothness;
                half  _NormalStrength;
                float _WaveScale;
                float _WaveSpeed;
                float _FoamDistance;
                half  _FogStrength;
            CBUFFER_END

            // DayNightCycle vars (Global)
            float _SunVisibility;
            float _SunVisibility2;
            float _SunFactor;
            float _NoonFactor;
            float _NightFactor;
            float _ShadowTime;
            float _RainFactor;
            
            float _FogHeight;
            float _FogHeightFalloff;
            half4 _AtmosphereColor;
            float _AtmosphereStrength;

            #define PI 3.14159265358979323846

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 color        : COLOR;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0; // uv.y == 1 for top surface
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float4 color        : COLOR;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : NORMAL;
                float2 uv           : TEXCOORD1;
                half   fogFactor    : TEXCOORD2;
            };

            // Noise for water normal
            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy) - 0.5;
            }
            
            float2 smoothNoise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float2 a = hash22(i + float2(0.0, 0.0));
                float2 b = hash22(i + float2(1.0, 0.0));
                float2 c = hash22(i + float2(0.0, 1.0));
                float2 d = hash22(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float3 GetWaterNormal(float3 worldPos, bool isTopSurface)
            {
                if (!isTopSurface) {
                    return float3(0, 1, 0);
                }
                
                float2 waterPos = worldPos.xz * _WaveScale * 0.5;
                float timeVar = _Time.y * _WaveSpeed;
                
                // 3 layers of noise
                float2 normalMed = smoothNoise2D(waterPos * 3.0 + float2(timeVar, timeVar));
                float2 normalSmall = smoothNoise2D(waterPos * 12.0 - float2(timeVar, timeVar)*2.0);
                float2 normalBig = smoothNoise2D(waterPos * 0.75 - float2(timeVar*0.5, 0.0));
                
                float2 bump = (normalMed + normalSmall * 0.5 + normalBig * 2.0) * 0.333;
                float3 waterNormal = float3(bump.x * _NormalStrength, 1.0, bump.y * _NormalStrength);
                
                waterNormal.y = max(waterNormal.y, 0.35);
                return normalize(waterNormal);
            }

            float GGX_Water(float3 normalM, float3 viewDir, float3 lightDir, float NdotL, float smoothness)
            {
                smoothness = sqrt(smoothness * 0.9 + 0.1);
                float roughnessP = max(1.35 - smoothness, 0.01);
                float roughness = max(roughnessP * roughnessP * roughnessP * roughnessP, 0.001); // pow4
                
                float3 halfVec = normalize(lightDir + viewDir);
                
                float dotNH = saturate(dot(normalM, halfVec));
                float dotLH = saturate(dot(halfVec, lightDir));
                
                float denom = dotNH * roughness - dotNH + 1.0;
                float D = roughness / (PI * denom * denom);
                
                float f0 = 0.02;
                float F = exp2((-5.55473 * dotLH - 6.98316) * dotLH) * (1.0 - f0) + f0;
                
                float safeLH = max(dotLH * dotLH, 0.0001);
                float specular = max(0.0, max(NdotL, 0.0) * D * F / safeLH);
                specular = specular / (0.125 * specular + 1.0); // Tone mapping
                
                return specular;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.positionWS = worldPos;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                bool isTopSurface = (IN.uv.y > 0.5);
                
                // --- Depth ---
                float2 screenUV = IN.positionHCS.xy / _ScaledScreenParams.xy;
                float rawSceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float waterEyeDepth = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);
                float depthDifference = max(0.0, sceneEyeDepth - waterEyeDepth);
                float depth01 = saturate(depthDifference / max(_DepthFadeDistance, 0.001));
                
                if (rawSceneDepth >= 0.9999) depth01 = 1.0; // Fallback if no depth (e.g. skybox)
                
                // --- Base Color ---
                half3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
                
                // --- Vectors & Normal ---
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                
                float3 baseNormal = isTopSurface ? float3(0, 1, 0) : normalize(IN.normalWS);
                float3 waterNormal = GetWaterNormal(IN.positionWS, isTopSurface);
                
                float viewDotNormal = dot(baseNormal, viewDir);
                if (viewDotNormal < 0) {
                    waterNormal = -baseNormal; // Fix backface viewing
                } else if (!isTopSurface) {
                    waterNormal = baseNormal; // Side faces
                }
                
                // --- Fresnel ---
                float NdotV = saturate(dot(waterNormal, viewDir));
                float fresnel = pow(max(1.0 - NdotV, 0.001), max(_FresnelPower, 0.01));
                float reflectionAmount = saturate(fresnel * _ReflectionStrength);
                
                // --- Lighting & Specular ---
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float NdotL = dot(waterNormal, mainLight.direction);
                
                float3 reflectVector = reflect(-viewDir, waterNormal);
                half3 skyColor = SampleSH(reflectVector);
                
                float specular = GGX_Water(waterNormal, viewDir, mainLight.direction, NdotL, _Smoothness);
                // Safe default for _SunVisibility2 in case DayNight isn't providing it
                float sunVis = max(_SunVisibility2, 0.1); 
                half3 highlightColor = mainLight.color * sunVis * (1.0 - _RainFactor * 0.85);
                half3 sunSpec = specular * highlightColor * mainLight.shadowAttenuation;
                
                // --- Mixing ---
                half3 finalColor = lerp(waterColor, skyColor, reflectionAmount);
                finalColor += sunSpec * reflectionAmount;
                
                // --- Foam ---
                float foam = 0.0;
                if (isTopSurface && rawSceneDepth < 0.9999) {
                    float shallowMask = 1.0 - smoothstep(0.0, max(_FoamDistance, 0.001), depthDifference);
                    float foamNoise = smoothNoise2D(IN.positionWS.xz * 0.08 + _Time.y * 0.02).x;
                    foam = shallowMask * smoothstep(0.45, 0.75, foamNoise);
                    finalColor = lerp(finalColor, _FoamColor.rgb, foam);
                }
                
                // --- Cinematic Fog ---
                float distanceFog = 1.0 - saturate(IN.fogFactor);
                float heightFog = exp(-max(IN.positionWS.y - _FogHeight, 0.0) * _FogHeightFalloff);
                float cinematicFogAmount = saturate(distanceFog * lerp(1.0, heightFog, 1.0)) * _AtmosphereStrength;
                
                finalColor = lerp(finalColor, _AtmosphereColor.rgb, cinematicFogAmount * _FogStrength);
                
                // --- Alpha ---
                half alpha = lerp(_BaseAlpha, 1.0, fresnel * 0.5);
                alpha = lerp(alpha, 1.0, depth01 * 0.5); // Deeper = more opaque
                alpha = max(alpha, foam); // Foam is opaque
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
