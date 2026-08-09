using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class GrassBladeManager : MonoBehaviour
{
    public Material grassBladeMaterial;
    public World world;
    public Transform player;

    // Cache Cỏ theo Chunk (Tối ưu Rendering)
    public class GrassLODData
    {
        public List<Matrix4x4> full;
        public List<Matrix4x4> lod1;
        public List<Matrix4x4> lod2;
    }
    private Dictionary<Vector3Int, GrassLODData> chunkGrassCache = new Dictionary<Vector3Int, GrassLODData>();
    private Mesh bladeMesh;
    
    // === LOD: 2 Material — gần có bóng, xa không bóng ===
    private Material grassMaterialNoShadow;
    
    // Khoảng cách LOD (tính bằng chunk)
    private const int LOD0_DISTANCE = 4;   // 100% mật độ + Bóng đổ
    private const int LOD1_DISTANCE = 8;   // 33% mật độ + Không bóng
    private const int LOD2_DISTANCE = 12;  // 10% mật độ + Không bóng (rất thưa thớt)

    private Vector3Int lastCheckedChunk = new Vector3Int(int.MinValue, 0, 0);

    void Start()
    {
        bladeMesh = CreateCrossQuadMesh(0.8f, 0.8f); 
    }

    void Update()
    {
        if (world == null || grassBladeMaterial == null || bladeMesh == null) return;
        
        // Tạo material không bóng nếu chưa có (lazy init)
        if (grassMaterialNoShadow == null)
        {
            grassMaterialNoShadow = new Material(grassBladeMaterial);
            grassMaterialNoShadow.enableInstancing = true;
            // Tắt keyword bóng để GPU không phải tính shadow map cho cỏ xa
            grassMaterialNoShadow.DisableKeyword("_MAIN_LIGHT_SHADOWS");
            grassMaterialNoShadow.DisableKeyword("_MAIN_LIGHT_SHADOWS_CASCADE");
            grassMaterialNoShadow.DisableKeyword("_SHADOWS_SOFT");
        }

        Vector3Int currentChunk = new Vector3Int(
            Mathf.FloorToInt(player.position.x / Chunk.ChunkWidth), 0,
            Mathf.FloorToInt(player.position.z / Chunk.ChunkWidth)
        );
        
        if (currentChunk != lastCheckedChunk)
        {
            lastCheckedChunk = currentChunk;
            UpdateGrassCache();
        }
        RenderInstanced();
    }

    void UpdateGrassCache()
    {
        // Lấy danh sách chunk đã load xong mặt đất từ World
        var loadedChunks = world.GetLoadedChunkCoords().ToList();
        
        // 1. Unload chunk không còn tồn tại trong World (gom rác)
        var chunksToRemove = chunkGrassCache.Keys.Where(c => !loadedChunks.Contains(c)).ToList();
        foreach (var c in chunksToRemove)
        {
            chunkGrassCache.Remove(c);
        }

        // 2. Sinh cỏ cho chunk mới xuất hiện
        foreach (var chunkCoord in loadedChunks)
        {
            if (!chunkGrassCache.ContainsKey(chunkCoord))
            {
                GenerateGrassForChunk(chunkCoord);
            }
        }
    }

    void GenerateGrassForChunk(Vector3Int chunkCoord)
    {
        List<Matrix4x4> matrices = new List<Matrix4x4>();
        
        Vector3Int chunkWorldPos = new Vector3Int(
            chunkCoord.x * Chunk.ChunkWidth,
            0,
            chunkCoord.z * Chunk.ChunkWidth
        );

        // Duyệt từng cột x,z trong không gian của Chunk đó
        for (int x = 0; x < Chunk.ChunkWidth; x++)
        {
            for (int z = 0; z < Chunk.ChunkWidth; z++)
            {
                int worldX = chunkWorldPos.x + x;
                int worldZ = chunkWorldPos.z + z;
                
                // --- PHÂN BỐ TỰ NHIÊN ---
                float noise = Mathf.PerlinNoise(worldX * 0.15f, worldZ * 0.15f);
                if (noise < 0.45f) continue; 
                
                // Mật độ: 5 đến 15 cụm trong 1 block
                int bladesCount = Mathf.FloorToInt((noise - 0.45f) * 15f) + 5; 

                // --- GIỚI HẠN ĐỘ CAO ---
                int approxSurface = world.GetTerrainGenerator().GetHeight(worldX, worldZ);
                if (approxSurface > 80) continue;
                
                // Dò block bề mặt
                for (int y = approxSurface + 5; y >= approxSurface - 5; y--)
                {
                    Vector3Int pos = new Vector3Int(worldX, y, worldZ);
                    if (world.GetBlock(pos) == BlockType.Grass && world.GetBlock(pos + Vector3Int.up) == BlockType.Air)
                    {
                        for (int i = 0; i < bladesCount; i++)
                        {
                            Vector3 offset = new Vector3(Random.Range(0.1f, 0.9f), 0f, Random.Range(0.1f, 0.9f));
                            Vector3 bladePos = (Vector3)pos + Vector3.up + offset;
                            
                            Quaternion rot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                            
                            // Scale Y: 15% xác suất mọc cỏ cao hơn
                            float randomScaleY;
                            if (Random.value > 0.85f) {
                                randomScaleY = Random.Range(1.1f, 1.4f);
                            } else {
                                randomScaleY = Random.Range(0.6f, 0.9f);
                            }
                            Vector3 scale = new Vector3(1f, randomScaleY, 1f);
                            
                            matrices.Add(Matrix4x4.TRS(bladePos, rot, scale));
                        }
                        break; 
                    }
                }
            }
        }
        
        chunkGrassCache[chunkCoord] = new GrassLODData
        {
            full = matrices,
            lod1 = matrices.Where((m, i) => i % 3 == 0).ToList(),
            lod2 = matrices.Where((m, i) => i % 10 == 0).ToList()
        };
    }

    void RenderInstanced()
    {
        if (player == null || Camera.main == null) return;
        
        Vector3 playerPos = player.position;
        int playerChunkX = Mathf.FloorToInt(playerPos.x / Chunk.ChunkWidth);
        int playerChunkZ = Mathf.FloorToInt(playerPos.z / Chunk.ChunkWidth);
        
        // --- 1. TÍNH TOÁN VÙNG NHÌN THẤY (FRUSTUM CULLING) ---
        // Lấy 6 mặt phẳng bao quanh góc nhìn của Camera
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);
        
        // Chia render thành 2 nhóm: GẦN (có bóng) và XA (không bóng)
        foreach (var kvp in chunkGrassCache)
        {
            if (kvp.Value.full.Count == 0) continue;
            
            Vector3Int chunkCoord = kvp.Key;
            
            // --- 2. CULLING THEO KHOẢNG CÁCH (Khoảng cách Chunk) ---
            int distX = Mathf.Abs(chunkCoord.x - playerChunkX);
            int distZ = Mathf.Abs(chunkCoord.z - playerChunkZ);
            int chunkDist = Mathf.Max(distX, distZ);
            
            // Chunk quá xa → bỏ qua luôn, không render
            if (chunkDist > LOD2_DISTANCE) continue;

            // --- 3. CULLING THEO CAMERA (FRUSTUM CULLING) ---
            Vector3 chunkCenter = new Vector3(chunkCoord.x * Chunk.ChunkWidth + Chunk.ChunkWidth / 2f, 50f, chunkCoord.z * Chunk.ChunkWidth + Chunk.ChunkWidth / 2f);
            Vector3 chunkSize = new Vector3(Chunk.ChunkWidth, 100f, Chunk.ChunkWidth);
            Bounds chunkBounds = new Bounds(chunkCenter, chunkSize);
            
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, chunkBounds))
            {
                continue; 
            }
            
            // --- 4. ÁP DỤNG LOD (Mật độ giảm dần) ---
            Material mat;
            List<Matrix4x4> dataToRender;
            
            if (chunkDist <= LOD0_DISTANCE)
            {
                // LOD 0: Render 100% với bóng
                mat = grassBladeMaterial;
                dataToRender = kvp.Value.full;
            }
            else if (chunkDist <= LOD1_DISTANCE)
            {
                // LOD 1: Render 33% (bỏ qua 2, lấy 1) không bóng
                mat = grassMaterialNoShadow;
                dataToRender = kvp.Value.lod1;
            }
            else
            {
                // LOD 2: Render cực ít 10% (bỏ qua 9, lấy 1) không bóng
                mat = grassMaterialNoShadow;
                dataToRender = kvp.Value.lod2;
            }
            
            // DrawMeshInstanced giới hạn 1023 instance/lần gọi
            for (int i = 0; i < dataToRender.Count; i += 1023)
            {
                int count = Mathf.Min(1023, dataToRender.Count - i);
                Graphics.DrawMeshInstanced(bladeMesh, 0, mat, dataToRender.GetRange(i, count));
            }
        }
    }

    Mesh CreateCrossQuadMesh(float width, float height)
    {
        Mesh mesh = new Mesh();
        float hw = width / 2f;

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-hw, 0, 0), new Vector3(hw, 0, 0), new Vector3(hw, height, 0), new Vector3(-hw, height, 0),
            new Vector3(0, 0, -hw), new Vector3(0, 0, hw), new Vector3(0, height, hw), new Vector3(0, height, -hw),
        };

        Vector2[] uvs = new Vector2[]
        {
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
        };

        // Dùng Vertex Color như một dạng Fake Ambient Occlusion (Gốc tối, ngọn sáng)
        // Thay vì dùng màu xanh (sẽ làm lệch màu gốc của Texture), ta dùng màu xám.
        Color bottomColor = new Color(0.5f, 0.5f, 0.5f, 0f);   // Tối đi 50% ở gốc
        Color topColor = new Color(0.9f, 0.9f, 0.9f, 1f);      // Gần như giữ nguyên màu gốc ở ngọn

        Color[] colors = new Color[]
        {
            bottomColor, bottomColor, topColor, topColor,
            bottomColor, bottomColor, topColor, topColor
        };

        int[] triangles = new int[]
        {
            0,2,1, 0,3,2,  0,1,2, 0,2,3,  
            4,6,5, 4,7,6,  4,5,6, 4,6,7   
        };

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
