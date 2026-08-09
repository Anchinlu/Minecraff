using UnityEngine;

/// <summary>
/// Công cụ hỗ trợ Tắt/Bật nhanh các Shader phức tạp và hiệu ứng môi trường
/// để tập trung vào việc test logic của Block với mức FPS cao nhất.
/// Nhấn phím F4 để Tắt/Bật.
/// </summary>
public class ShaderToggler : MonoBehaviour
{
    private World world;
    private DayNightCycle dayNight;
    private CloudManager clouds;
    private SunManager sunManager;
    private Material skyMat;
    
    private bool shadersEnabled = false; // Mặc định TẮT shader khi mới vào game

    void Start()
    {
        world = FindFirstObjectByType<World>();
        dayNight = FindFirstObjectByType<DayNightCycle>();
        clouds = FindFirstObjectByType<CloudManager>();
        sunManager = FindFirstObjectByType<SunManager>();
        
        skyMat = RenderSettings.skybox;
        
        // Gọi hàm để áp dụng trạng thái tắt shader ngay khi game bắt đầu
        ApplyShaderState();
    }

    void Update()
    {
        // Nhấn F4 để bật/tắt nhanh
        if (Input.GetKeyDown(KeyCode.F4))
        {
            shadersEnabled = !shadersEnabled;
            ApplyShaderState();
        }
    }

    private void ApplyShaderState()
    {
        // 1. Bật/Tắt hiệu ứng môi trường
        if (dayNight != null) dayNight.enabled = shadersEnabled;
        if (clouds != null) clouds.gameObject.SetActive(shadersEnabled);
        if (sunManager != null) sunManager.gameObject.SetActive(shadersEnabled);
        
        // 2. Chuyển Skybox
        if (shadersEnabled)
        {
            RenderSettings.skybox = skyMat;
            RenderSettings.fog = true;
        }
        else
        {
            RenderSettings.skybox = null; // Trả về skybox mặc định/trống
            RenderSettings.fog = false;
            
            // Đặt ánh sáng cơ bản cho dễ nhìn
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.8f);
            if (dayNight != null && dayNight.sunLight != null)
            {
                dayNight.sunLight.intensity = 1.0f;
                dayNight.sunLight.color = Color.white;
                dayNight.sunLight.shadowStrength = 0.5f;
            }
        }
        
        // 3. Đổi Shader của Vật liệu
        if (world != null)
        {
            if (shadersEnabled)
            {
                if (world.matVertexColor != null) world.matVertexColor.shader = Shader.Find("Custom/VoxelVertexColor");
                if (world.matWater != null) world.matWater.shader = Shader.Find("Custom/VoxelWater");
                Debug.Log("[ShaderToggler] ĐÃ BẬT Cinematic Shaders.");
            }
            else
            {
                // Dùng URP Lit mặc định (Rất nhẹ, hỗ trợ Vertex Color)
                if (world.matVertexColor != null) world.matVertexColor.shader = Shader.Find("Universal Render Pipeline/Lit");
                if (world.matWater != null) 
                {
                    world.matWater.shader = Shader.Find("Universal Render Pipeline/Lit");
                    // Làm nước trong suốt cơ bản
                    world.matWater.SetFloat("_Surface", 1); 
                    world.matWater.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    world.matWater.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    world.matWater.SetInt("_ZWrite", 0);
                    world.matWater.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    world.matWater.color = new Color(0.2f, 0.5f, 1.0f, 0.5f);
                }
                Debug.Log("[ShaderToggler] ĐÃ TẮT Cinematic Shaders (Chế độ Fast Mode).");
            }
        }
    }
}
