using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bootstrap script: tạo World, spawn Player, setup crosshair UI.
/// 
/// Cách dùng:
/// 1. Tạo Empty GameObject → đặt tên "GameManager"
/// 2. Gắn script này vào
/// 3. Kéo material BlockVertexColor vào field "Block Material"
/// 4. Play → tự động tạo World + Player + UI
/// </summary>
public class GameSetup : MonoBehaviour
{
    [Header("Tùy chọn")]
    // Các material giờ đây được gán trực tiếp trong Inspector của World.cs

    private World world;

    private void Start()
    {
        CreateWorld();
        SpawnPlayer();
        SetupEnvironment();
        CreateCrosshairUI();
    }
    
    private void SetupEnvironment()
    {
        // 1. Tạo Gradient Skybox
        Material skyMat = new Material(Shader.Find("Custom/GradientSkybox"));
        RenderSettings.skybox = skyMat;
        
        // 2. Tìm hoặc tạo Mặt trời (Directional Light)
        Light[] lights = FindObjectsOfType<Light>();
        Light sunLight = null;
        foreach (var l in lights) {
            if (l.type == LightType.Directional) {
                sunLight = l; break;
            }
        }
        
        if (sunLight == null)
        {
            GameObject sunObj = new GameObject("Sun");
            sunLight = sunObj.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.shadows = LightShadows.Soft;
            // Áp dụng cấu hình chống "lọt khe" (Peter Panning)
            sunLight.shadowStrength = 1f;
            sunLight.shadowBias = 0.05f;
            
            Material sunMat = new Material(Shader.Find("Custom/SunShader"));
            sunMat.SetColor("_Color", new Color(1f, 0.95f, 0.8f)); // Màu nắng vàng nhạt
        }
        
        // 3. Khởi tạo DayNightCycle
        GameObject envObj = new GameObject("EnvironmentManager");
        DayNightCycle timeCycle = envObj.AddComponent<DayNightCycle>();
        timeCycle.sunLight = sunLight;
        timeCycle.skyMaterial = skyMat;
        
        // Đám mây giống hệt block cỏ (Lit) nhưng Mờ Mờ sương mù (Transparent)
        Material cloudMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        cloudMat.color = new Color(1f, 1f, 1f, 0.5f); // Trắng trong suốt (Alpha = 0.5)
        
        // Bật tính năng trong suốt cho URP Lit
        cloudMat.SetFloat("_Surface", 1); 
        cloudMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        cloudMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        cloudMat.SetInt("_ZWrite", 1); // RẤT QUAN TRỌNG: Giữ ZWrite = 1 để không bị lỗi 2D đè mặt nhau
        cloudMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        
        CloudManager cloudMgr = envObj.AddComponent<CloudManager>();
        
        GameObject sunSystemObj = new GameObject("SunSystem");
        SunManager sunMgr = sunSystemObj.AddComponent<SunManager>();
        
        Transform player = GameObject.Find("Player")?.transform;
        if (player != null)
        {
            sunMgr.Initialize(player, timeCycle, new Material(Shader.Find("Custom/SunShader")));
            cloudMgr.Initialize(player, cloudMat);
        }
        
        Debug.Log("[GameSetup] Environment (DayNight & Clouds) initialized!");
    }

    /// <summary>
    /// Tạo World.
    /// </summary>
    private void CreateWorld()
    {
        // Tìm World có sẵn trong Scene (do user đã gán material qua Inspector của nó)
        world = FindObjectOfType<World>();
        
        if (world == null)
        {
            Debug.LogError("[GameSetup] Không tìm thấy World trong Scene! Vui lòng tạo một GameObject chứa script World và gán các Material.");
            return;
        }

        world.Initialize();

        Debug.Log("[GameSetup] World initialized!");
    }

    /// <summary>
    /// Spawn player trên bề mặt địa hình.
    /// 
    /// Quy trình:
    /// 1. Tính spawn position = giữa chunk, trên bề mặt + offset
    /// 2. Tạo Player GameObject (empty, không cần visual)
    /// 3. Gắn CharacterController + PlayerController + PlayerInteraction
    /// 4. Chuyển Main Camera thành con của Player (FPS view)
    /// </summary>
    private void SpawnPlayer()
    {
        // Tính vị trí spawn: giữa chunk, trên bề mặt
        int spawnX = Chunk.ChunkWidth / 2;
        int spawnZ = Chunk.ChunkWidth / 2;
        int surfaceY = world.GetTerrainGenerator().GetHeight(spawnX, spawnZ);

        // Spawn trên bề mặt + 2 block (đầu player cao khoảng 1.8 unit)
        Vector3 spawnPos = new Vector3(spawnX + 0.5f, surfaceY + 2f, spawnZ + 0.5f);

        // Tạo Player GameObject
        GameObject player = new GameObject("Player");
        player.transform.position = spawnPos;

        // Gắn CharacterController
        // Height = 1.8 (chiều cao player), Radius = 0.3 (nhỏ hơn 1 block)
        // Center.y = 0 (player pivot ở giữa thân)
        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0f, 0f);

        // Gắn PlayerController (FPS movement)
        player.AddComponent<PlayerController>();

        // Gắn PlayerInteraction (đào/đặt block)
        PlayerInteraction interaction = player.AddComponent<PlayerInteraction>();
        interaction.SetWorld(world);

        // Truyền player reference cho World để quản lý chunk động
        world.player = player.transform;

        // Chuyển Main Camera thành con của Player
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.parent = player.transform;
            // Camera ở vị trí "mắt" player (gần đỉnh đầu)
            cam.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            cam.transform.localRotation = Quaternion.identity;

            cam.farClipPlane = 1000f;
        }

        Debug.Log($"[GameSetup] Player spawned at {spawnPos} (surface Y: {surfaceY})");
    }

    /// <summary>
    /// Tạo crosshair UI (dấu + giữa màn hình).
    /// 
    /// Cấu trúc UI:
    /// Canvas (Screen Space - Overlay)
    ///   └── CrosshairText (Text "+" ở giữa)
    /// </summary>
    private void CreateCrosshairUI()
    {
        // Tạo Canvas
        GameObject canvasObj = new GameObject("CrosshairCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;  // Luôn ở trên cùng
        canvasObj.AddComponent<CanvasScaler>();

        // Tạo crosshair text "+"
        GameObject crosshairObj = new GameObject("Crosshair");
        crosshairObj.transform.SetParent(canvasObj.transform, false);

        Text crosshairText = crosshairObj.AddComponent<Text>();
        crosshairText.text = "+";
        crosshairText.font = Font.CreateDynamicFontFromOSFont("Arial", 24);
        crosshairText.fontSize = 24;
        crosshairText.color = Color.white;
        crosshairText.alignment = TextAnchor.MiddleCenter;

        // Outline cho crosshair (dễ nhìn trên mọi background)
        Outline outline = crosshairObj.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1, -1);

        // Đặt ở giữa màn hình
        RectTransform rt = crosshairObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(40, 40);
    }
}
