Shader "Custom/VoxelWater"
{
    Properties
    {
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

            // === GLOBAL SHADER VARIABLES (Từ DayNightCycle.cs, mô phỏng COBBLEVERSE) ===
            float _SunVisibility;
            float _SunVisibility2;
            float _SunFactor;
            float _NoonFactor;
            float _NightFactor;
            float _ShadowTime;
            float _RainFactor;

            #define PI 3.14159265358979323846

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float4 color        : COLOR;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
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

            // ====================================================================
            // PROCEDURAL NOISE (Thay thế noisetex/gaux4 của COBBLEVERSE)
            // Hash-based smooth noise cho water normals
            // ====================================================================
            
            // Hash function tạo giá trị giả ngẫu nhiên từ tọa độ 2D
            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy) - 0.5;
            }
            
            // Smooth value noise 2D — mô phỏng texture lookup mịn
            float2 smoothNoise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                // Hermite interpolation (smoother than linear)
                float2 u = f * f * (3.0 - 2.0 * f);
                
                float2 a = hash22(i + float2(0.0, 0.0));
                float2 b = hash22(i + float2(1.0, 0.0));
                float2 c = hash22(i + float2(0.0, 1.0));
                float2 d = hash22(i + float2(1.0, 1.0));
                
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // ====================================================================
            // WATER NORMALS — 3-Octave System (Từ COBBLEVERSE water.glsl L105-123)
            // ====================================================================
            // Tỉ lệ bump từ COBBLEVERSE water settings:
            // WATER_BUMP_BIG = 2.0, WATER_BUMP_MED = 1.7, WATER_BUMP_SMALL = 0.75
            // WATER_BUMPINESS = 1.25, WATER_BUMPINESS_M = 1.25 * 0.8 = 1.0
            // WATER_SPEED_MULT_M = 1.0 * 0.018 = 0.018
            
            float3 GetWaterNormal(float3 worldPos, float3 viewDir)
            {
                // Tọa độ nước: world XZ + ảnh hưởng Y (giống COBBLEVERSE L87)
                float2 waterPos = worldPos.xz * 0.032 + worldPos.y * 0.064;
                
                // Gió — tốc độ từ COBBLEVERSE: rawWind = frameTimeCounter * 0.018
                float rawWind = _Time.y * 0.018;
                float2 wind = float2(0.0, -rawWind);
                
                // Scale * 2.5 (từ COBBLEVERSE L106)
                waterPos *= 2.5;
                wind *= 2.5;
                
                // === 3 TẦNG BUMP (Từ COBBLEVERSE water.glsl L116-121) ===
                // Tầng Medium: tỉ lệ 1:1 với waterPos
                float2 normalMed = smoothNoise2D(waterPos * 3.0 + wind * 3.0);
                
                // Tầng Small: tỉ lệ 4x, gió ngược 2x (chi tiết nhỏ cho lấp lánh)
                float2 normalSmall = smoothNoise2D(waterPos * 12.0 - wind * 6.0);
                
                // Tầng Big: tỉ lệ 0.25x, gió chậm (sóng biển lớn)
                float2 normalBig = smoothNoise2D(waterPos * 0.75 - wind * 1.25);
                normalBig += smoothNoise2D(waterPos * 0.15 - wind * 0.125);
                
                // Trộn 3 tầng theo tỉ lệ COBBLEVERSE (L121)
                // normalMap.xy = normalMed * WATER_BUMP_MED + normalSmall * WATER_BUMP_SMALL + normalBig * WATER_BUMP_BIG
                float2 bump = normalMed * 1.7 + normalSmall * 0.75 + normalBig * 2.0;
                
                // Cường độ bump: 6.0 * (1 - 0.7 * fresnel) * WATER_BUMPINESS_M * waterBumpNoise (L122)
                // Ở đây ta ước lượng fresnel sơ bộ dùng viewDir
                float approxFresnel = saturate(1.0 - abs(viewDir.y));
                bump *= 6.0 * (1.0 - 0.7 * approxFresnel) * 1.0; // WATER_BUMPINESS_M = 1.0
                
                // Scale cuối cùng: 0.03 * lmCoordM.y + 0.01 (L125)
                // Trong Unity ta giả sử skylight = 1.0 (ngoài trời)
                bump *= 0.03 * 1.0 + 0.01;
                
                return normalize(float3(bump.x, 1.0, bump.y));
            }

            // ====================================================================
            // GGX SPECULAR (Từ COBBLEVERSE ggx.glsl — Horizon Zero Dawn approximation)
            // ====================================================================
            float GGX_Water(float3 normalM, float3 viewDir, float3 lightDir, float NdotL, float smoothness)
            {
                smoothness = sqrt(smoothness * 0.9 + 0.1);
                float roughnessP = 1.35 - smoothness;
                float roughness = roughnessP * roughnessP * roughnessP * roughnessP; // pow4
                
                float3 halfVec = normalize(lightDir + viewDir);
                
                float dotNH = saturate(dot(normalM, halfVec));
                float dotLH = saturate(dot(halfVec, lightDir));
                float dotNV = saturate(dot(normalM, viewDir));
                
                // GGX Distribution
                float denom = dotNH * roughness - dotNH + 1.0;
                float D = roughness / (PI * denom * denom);
                
                // Schlick Fresnel approximation 
                float f0 = 0.05;
                float F = exp2((-5.55473 * dotLH - 6.98316) * dotLH) * (1.0 - f0) + f0;
                
                // Combined specular (từ COBBLEVERSE ggx.glsl L55-57)
                float NdotLmax0 = max(NdotL, 0.0);
                float NdotLmax0M = sqrt(sqrt(sqrt(NdotLmax0 * max(dot(float3(0,1,0), lightDir), 0.0))));
                float specular = max(0.0, NdotLmax0M * D * F / (dotLH * dotLH));
                specular = specular / (0.125 * specular + 1.0); // Tone-mapping (tránh blown out)
                
                return specular;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                
                // ĐÃ XÓA: Bỏ hoàn toàn hiệu ứng Vertex Displacement (sóng vẫy) 
                // vì nó gây ra lỗi rách lưới (Tearing) ở ranh giới giữa nước và đất liền.
                
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
                // === 1. TÍNH TOÁN ĐỘ SÂU (Depth) ===
                float2 screenUV = IN.positionHCS.xy / _ScaledScreenParams.xy;
                
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterDepth = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);
                
                float depthDifference = max(0.0, sceneDepth - waterDepth);

                // === 2. MÀU NƯỚC (Từ COBBLEVERSE water.glsl Step 1) ===
                // Màu nước cơ bản — COBBLEVERSE dùng glColorM = (0.43, 0.6, 0.8)
                half3 waterBaseColor = half3(0.43, 0.6, 0.8);
                half3 deepColor = waterBaseColor * waterBaseColor * 0.3; // pow2 * darker
                half3 shallowColor = waterBaseColor * 0.6;
                
                // Water fog: exponential (Từ COBBLEVERSE water.glsl L200)
                // max0(1.0 - exp(lViewPosDifM * 0.075))
                float waterFog = max(0.0, 1.0 - exp(-depthDifference * 0.075));
                waterFog = saturate(waterFog);
                
                // Water alpha (Từ COBBLEVERSE L186-201)
                half waterAlpha = sqrt(saturate(waterFog + 0.3)); // sqrt1(color.a)
                waterAlpha *= 0.25 + 0.75 * waterFog; // L201
                waterAlpha = max(waterAlpha, 0.4); // Đảm bảo nước luôn nhìn thấy được

                // === 3. PHÁP TUYẾN 3-OCTAVE & VECTORS ===
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 normalWS = GetWaterNormal(IN.positionWS, viewDir);
                
                // Fix normals pointing inside water (Từ COBBLEVERSE water.glsl L150-152)
                float3 geoNormal = float3(0, 1, 0);
                float3 reflectCheck = reflect(-viewDir, normalize(normalWS));
                float norMix = pow(saturate(1.0 - max(0, dot(geoNormal, reflectCheck))), 8) * 0.5;
                normalWS = lerp(normalWS, geoNormal, norMix);
                
                // === 4. FRESNEL (Từ COBBLEVERSE water.glsl L65-66, L275-276, L293) ===
                float NdotV = saturate(dot(normalWS, viewDir));
                float fresnel = saturate(1.0 - NdotV); // 0 nhìn thẳng, 1 nhìn ngang
                float fresnel2 = fresnel * fresnel;
                float fresnelM = fresnel * fresnel2;       // pow3 (COBBLEVERSE L276)
                float fresnel4 = fresnel2 * fresnel2;      // pow4 (COBBLEVERSE L66)
                
                // fresnelM điều chỉnh: (fresnelM * 0.85 + 0.15) * reflectMult (COBBLEVERSE L393)
                float reflectMult = 1.0; // Nước luôn phản chiếu
                reflectMult *= 0.5 + 0.5 * max(0, dot(geoNormal, float3(0,1,0))); // L291
                fresnelM = (fresnelM * 0.85 + 0.15) * reflectMult;
                
                // === 5. ÁNH SÁNG MẶT TRỜI (Main Light) ===
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half3 lightColor = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float NdotL = dot(normalWS, mainLight.direction);
                
                // Highlight color (Từ COBBLEVERSE mainLighting.glsl L43)
                // normalize(pow(lightColor, 0.37)) * (0.3 + 1.5 * sunVisibility2) * (1 - 0.85 * rainFactor)
                half3 highlightColor = normalize(pow(max(lightColor, 0.001), 0.37)) 
                                     * (0.3 + 1.5 * _SunVisibility2) 
                                     * (1.0 - 0.85 * _RainFactor);
                
                // === 6. PHẢN XẠ BẦU TRỜI (Sky Reflection) ===
                float3 reflectVector = reflect(-viewDir, normalWS);
                half3 skyColor = SampleSH(reflectVector);
                
                // Hào quang Mặt Trời chiếu lên bầu trời (Sun Glare trên phản xạ)
                // Từ COBBLEVERSE sky.glsl — sun glare scatter
                float sunSkyGlow = saturate(dot(reflectVector, mainLight.direction));
                float glareScatter = 3.0;
                float sunGlare = pow(sunSkyGlow, glareScatter);
                float visfactor = 0.075;
                float glare = visfactor / (1.0 - (1.0 - visfactor) * pow(sunSkyGlow, glareScatter)) - visfactor;
                glare *= 0.7;
                
                half3 glareColor = lerp(half3(0.38, 0.4, 0.5) * 0.3, 
                                        half3(1.5, 0.7, 0.3) + half3(0.0, 0.5, 0.5) * _NoonFactor, 
                                        _SunVisibility);
                skyColor += glare * _ShadowTime * glareColor;
                
                // === 7. TRỘN MÀU NƯỚC & PHẢN XẠ ===
                half3 waterAlbedo = lerp(shallowColor, deepColor, waterFog);
                
                // Noise coloring (Từ COBBLEVERSE water.glsl L165-168)
                // Thêm variation nhẹ để nước không đơn điệu
                float noiseCol = smoothNoise2D(IN.positionWS.xz * 0.08 + _Time.y * 0.005).x;
                noiseCol = noiseCol * 0.25;
                waterAlbedo = pow(max(waterAlbedo, 0.001), 1.0 + noiseCol);
                
                half3 finalColor = lerp(waterAlbedo, skyColor, fresnelM);
                
                // === 8. GGX SPECULAR (Thay thế pow(NdotH) cũ) ===
                // Từ COBBLEVERSE: dùng GGX với smoothnessG cao cho nước
                // COBBLEVERSE water.glsl L297: smoothnessG = 1.0 cho water style 3
                float specular = GGX_Water(normalWS, viewDir, mainLight.direction, NdotL, 1.0);
                
                // Highlight mult (Từ COBBLEVERSE water.glsl L299-304)
                // Tính highlight dựa trên bump normals cho specular path chân thực hơn
                float highlightMult = specular;
                highlightMult = lerp(highlightMult * highlightMult * highlightMult * highlightMult * 1.21, 
                                     1.0, 0.0) * 0.24; // pow4 * 1.1^4 ≈ 1.21, blend với miplevel
                
                // Áp dụng specular highlight (Từ COBBLEVERSE mainLighting.glsl L790-795)
                half3 lightHighlight = mainLight.shadowAttenuation > 0.01 
                                     ? half3(mainLight.shadowAttenuation, mainLight.shadowAttenuation, mainLight.shadowAttenuation)
                                     : half3(0,0,0);
                lightHighlight *= specular * highlightColor;
                finalColor += lightHighlight * fresnelM;
                
                // === 9. BỌT BIỂN (Foam) — Từ COBBLEVERSE water.glsl L214-258 ===
                // Foam dựa trên depth difference (đơn giản hóa từ COBBLEVERSE)
                float foamThreshold = 0.3;
                float foam = saturate(1.0 - depthDifference / foamThreshold);
                foam = foam * foam; // pow2 (COBBLEVERSE L235)
                foam *= 0.4 + 0.25 * 1.0; // L237: 0.4 + 0.25 * lmCoord.y
                // Chỉ tạo foam khi mặt nước hướng lên (COBBLEVERSE L241)
                foam *= saturate((frac(IN.positionWS.y) - 0.7) * 10.0);
                
                half3 foamColor = half3(0.9, 0.95, 1.05); // COBBLEVERSE L243
                finalColor = lerp(finalColor, foamColor, foam);
                
                // === 10. FOG ===
                finalColor = MixFog(finalColor, IN.fogFactor);
                
                // === 11. ALPHA (Từ COBBLEVERSE water.glsl L293) ===
                // color.a = mix(color.a, 1.0, fresnel4) — Mép nhìn ngang = hoàn toàn đục
                half alpha = lerp(waterAlpha, 1.0, fresnel4);
                alpha = max(alpha, foam); // Foam luôn đục
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
