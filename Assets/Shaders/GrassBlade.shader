Shader "Custom/GrassBlade"
{
    Properties
    {
        _MainTex ("Texture Atlas", 2D) = "white" {}
        _WindStrength ("Wind Strength", Range(0,0.5)) = 0.15
        _WindSpeed ("Wind Speed", Range(0,5)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        // === PASS 1: Forward Lit (nhận ánh sáng + bóng) ===
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            sampler2D _MainTex;
            float _WindStrength;
            float _WindSpeed;
            float4 _GlobalAmbientColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                half   fogFactor   : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);

                // Gió hữu cơ (Organic Wind) mô phỏng Complementary Shaders
                float wind = _Time.y * _WindSpeed * 50.0;
                float magnitude = sin(wind * 0.0027 + worldPos.x + worldPos.y) * 0.04 + 0.04;
                float d0 = sin(wind * 0.0127);
                float d1 = sin(wind * 0.0089);
                float d2 = sin(wind * 0.0114);
                float waveX = magnitude * sin(wind * 0.0224 + d1 + d2 + worldPos.x - worldPos.z + worldPos.y);
                float waveZ = magnitude * sin(wind * 0.0063 + d0 + d1 - worldPos.x + worldPos.z + worldPos.y);
                
                // Scale wave theo độ cao của cỏ (ngọn cỏ lay mạnh hơn gốc)
                float swayMask = IN.positionOS.y;
                float3 sway = float3(waveX * 6.0, 0, waveZ * 8.0) * _WindStrength * swayMask;

                float3 displaced = IN.positionOS.xyz + sway;

                OUT.positionHCS = TransformObjectToHClip(displaced);
                OUT.positionWS = TransformObjectToWorld(displaced);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = tex2D(_MainTex, IN.uv);
                clip(texColor.a - 0.5);

                half3 albedo = texColor.rgb * IN.color.rgb;

                // === ÁNH SÁNG & BÓNG ===
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // 1. Giảm gắt bóng đổ trên cỏ (grass không bao giờ đen xì)
                // COBBLEVERSE: subsurface mode — shadow mềm hơn cho foliage
                half shadow = lerp(0.3, 1.0, mainLight.shadowAttenuation);
                half shadowMultFloat = shadow;

                // 2. Khử ám màu mặt trời: Mặt trời đang rất Vàng/Cam. Màu Xanh Lục + Cam = Nâu (Cỏ héo).
                // Do đó ta pha màu mặt trời về gần màu Trắng hơn để giữ nguyên độ tươi của cỏ!
                half3 sunTint = lerp(half3(1.0, 1.0, 1.0), mainLight.color, 0.4); 
                half3 lightColor = sunTint * mainLight.distanceAttenuation * shadow * 1.5; // Tăng sáng nắng

                // Cỏ dùng normal hướng lên trời (giả lập cỏ luôn nhận nắng từ trên xuống)
                half NdotL = saturate(dot(half3(0, 1, 0), normalize(mainLight.direction)));

                // Wrap lighting rất mềm cho cỏ
                half wrapNdotL = saturate((NdotL + 0.6) / 1.6);

                // 3. Ambient ánh sáng môi trường động
                // Lỗi Unity: Graphics.DrawMeshInstanced KHÔNG gửi dữ liệu SampleSH xuống GPU!
                // Do đó SampleSH luôn trả về màu đen (0,0,0). Ta phải dùng biến Global.
                half3 ambient = _GlobalAmbientColor.rgb * 2.5; 
                
                // Đảm bảo cỏ không bao giờ bị đen xì tuyệt đối kể cả ban đêm
                ambient = max(ambient, half3(0.05, 0.08, 0.05));

                // === 4. SUBSURFACE SCATTERING (Từ COBBLEVERSE mainLighting.glsl L346-350) ===
                // Ánh sáng xuyên qua lá cỏ khi player nhìn về phía mặt trời
                // VdotL = dot(viewDir, lightVec) — nếu > 0 = nhìn về hướng mặt trời
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float VdotL = dot(viewDir, normalize(mainLight.direction));
                
                // subsurfaceHighlight = pow(max(VdotL, 0), 10) * 0.8 (COBBLEVERSE L350: subsurfaceMode == 1)
                float subsurfaceHighlight = pow(max(VdotL, 0.0), 10.0) * 0.8;
                
                // Subsurface chỉ hoạt động khi không ở dưới nước
                // Highlight color: mượt mại theo ánh sáng mặt trời
                half3 subsurfaceGlow = lightColor * subsurfaceHighlight * 0.5;

                // === 5. COBBLEVERSE AO CHO FOLIAGE (mainLighting.glsl L753) ===
                // subsurface mode: AO mềm hơn khi có nắng trực tiếp
                // vanillaAO = mix(min1(vanillaAO * 1.15), 1.0, shadowMultFloat)
                half vanillaAO = IN.color.a;
                vanillaAO = lerp(min(vanillaAO * 1.15, 1.0), 1.0, shadowMultFloat);

                half3 finalLight = ambient + (lightColor * wrapNdotL) + subsurfaceGlow;
                
                // Trộn ánh sáng vào cỏ * AO
                half3 finalColor = albedo * finalLight * vanillaAO;

                // Sương mù
                finalColor = MixFog(finalColor, IN.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // === PASS 2: Shadow Caster (đổ bóng xuống mặt đất) ===
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            sampler2D _MainTex;
            float _WindStrength;
            float _WindSpeed;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vertShadow(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                // Gió hữu cơ (Organic Wind) cho đổ bóng
                float wind = _Time.y * _WindSpeed * 50.0;
                float magnitude = sin(wind * 0.0027 + worldPos.x + worldPos.y) * 0.04 + 0.04;
                float d0 = sin(wind * 0.0127);
                float d1 = sin(wind * 0.0089);
                float d2 = sin(wind * 0.0114);
                float waveX = magnitude * sin(wind * 0.0224 + d1 + d2 + worldPos.x - worldPos.z + worldPos.y);
                float waveZ = magnitude * sin(wind * 0.0063 + d0 + d1 - worldPos.x + worldPos.z + worldPos.y);
                
                float swayMask = IN.positionOS.y;
                float3 sway = float3(waveX * 6.0, 0, waveZ * 8.0) * _WindStrength * swayMask;

                float3 displaced = IN.positionOS.xyz + sway;

                float3 positionWS = TransformObjectToWorld(displaced);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, GetMainLight().direction));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionHCS = positionCS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                half4 texColor = tex2D(_MainTex, IN.uv);
                clip(texColor.a - 0.5); // Chỉ đổ bóng ở pixel có cỏ, không đổ bóng ở pixel trong suốt
                return 0;
            }
            ENDHLSL
        }
    }
}
