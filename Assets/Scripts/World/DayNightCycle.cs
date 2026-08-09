using UnityEngine;
using System;

[Serializable]
public struct SkyKeyframe
{
    public float timeOfDay; // 0.0 (Bình minh) đến 1.0
    public Color topColor;
    public Color horizonColor;
    public float lightIntensity;
    public Color lightColor;
}

public class DayNightCycle : MonoBehaviour
{
    public Light sunLight;
    public Material skyMaterial;
    
    [Header("Thời gian (1 ngày = x giây)")]
    public float dayDurationInSeconds = 1200f; // 20 phút cho một chu kỳ sáng tối
    [Range(0, 1)] public float currentTime = 0.5f; // Bắt đầu ở giữa trưa
    
    [Header("Cài đặt Môi trường")]
    public SkyKeyframe[] keyframes;

    private void Start()
    {
        // Sương mù che phủ vùng xa — bắt đầu mờ vùng giữa render distance, kết thúc ở mép xa
        // viewDistance = 35 chunk = 560 blocks → fog bắt đầu từ 460 và che hết ở 560
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 460f;
        RenderSettings.fogEndDistance = 560f;
        
        // Ambient Mode = Color để ta tự kiểm soát hoàn toàn
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        
        if (keyframes == null || keyframes.Length == 0)
        {
            // Phong cách ánh sáng mô phỏng shader "Complementary Unbound"
            keyframes = new SkyKeyframe[]
            {
                // Đêm: Tối hơn rất nhiều, bầu trời gần như đen, ánh trăng mờ
                new SkyKeyframe { timeOfDay = 0.0f, topColor = new Color(0.01f, 0.015f, 0.03f), horizonColor = new Color(0.02f, 0.04f, 0.08f), lightIntensity = 0.05f, lightColor = new Color(0.2f, 0.4f, 0.8f) }, 
                // Bình minh: Ánh nắng vàng cam RỰC RỠ (Golden Hour), chân trời ngả cam
                new SkyKeyframe { timeOfDay = 0.25f, topColor = new Color(0.15f, 0.35f, 0.75f), horizonColor = new Color(1.0f, 0.5f, 0.15f), lightIntensity = 1.2f, lightColor = new Color(1.0f, 0.55f, 0.2f) }, 
                // Trưa: Ánh nắng vàng, BẦU TRỜI XANH DA TRỜI RỰC RỠ (Xanh thẫm ở đỉnh, xanh lơ ở chân trời)
                new SkyKeyframe { timeOfDay = 0.5f, topColor = new Color(0.05f, 0.35f, 0.95f), horizonColor = new Color(0.3f, 0.75f, 1.0f), lightIntensity = 5.0f, lightColor = new Color(1.0f, 0.85f, 0.2f) }, 
                // Hoàng hôn: Đỏ cam RỰC CHÁY, tương phản mạnh
                new SkyKeyframe { timeOfDay = 0.75f, topColor = new Color(0.15f, 0.3f, 0.6f), horizonColor = new Color(1.0f, 0.4f, 0.12f), lightIntensity = 1.2f, lightColor = new Color(1.0f, 0.5f, 0.18f) }, 
                // Đêm
                new SkyKeyframe { timeOfDay = 1.0f, topColor = new Color(0.01f, 0.015f, 0.03f), horizonColor = new Color(0.02f, 0.04f, 0.08f), lightIntensity = 0.05f, lightColor = new Color(0.2f, 0.4f, 0.8f) } 
            };
        }
    }

    private void Update()
    {
        currentTime += Time.deltaTime / dayDurationInSeconds;
        if (currentTime >= 1f) currentTime = 0f;
        
        UpdateTime(currentTime);
    }
    
