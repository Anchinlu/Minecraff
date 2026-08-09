Shader "Custom/GradientSkybox"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.3, 0.5, 0.9, 1)
        _HorizonColor ("Horizon Color", Color) = (0.7, 0.85, 1, 1)
        _Exponent ("Blend Exponent", Range(0.1, 5)) = 1.0
        _SunDir ("Sun Direction", Vector) = (0, 1, 0, 0)
        _SunColor ("Sun Glow Color", Color) = (1, 0.9, 0.7, 1)
        _SunGlowSize ("Sun Glow Size", Range(0.01, 0.5)) = 0.15
        _SunGlowIntensity ("Sun Glow Intensity", Range(0, 5)) = 2.0
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
            half4 _SunDir;
            half4 _SunColor;
            half _SunGlowSize;
            half _SunGlowIntensity;
            
            // === GLOBAL SHADER VARIABLES (Từ DayNightCycle.cs, mô phỏng COBBLEVERSE) ===
            float _SunVisibility;
            float _SunFactor;
            float _NoonFactor;
            float _NightFactor;
            float _ShadowTime;
            float _RainFactor;

            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 dir:TEXCOORD0; };

            // Dither chống banding (Từ COBBLEVERSE sky.glsl L107)
            float InterleavedGradientNoise(float2 screenPos)
            {
                return frac(52.9829189 * frac(0.06711056 * screenPos.x + 0.00583715 * screenPos.y));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }
            half4 frag(Varyings IN):SV_Target
            {
                float3 viewDir = normalize(IN.dir);
                float3 sunDirection = normalize(_SunDir.xyz);
                
                // === COBBLEVERSE SKY ATMOSPHERE (Từ sky.glsl) ===
                
                // VdotU = dot(viewDir, up) — Dùng cho sky gradient
                float VdotU = viewDir.y;
                float VdotUmax0 = max(VdotU, 0.0);
                
                // VdotS = dot(viewDir, sunDir) — Dùng cho sun glare & sunset
                float VdotS = dot(viewDir, -sunDirection);
                float VdotSM1 = VdotS > 0 ? VdotS * VdotS : 0.0;         // pow2
                float VdotSM2 = VdotSM1 * VdotSM1;                         // pow4
                
                // === 1. SKY GRADIENT (Từ COBBLEVERSE sky.glsl L27-37) ===
                // Set sky gradient: VdotUM1 = pow2(1 - VdotUmax0)
                float VdotUM1 = (1.0 - VdotUmax0) * (1.0 - VdotUmax0);
                // Mô phỏng pow2(VdotSM1) ảnh hưởng tới gradient: bình minh/hoàng hôn kéo dãn
                VdotUM1 = pow(VdotUM1, 1.0 - VdotSM2 * 0.4);
                
                // Mix upColor → middleColor (horizon) theo gradient
                half4 skyColor = lerp(_TopColor, _HorizonColor, VdotUM1);
                
                // === 2. SUNSET COLOR SCATTERING (Từ COBBLEVERSE sky.glsl L40-43) ===
                // Màu hoàng hôn tán xạ ở chân trời
                float VdotUM2 = (1.0 - abs(VdotU));
                VdotUM2 = VdotUM2 * VdotUM2; // pow2
                VdotUM2 = VdotUM2 * VdotUM2 * (3.0 - 2.0 * VdotUM2); // smoothstep-like (Hermite)
                
                // invNoonFactor: 1 khi bình minh/hoàng hôn, 0 khi trưa
                float invNoonFactor = 1.0 - _NoonFactor;
                VdotUM2 *= (0.7 + VdotSM1 * 0.3) * invNoonFactor * _SunFactor;
                
                // Sunset color: dùng _SunColor (đã đặt đúng cam/đỏ từ DayNightCycle)
                // Thêm sáng lên phía mặt trời (1 + VdotSM1 * 0.3)
                half4 sunsetColor = _SunColor * (1.0 + VdotSM1 * 0.3);
                float invRainFactor = 1.0 - _RainFactor;
                skyColor = lerp(skyColor, sunsetColor, VdotUM2 * invRainFactor);
                
                // === 3. SKY GROUND DARKENING (Từ COBBLEVERSE sky.glsl L55) ===
                // Bầu trời phía dưới chân trời tối đi: smoothstep1(pow2(1 + min(VdotU, 0)))
                float groundFade = 1.0 + min(VdotU, 0.0);
                groundFade = groundFade * groundFade; // pow2
                groundFade = groundFade * groundFade * (3.0 - 2.0 * groundFade); // smoothstep
                skyColor *= groundFade;
                
                // === 4. SUN GLARE (Từ COBBLEVERSE sky.glsl L62-97) ===
                // Mô phỏng tán xạ ánh sáng của COBBLEVERSE — mượt và tự nhiên hơn pow đơn giản
                float sunDot = saturate(dot(viewDir, -sunDirection));
                
                // Glare scatter: intensity thay đổi theo khoảng cách tới mặt trời
                // COBBLEVERSE: glareScatter = 3.0 * (2.0 - clamp01(VdotS * 1000))
                float VdotSML = _SunVisibility > 0.5 ? VdotS : -VdotS;
                
                if (VdotSML > 0.0)
                {
                    float glareScatter = 3.0 * (2.0 - saturate(VdotS * 1000.0));
                    float VdotSM4 = pow(abs(VdotS), glareScatter);
                    
                    // COBBLEVERSE glare formula (L72-73)
                    float visfactor = 0.075;
                    float glare = visfactor / (1.0 - (1.0 - visfactor) * VdotSM4) - visfactor;
                    glare *= 0.25;
                    
                    // Glare color: blue-ish at night, warm orange at day (L76)
                    half3 glareColor = lerp(half3(0.38, 0.4, 0.5) * 0.3, 
                                            half3(1.5, 0.7, 0.3) + half3(0.0, 0.5, 0.5) * _NoonFactor, 
                                            _SunVisibility);
                    
                    // Giảm glare khi mưa (L85)
                    glare *= (1.0 - 0.8 * _RainFactor);
                    
                    // SUN_GLARE_AMOUNT = 10 (COBBLEVERSE L4), MOON_GLARE_AMOUNT = 10
                    glare *= lerp(1.0, 1.0, _SunVisibility); // Cả 2 = 10 * 0.1 = 1.0
                    
                    skyColor.rgb += glare * _ShadowTime * glareColor;
                }
                
                // Vầng sáng tập trung (core glow) — giảm bớt để nhìn rõ mặt trời
                float coreGlow = pow(sunDot, 1.0 / max(_SunGlowSize, 0.001));
                coreGlow *= _SunGlowIntensity;
                
                // Halo mềm bao quanh — giảm mạnh để không che mặt trời
                float halo = pow(sunDot, 5.0);
                halo *= _SunGlowIntensity * 0.1;
                
                skyColor.rgb += _SunColor.rgb * (coreGlow + halo);
                
                // === 5. DITHER CHỐNG BANDING (Từ COBBLEVERSE sky.glsl L107) ===
                // finalSky += (dither - 0.5) / 128.0
                float dither = InterleavedGradientNoise(IN.positionHCS.xy);
                skyColor.rgb += (dither - 0.5) / 128.0;
                
                skyColor.a = 1.0;
                return skyColor;
            }
            ENDHLSL
        }
    }
}
