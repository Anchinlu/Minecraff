using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

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
    private Transform playerTransform;

    private void Start()
    {
        CreateWorld();
        SpawnPlayer();
        SetupEnvironment();
        CreateCrosshairUI();
        
        // Công cụ Tắt/Bật nhanh Shader
        gameObject.AddComponent<ShaderToggler>();
    }
    
    private void Update()
    {
        if (playerTransform != null)
        {
            // Truyền tọa độ Player xuống toàn bộ Shaders (để làm Cỏ tương tác)
            Shader.SetGlobalVector("_PlayerPos", playerTransform.position);
        }
    }
    
    private void SetupEnvironment()
    {
        // 1. Tạo Gradient Skybox
        Material skyMat = new Material(Shader.Find("Custom/GradientSkybox"));
        RenderSettings.skybox = skyMat;
        
        // 2. Tìm hoặc tạo Mặt trời (Directional Light)
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
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
        
        // Đám mây dùng Shader tùy chỉnh (Opaque, có Directional Shading + Cinematic Fog)
        Material cloudMat = new Material(Shader.Find("Custom/CloudShader"));
        
        CloudManager cloudMgr = envObj.AddComponent<CloudManager>();
        GrassBladeManager grassMgr = envObj.AddComponent<GrassBladeManager>();
        
        GameObject sunSystemObj = new GameObject("SunSystem");
        SunManager sunMgr = sunSystemObj.AddComponent<SunManager>();
        
        Transform player = GameObject.Find("Player")?.transform;
        if (player != null)
        {
            sunMgr.Initialize(player, timeCycle, new Material(Shader.Find("Custom/SunShader")));
            cloudMgr.Initialize(player, cloudMat);
            
            Material grassBladeMat = new Material(Shader.Find("Custom/GrassBlade"));
            grassBladeMat.enableInstancing = true; // Bắt buộc cho GPU Instancing
            
            Texture2D grassAtlas = GetOrCreatePixelGrass();
            grassBladeMat.mainTexture = grassAtlas;
            
            grassMgr.player = player;
            grassMgr.world = world;
            grassMgr.grassBladeMaterial = grassBladeMat;
        }
        
        Debug.Log("[GameSetup] Environment (DayNight & Clouds) initialized!");
    }

    /// <summary>
    /// Tạo World.
    /// </summary>
    private void CreateWorld()
    {
        // Tìm World có sẵn trong Scene (do user đã gán material qua Inspector của nó)
        world = FindFirstObjectByType<World>();
        
        if (world == null)
        {
            Debug.LogError("[GameSetup] Không tìm thấy World trong Scene! Vui lòng tạo một GameObject chứa script World và gán các Material.");
            return;
        }

        // Tự động gán Material Nước nếu chưa có (Hỗ trợ Phase 1 Water)
        if (world.matWater == null)
        {
            world.matWater = new Material(Shader.Find("Custom/VoxelWater"));
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
        playerTransform = player.transform;

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

            cam.farClipPlane = 2000f; // Tăng lên để nhìn thấy mặt trời ở khoảng cách 1500

            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            if (camData != null)
            {
                camData.requiresDepthOption = CameraOverrideOption.On;
            }
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

    private Texture2D GetOrCreatePixelGrass()
    {
        // Thử load từ Resources nếu user đã vẽ
        Texture2D tex = Resources.Load<Texture2D>("Grass/pixel_grass");
        if (tex != null) {
            tex.filterMode = FilterMode.Point;
            return tex;
        }
        
        // Tạo procedural Pixel Art 16x16 nếu chưa có
        tex = new Texture2D(16, 16);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        
        Color clear = new Color(0,0,0,0);
        Color c1 = new Color(0.35f, 0.55f, 0.2f, 1f); // Xanh olive (giống màu cỏ đất)
        Color c2 = new Color(0.25f, 0.4f, 0.15f, 1f); // Xanh tối hơn ở vùng khuất
        
        for(int i=0; i<256; i++) tex.SetPixel(i%16, i/16, clear);
        
        // Mảng tọa độ các điểm ảnh tạo nên hình dạng 3 cụm cỏ (Pixel Art thuần)
        int[,] grassPixels = new int[,] {
            // Cỏ bên trái
            {2,0},{3,0},{2,1},{3,1},{2,2},{3,2},{3,3},{4,3},{3,4},{4,4},{4,5},{5,5},{5,6},{4,6},{4,7},
            // Cỏ bên phải
            {12,0},{13,0},{12,1},{13,1},{12,2},{11,2},{12,3},{11,3},{11,4},{10,4},{11,5},{10,5},{10,6},
            // Cỏ giữa cao
            {7,0},{8,0},{7,1},{8,1},{7,2},{8,2},{7,3},{8,3},{7,4},{8,4},{7,5},{8,5},{8,6},{9,6},{8,7},{9,7},{9,8},{9,9},{8,9},{9,10}
        };
        
        for(int i = 0; i < grassPixels.GetLength(0); i++) {
            int x = grassPixels[i,0];
            int y = grassPixels[i,1];
            tex.SetPixel(x, y, (i%2==0) ? c1 : c2);
        }
        
        tex.Apply();
        return tex;
    }
}