    private void UpdateTime(float t)
    {
        // 1. Xoay mặt trời
        // 0.25 (Bình minh) -> X = 0
        // 0.5 (Trưa) -> X = 90
        // 0.75 (Hoàng hôn) -> X = 180
        float sunAngle = (t - 0.25f) * 360f;
        if (sunLight != null)
        {
            sunLight.transform.rotation = Quaternion.Euler(sunAngle, -30f, 0f);
            
            // Ép vị trí của mặt trời đi theo camera để Mặt trời (hình vuông) không bao giờ bị bỏ lại phía sau
            if (Camera.main != null)
            {
                sunLight.transform.position = Camera.main.transform.position;
            }
        }
        
        // 2. Nội suy giữa 2 Keyframe
        SkyKeyframe currentKF = keyframes[0];
        SkyKeyframe nextKF = keyframes[0];
        float blend = 0f;
        
        for (int i = 0; i < keyframes.Length; i++)
        {
            if (t >= keyframes[i].timeOfDay)
            {
                currentKF = keyframes[i];
                if (i < keyframes.Length - 1)
                {
                    nextKF = keyframes[i + 1];
                    blend = (t - currentKF.timeOfDay) / (nextKF.timeOfDay - currentKF.timeOfDay);
                }
                else
                {
                    nextKF = keyframes[0]; // Wrap around về 0
                    blend = (t - currentKF.timeOfDay) / (1f - currentKF.timeOfDay);
                }
            }
        }
        
        // --- NÂNG CẤP KHÍ QUYỂN (Atmospheric Scattering) ---
        // Thay vì Lerp tuyến tính (Linear), ta dùng hàm Exponential (Mũ 1.5 -> 2.0)
        // để mô phỏng sự tán xạ ánh sáng. Bầu trời sẽ giữ nguyên độ trong trẻo lâu hơn, 
        // và chỉ gắt lên (đỏ ối) khi mặt trời thực sự chạm chân trời.
        float blendExp = Mathf.Pow(blend, 1.5f);
        
        // 3. Chỉnh độ sáng và màu của ánh sáng (để ánh sáng hoàng hôn hắt màu cam lên đất)
        Color currentLightColor = Color.Lerp(currentKF.lightColor, nextKF.lightColor, blendExp);
        if (sunLight != null)
        {
            sunLight.intensity = Mathf.Lerp(currentKF.lightIntensity, nextKF.lightIntensity, blendExp);
            sunLight.color = currentLightColor;
        }
            
        // 4. Đồng bộ màu Skybox, Fog, Ambient, và Sun Glow
        if (skyMaterial != null)
        {
            Color top = Color.Lerp(currentKF.topColor, nextKF.topColor, blendExp);
            Color horizon = Color.Lerp(currentKF.horizonColor, nextKF.horizonColor, blendExp);
            
            skyMaterial.SetColor("_TopColor", top);
            skyMaterial.SetColor("_HorizonColor", horizon);
            
            // === SUN GLOW: Truyền hướng mặt trời vào Skybox Shader ===
            if (sunLight != null)
            {
                skyMaterial.SetVector("_SunDir", sunLight.transform.forward);
                // Màu hào quang = Màu ánh sáng hiện tại (cam lúc hoàng hôn, vàng lúc trưa)
                skyMaterial.SetColor("_SunColor", currentLightColor);
                // Cường độ hào quang tỉ lệ với intensity (tăng hệ số nhân lên)
                skyMaterial.SetFloat("_SunGlowIntensity", sunLight.intensity * 0.6f);
            }
            
            RenderSettings.fogColor = horizon;
        }
        
        // 5. Ambient Light đổi theo thời gian ngày (trưa sáng, đêm tối)
        float ambientBrightness = Mathf.Lerp(currentKF.lightIntensity, nextKF.lightIntensity, blend);
        Color ambientColor = Color.Lerp(currentLightColor * 0.15f, currentLightColor * 0.3f, ambientBrightness);
        RenderSettings.ambientLight = ambientColor;
        
        // Gửi thẳng màu Ambient xuống toàn bộ Shader (Sửa lỗi Instancing đọc SampleSH bị đen)
        Shader.SetGlobalColor("_GlobalAmbientColor", ambientColor);
        
        // === 6. GLOBAL SHADER VARIABLES (Mô phỏng COBBLEVERSE/Complementary Shaders) ===
        // SdotU = dot(sunDirection, upVector) — Cơ sở tính toán ánh sáng theo thời gian ngày
        // Mặt trời ở đỉnh (trưa): SdotU ≈ 1, ở chân trời: SdotU ≈ 0, dưới chân trời (đêm): SdotU < 0
        float SdotU = sunLight != null ? Vector3.Dot(-sunLight.transform.forward, Vector3.up) : 0f;
        
        // sunVisibility: 0 khi đêm, 1 khi mặt trời trên chân trời (Từ COBBLEVERSE common.glsl)
        // clamp(SdotU + 0.0625, 0.0, 0.125) / 0.125
        float sunVisibility = Mathf.Clamp01((SdotU + 0.0625f) / 0.125f);
        float sunVisibility2 = sunVisibility * sunVisibility;
        
        // sunFactor: Chuyển đổi mượt giữa ngày/đêm (dùng cho sky color blending)
        // clamp(SdotU + 0.375, 0.0, 0.75) / 0.75 khi SdotU < 0, ngược lại dùng 0.0625
        float sunFactor;
        if (SdotU < 0f)
            sunFactor = Mathf.Clamp01((SdotU + 0.375f) / 0.75f);
        else
            sunFactor = Mathf.Clamp01((SdotU + 0.03125f) / 0.0625f);
        
        // noonFactor: 1 khi đúng trưa, 0 khi bình minh/hoàng hôn (cho specular, shadow contrast)
        float noonFactor = Mathf.Clamp01(SdotU);
        noonFactor = noonFactor * noonFactor; // pow2 cho chuyển đổi mượt hơn
        
        // nightFactor: 1 khi đêm hoàn toàn, 0 khi ngày 
        float nightFactor = 1f - sunVisibility;
        
        // shadowTime: Bóng đổ chỉ rõ lúc trưa/đêm, mờ lúc bình minh/hoàng hôn
        float shadowTimeVar1 = Mathf.Abs(sunVisibility - 0.5f) * 2f;
        float shadowTime = shadowTimeVar1 * shadowTimeVar1 * shadowTimeVar1 * shadowTimeVar1; // pow4
        
        // rainFactor placeholder (cho tương lai khi có hệ thống thời tiết)
        float rainFactor = 0f;
        
        // Gửi tất cả xuống GPU
        Shader.SetGlobalFloat("_SunVisibility", sunVisibility);
        Shader.SetGlobalFloat("_SunVisibility2", sunVisibility2);
        Shader.SetGlobalFloat("_SunFactor", sunFactor);
        Shader.SetGlobalFloat("_NoonFactor", noonFactor);
        Shader.SetGlobalFloat("_NightFactor", nightFactor);
        Shader.SetGlobalFloat("_ShadowTime", shadowTime);
        Shader.SetGlobalFloat("_RainFactor", rainFactor);
        Shader.SetGlobalFloat("_SdotU", SdotU);
    }

    public Vector3 GetSunDirection()
    {
        return sunLight != null ? sunLight.transform.forward : Vector3.down;
    }

    public Color GetSunColor()
    {
        return sunLight != null ? sunLight.color : Color.white;
    }

    /// <summary>
    /// Trả về màu chân trời hiện tại (dùng cho SunManager pha neon)
    /// </summary>
    public Color GetSkyHorizonColor()
    {
        if (skyMaterial != null)
        {
            return skyMaterial.GetColor("_HorizonColor");
        }
        return Color.white;
    }
}
