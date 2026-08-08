using UnityEngine;
using UnityEditor;

public class SetupSkybox : EditorWindow
{
    [MenuItem("Minecraft/Bật Bầu Trời (Setup Skybox)")]
    public static void Setup()
    {
        // Tạo Material mới dùng Gradient Skybox Shader
        Shader gradientShader = Shader.Find("Custom/GradientSkybox");
        if (gradientShader == null)
        {
            Debug.LogError("Không tìm thấy Shader Custom/GradientSkybox!");
            return;
        }

        Material skyMat = new Material(gradientShader);
        
        // Thiết lập màu sắc mặc định giống như ban ngày
        skyMat.SetColor("_TopColor", new Color(0.3f, 0.5f, 0.9f));
        skyMat.SetColor("_HorizonColor", new Color(0.7f, 0.85f, 1f));
        
        // Lưu Material thành file trong project để dùng chung cho Editor
        string matPath = "Assets/Resources/Mat_GradientSkybox.mat";
        AssetDatabase.CreateAsset(skyMat, matPath);
        
        // Ép Unity sử dụng bầu trời mới cho Scene hiện tại
        RenderSettings.skybox = skyMat;
        
        // Bật Fog
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.7f, 0.85f, 1f);
        
        Debug.Log("🎉 Đã gỡ bỏ bầu trời ảnh cũ và thay bằng Gradient Skybox thành công!");
    }
}
