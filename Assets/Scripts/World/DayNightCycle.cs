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
    public float fogDensity;
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
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared; // Fog điện ảnh
        
        // Ambient Mode = Color để ta tự kiểm soát hoàn toàn
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        
        if (keyframes == null || keyframes.Length == 0)
        {
            keyframes = new SkyKeyframe[]
            {
                // Đêm: Tối hơn rất nhiều, ánh trăng mờ, fog dày hơn một chút
                new SkyKeyframe { timeOfDay = 0.0f, topColor = new Color(0.01f, 0.015f, 0.03f), horizonColor = new Color(0.02f, 0.04f, 0.08f), lightIntensity = 0.05f, lightColor = new Color(0.2f, 0.4f, 0.8f), fogDensity = 0.0025f }, 
                // Bình minh: Ánh nắng vàng cam, intensity an toàn (không bị chói)
                new SkyKeyframe { timeOfDay = 0.25f, topColor = new Color(0.15f, 0.35f, 0.75f), horizonColor = new Color(1.0f, 0.5f, 0.15f), lightIntensity = 0.8f, lightColor = new Color(1.0f, 0.6f, 0.3f), fogDensity = 0.002f }, 
                // Trưa: Ánh nắng vàng nhạt, bầu trời xanh. Intensity = 1.2 (an toàn, không phải 5.0)
                new SkyKeyframe { timeOfDay = 0.5f, topColor = new Color(0.05f, 0.35f, 0.95f), horizonColor = new Color(0.3f, 0.75f, 1.0f), lightIntensity = 1.2f, lightColor = new Color(1.0f, 0.9f, 0.7f), fogDensity = 0.0015f }, 
                // Hoàng hôn: Đỏ cam, tương phản
                new SkyKeyframe { timeOfDay = 0.75f, topColor = new Color(0.15f, 0.3f, 0.6f), horizonColor = new Color(1.0f, 0.4f, 0.12f), lightIntensity = 0.8f, lightColor = new Color(1.0f, 0.55f, 0.25f), fogDensity = 0.002f }, 
                // Đêm
                new SkyKeyframe { timeOfDay = 1.0f, topColor = new Color(0.01f, 0.015f, 0.03f), horizonColor = new Color(0.02f, 0.04f, 0.08f), lightIntensity = 0.05f, lightColor = new Color(0.2f, 0.4f, 0.8f), fogDensity = 0.0025f } 
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
        
        // --- NÂNG CẤP KHÍ QUYỂN & NỘI SUY MƯỢT ---
        // Nội suy mượt theo phương pháp điện ảnh (SmoothStep/Hermite)
        float smoothBlend = blend * blend * (3f - 2f * blend);
        
        // 3. Chỉnh độ sáng và màu của ánh sáng
        Color currentLightColor = Color.Lerp(currentKF.lightColor, nextKF.lightColor, smoothBlend);
        
        // Tính toán SdotU (Sun Elevation)
        float SdotU = sunLight != null ? Vector3.Dot(-sunLight.transform.forward, Vector3.up) : 0f;
        float sunAboveHorizon = Mathf.SmoothStep(0.0f, 0.12f, SdotU); // Mượt hơn
        
        if (sunLight != null)
        {
            sunLight.intensity = Mathf.Lerp(currentKF.lightIntensity, nextKF.lightIntensity, smoothBlend);
            sunLight.color = currentLightColor;
            
            // Điều chỉnh bóng đổ theo elevation (đêm tắt bóng, sáng/tối bóng nhạt, trưa bóng gắt)
            float shadowStrength = Mathf.Lerp(0.3f, 0.8f, sunAboveHorizon);
            sunLight.shadowStrength = SdotU < -0.1f ? 0f : shadowStrength;
        }
            
        // 4. Đồng bộ màu Skybox, Fog, Ambient, và Sun Glow
        float currentFogDensity = Mathf.Lerp(currentKF.fogDensity, nextKF.fogDensity, smoothBlend);
        RenderSettings.fogDensity = currentFogDensity;

        Color top = Color.Lerp(currentKF.topColor, nextKF.topColor, smoothBlend);
        Color horizon = Color.Lerp(currentKF.horizonColor, nextKF.horizonColor, smoothBlend);

        if (skyMaterial != null)
        {
            skyMaterial.SetColor("_TopColor", top);
            skyMaterial.SetColor("_HorizonColor", horizon);
            
            if (sunLight != null)
            {
                skyMaterial.SetVector("_SunDir", sunLight.transform.forward);
                skyMaterial.SetColor("_SunColor", currentLightColor);
                // Giảm Glow Intensity, điều khiển từ shader
                skyMaterial.SetFloat("_SunGlowIntensity", 1.0f);
            }
        }
        
        // 5. Ambient Light độc lập
        // Ambient Light cần nhỏ hơn Direct Light rất nhiều
        float ambientBrightness = Mathf.Lerp(0.03f, 0.25f, sunAboveHorizon);
        Color ambientColor = Color.Lerp(currentKF.horizonColor, currentLightColor, 0.3f) * ambientBrightness;
        RenderSettings.ambientLight = ambientColor;
        
        // 6. Màu Sương Mù (Fog Color)
        // Không dùng thẳng horizon color. Pha trộn horizon và ambient, làm tối đi một chút.
        Color fogColor = Color.Lerp(horizon, ambientColor, 0.3f);
        fogColor = Color.Lerp(fogColor, new Color(0.01f, 0.02f, 0.05f), 1f - sunAboveHorizon); // Đêm tối fog
        RenderSettings.fogColor = fogColor;
        
        Shader.SetGlobalColor("_GlobalAmbientColor", ambientColor);
        
        // === 7. GLOBAL SHADER VARIABLES ===
        float sunVisibility = sunAboveHorizon;
        float sunVisibility2 = sunVisibility * sunVisibility;
        
        // sunFactor: Chuyển đổi mượt giữa ngày/đêm
        float sunFactor = Mathf.SmoothStep(-0.1f, 0.1f, SdotU);
        
        // noonFactor: 1 khi đúng trưa, 0 khi bình minh/hoàng hôn
        float noonFactor = Mathf.SmoothStep(0.2f, 0.8f, SdotU);
        noonFactor = noonFactor * noonFactor;
        
        // nightFactor
        float nightFactor = 1f - sunVisibility;
        
        // shadowTime
        float shadowTimeVar1 = Mathf.Abs(sunVisibility - 0.5f) * 2f;
        float shadowTime = shadowTimeVar1 * shadowTimeVar1 * shadowTimeVar1 * shadowTimeVar1;
        
        float rainFactor = 0f;
        
        // Global variables cho Cinematic Fog
        Shader.SetGlobalFloat("_FogHeight", 50f);
        Shader.SetGlobalFloat("_FogHeightFalloff", 0.05f);
        Shader.SetGlobalColor("_AtmosphereColor", fogColor);
        Shader.SetGlobalFloat("_AtmosphereStrength", 1.0f);
        
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
