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
    public float dayDurationInSeconds = 60f; 
    [Range(0, 1)] public float currentTime = 0.5f; // Bắt đầu ở giữa trưa
    
    [Header("Cài đặt Môi trường")]
    public SkyKeyframe[] keyframes;

    private void Start()
    {
        // Bật sương mù (Fog) để tạo chiều sâu - Đã tăng khoảng cách cho thoáng hơn
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 100f;
        RenderSettings.fogEndDistance = 200f;
        
        if (keyframes == null || keyframes.Length == 0)
        {
            // Phong cách ánh sáng mô phỏng shader "Complementary Unbound"
            keyframes = new SkyKeyframe[]
            {
                // Đêm: Ánh trăng xanh lam lạnh (Cyan-Blue), bầu trời tối thăm thẳm
                new SkyKeyframe { timeOfDay = 0.0f, topColor = new Color(0.02f, 0.03f, 0.08f), horizonColor = new Color(0.05f, 0.1f, 0.2f), lightIntensity = 0.15f, lightColor = new Color(0.4f, 0.6f, 1.0f) }, 
                // Bình minh: Ánh nắng vàng cam rực rỡ (Golden Hour), chân trời ngả cam
                new SkyKeyframe { timeOfDay = 0.25f, topColor = new Color(0.15f, 0.3f, 0.6f), horizonColor = new Color(1.0f, 0.45f, 0.15f), lightIntensity = 0.8f, lightColor = new Color(1.0f, 0.5f, 0.2f) }, 
                // Trưa: Nắng gắt, màu trắng tinh khiết hơi ám vàng rất nhẹ, chân trời xanh ngọc sáng
                new SkyKeyframe { timeOfDay = 0.5f, topColor = new Color(0.1f, 0.35f, 0.8f), horizonColor = new Color(0.55f, 0.85f, 1.0f), lightIntensity = 1.1f, lightColor = new Color(1.0f, 0.98f, 0.95f) }, 
                // Hoàng hôn: Đỏ cam rực cháy, tương phản mạnh
                new SkyKeyframe { timeOfDay = 0.75f, topColor = new Color(0.2f, 0.25f, 0.5f), horizonColor = new Color(1.0f, 0.35f, 0.1f), lightIntensity = 0.8f, lightColor = new Color(1.0f, 0.45f, 0.15f) }, 
                // Đêm
                new SkyKeyframe { timeOfDay = 1.0f, topColor = new Color(0.02f, 0.03f, 0.08f), horizonColor = new Color(0.05f, 0.1f, 0.2f), lightIntensity = 0.15f, lightColor = new Color(0.4f, 0.6f, 1.0f) } 
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
        
        // 3. Chỉnh độ sáng và màu của ánh sáng (để ánh sáng hoàng hôn hắt màu cam lên đất)
        if (sunLight != null)
        {
            sunLight.intensity = Mathf.Lerp(currentKF.lightIntensity, nextKF.lightIntensity, blend);
            sunLight.color = Color.Lerp(currentKF.lightColor, nextKF.lightColor, blend);
        }
            
        // 4. Đồng bộ màu Skybox và Fog
        if (skyMaterial != null)
        {
            Color top = Color.Lerp(currentKF.topColor, nextKF.topColor, blend);
            Color horizon = Color.Lerp(currentKF.horizonColor, nextKF.horizonColor, blend);
            
            skyMaterial.SetColor("_TopColor", top);
            skyMaterial.SetColor("_HorizonColor", horizon);
            
            RenderSettings.fogColor = horizon;
        }
    }

    public Vector3 GetSunDirection()
    {
        return sunLight != null ? sunLight.transform.forward : Vector3.down;
    }

    public Color GetSunColor()
    {
        return sunLight != null ? sunLight.color : Color.white;
    }
}
