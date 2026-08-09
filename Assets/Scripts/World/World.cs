using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// World quản lý tất cả các chunk trong thế giới.
/// 
/// === HỆ THỐNG TẦM NHÌN 3 VÙNG ===
/// 1. renderDistance (gần): Chunk đầy đủ mesh + collider + cỏ + bóng
/// 2. preloadDistance (xa hơn): Chunk được pre-generate sẵn data (block + light) nhưng CHƯA tạo mesh
///    → Khi người chơi tiến đến, mesh được dựng ngay lập tức, không cần chờ sinh địa hình
/// 3. Sương mù (Fog) che phủ vùng giữa renderDistance và chân trời
///    → Người chơi không bao giờ thấy chunk "pop in" đột ngột
/// </summary>
public class World : MonoBehaviour
{
    [Header("Tầm nhìn")]
    public int viewDistance = 16;      // Đẩy lên mức "Ultra": 35 chunk (560 blocks) -> Tối ưu: 16
    public int preloadDistance = 19;   // Preload trước 38 chunk (608 blocks) -> Tối ưu: 19

    public Transform player;

    public Material matVertexColor;
    public Material matGrassTexture;
    public Material matWater;

    private TerrainGenerator terrainGenerator;
    
    // Hệ thống nước động
    private WaterSimulator waterSimulator;

    // Dictionary lưu chunk data đang load
    private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
    
    // Lưu chunk GameObject để destroy khi unload
    private Dictionary<Vector3Int, GameObject> chunkObjects = new Dictionary<Vector3Int, GameObject>();
    
    // Pre-loaded chunk data (chỉ có block data, chưa có mesh)
    private Dictionary<Vector3Int, Chunk> preloadedChunks = new Dictionary<Vector3Int, Chunk>();

    private Vector3Int currentPlayerChunkCoord;
    private bool initialized = false;

    // Hàng đợi tạo chunk dần dần (chuyển sang HashSet để tìm kiếm động)
    private HashSet<Vector3Int> pendingChunks = new HashSet<Vector3Int>();
    private HashSet<Vector3Int> pendingPreloads = new HashSet<Vector3Int>();
    
    [Header("Tối ưu hóa")]
    public int maxChunksPerFrame = 2;
    public int maxPreloadsPerFrame = 3;
    public int colliderRadius = 6; // Chỉ tạo MeshCollider cho chunk trong phạm vi này

    public void SetMaterials(Material vertexColorMat, Material grassTextureMat, Material waterMat)
    {
        matVertexColor = vertexColorMat;
        matGrassTexture = grassTextureMat;
        matWater = waterMat;
    }

    public TerrainGenerator GetTerrainGenerator()
    {
        if (terrainGenerator == null) 
        {
            terrainGenerator = new TerrainGenerator();
            Debug.Log($"[World] TerrainGenerator (Lazy Init) — seed: {terrainGenerator.seed:F1}");
        }
        return terrainGenerator;
    }

    public void Initialize()
    {
        terrainGenerator = new TerrainGenerator();
        Debug.Log($"[World] TerrainGenerator — seed: {terrainGenerator.seed:F1}");

        // Khởi tạo WaterSimulator
        waterSimulator = gameObject.AddComponent<WaterSimulator>();
        waterSimulator.Initialize(this);

        // Cập nhật chunk lần đầu
        currentPlayerChunkCoord = new Vector3Int(0, 0, 0);
        UpdateVisibleChunks();
        
        // Render NGAY LẬP TỨC vùng không gian nhỏ dưới chân để có chỗ đứng (bán kính 2 chunk)
        List<Vector3Int> spawnChunks = new List<Vector3Int>();
        foreach (var coord in pendingChunks)
        {
            float dist = Mathf.Max(Mathf.Abs(coord.x), Mathf.Abs(coord.z));
            if (dist <= 2) spawnChunks.Add(coord);
        }
        foreach (var coord in spawnChunks)
        {
            pendingChunks.Remove(coord);
            if (!chunks.ContainsKey(coord)) CreateChunk(coord, true);
        }
        
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || player == null) return;

        // Tính tọa độ chunk hiện tại của player
        Vector3Int currentCoord = WorldToChunkCoord(Vector3Int.FloorToInt(player.position));

        // Nếu player đi sang chunk mới → update
        if (currentCoord != currentPlayerChunkCoord)
        {
            currentPlayerChunkCoord = currentCoord;
            UpdateVisibleChunks();
        }

        // === Xử lý hàng đợi RENDER (chunk gần - cần mesh) ===
        for (int i = 0; i < maxChunksPerFrame && pendingChunks.Count > 0; i++)
        {
            bool found;
            Vector3Int nearest = GetNearestChunk(pendingChunks, viewDistance, out found);
            
            if (found && !chunks.ContainsKey(nearest))
            {
                bool needsCollider = Vector3Int.Distance(nearest, currentPlayerChunkCoord) <= colliderRadius;
                
                if (preloadedChunks.ContainsKey(nearest))
                    PromotePreloadedChunk(nearest, needsCollider);
                else
                    CreateChunk(nearest, needsCollider);
            }
        }
        
        // === Xử lý hàng đợi PRE-LOAD (chunk xa - chỉ sinh data) ===
        for (int i = 0; i < maxPreloadsPerFrame && pendingPreloads.Count > 0; i++)
        {
            bool found;
            Vector3Int nearest = GetNearestChunk(pendingPreloads, preloadDistance, out found);
            
            if (found && !chunks.ContainsKey(nearest) && !preloadedChunks.ContainsKey(nearest))
            {
                PreloadChunkData(nearest);
            }
        }
    }

    /// <summary>
    /// Tìm chunk gần player nhất để ưu tiên render. Lọc bỏ các chunk đã văng khỏi tầm nhìn.
    /// </summary>
    private Vector3Int GetNearestChunk(HashSet<Vector3Int> pendingList, int maxGridDist, out bool found)
    {
        Vector3Int nearest = default;
        float minDist = float.MaxValue;
        found = false;
        
        List<Vector3Int> toRemove = new List<Vector3Int>();

        foreach (var coord in pendingList)
        {
            // Khoảng cách Chebyshev để check maxGridDist (khớp với bounding box của UpdateVisibleChunks)
            int gridDist = Mathf.Max(Mathf.Abs(coord.x - currentPlayerChunkCoord.x), Mathf.Abs(coord.z - currentPlayerChunkCoord.z));
            if (gridDist > maxGridDist)
            {
                toRemove.Add(coord);
                continue;
            }

            // Khoảng cách Euclidean để tạo hiệu ứng tải theo hình tròn mượt mà
            float dist = Vector2.Distance(new Vector2(coord.x, coord.z), new Vector2(currentPlayerChunkCoord.x, currentPlayerChunkCoord.z));
            if (dist < minDist)
            {
                minDist = dist;
                nearest = coord;
                found = true;
            }
        }

        foreach (var c in toRemove) pendingList.Remove(c);
        if (found) pendingList.Remove(nearest);

        return nearest;
    }

    /// <summary>
    /// Cập nhật cả 2 vùng: Render + Pre-load.
    /// </summary>
    private void UpdateVisibleChunks()
    {
        HashSet<Vector3Int> chunksToKeepRender = new HashSet<Vector3Int>();
        HashSet<Vector3Int> chunksToKeepPreload = new HashSet<Vector3Int>();

        // Duyệt lưới vuông trong phạm vi preloadDistance (bao trùm cả viewDistance)
        for (int x = -preloadDistance; x <= preloadDistance; x++)
        {
            for (int z = -preloadDistance; z <= preloadDistance; z++)
            {
                Vector3Int coord = new Vector3Int(currentPlayerChunkCoord.x + x, 0, currentPlayerChunkCoord.z + z);
                
                int dist = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                
                if (dist <= viewDistance)
                {
                    // Vùng RENDER: cần mesh đầy đủ
                    chunksToKeepRender.Add(coord);
                    if (!chunks.ContainsKey(coord) && !pendingChunks.Contains(coord))
                    {
                        pendingChunks.Add(coord);
                    }
                    
                    // Nạp thêm collider nếu chunk này tiến vào vùng colliderRadius nhưng chưa có
                    if (dist <= colliderRadius && chunks.ContainsKey(coord))
                    {
                        chunks[coord].EnsureColliderAdded();
                    }
                }
                else
                {
                    // Vùng PRE-LOAD: chỉ cần block data
                    chunksToKeepPreload.Add(coord);
                    if (!preloadedChunks.ContainsKey(coord) && !chunks.ContainsKey(coord) && !pendingPreloads.Contains(coord))
                    {
                        pendingPreloads.Add(coord);
                    }
                }
            }
        }

        // Unload chunk render nằm ngoài tầm render
        List<Vector3Int> chunksToRemove = new List<Vector3Int>();
        foreach (var coord in chunks.Keys)
        {
            if (!chunksToKeepRender.Contains(coord))
            {
                chunksToRemove.Add(coord);
            }
        }
        foreach (var coord in chunksToRemove)
        {
            Destroy(chunkObjects[coord]);
            chunkObjects.Remove(coord);
            chunks.Remove(coord);
        }
        
        // Unload pre-loaded chunk nằm ngoài tầm preload
        List<Vector3Int> preloadsToRemove = new List<Vector3Int>();
        foreach (var coord in preloadedChunks.Keys)
        {
            if (!chunksToKeepPreload.Contains(coord) && !chunksToKeepRender.Contains(coord))
            {
                preloadsToRemove.Add(coord);
            }
        }
        foreach (var coord in preloadsToRemove)
        {
            preloadedChunks.Remove(coord);
        }
    }

    /// <summary>
    /// Pre-load: Chỉ sinh block data + sunlight, KHÔNG tạo mesh/GameObject.
    /// Rất nhẹ vì không tốn GPU.
    /// </summary>
    private void PreloadChunkData(Vector3Int chunkCoord)
    {
        Vector3Int chunkWorldPos = new Vector3Int(
            chunkCoord.x * Chunk.ChunkWidth, 0, chunkCoord.z * Chunk.ChunkWidth
        );

        Chunk chunk = new Chunk();
        chunk.Init(matVertexColor, matGrassTexture, matWater, chunkWorldPos);
        chunk.PopulateBlocks(this, GetTerrainGenerator(), chunkWorldPos);
        // Không gọi RebuildMesh() — đây là điểm mấu chốt tiết kiệm hiệu năng
        
        preloadedChunks[chunkCoord] = chunk;
    }
    
    /// <summary>
    /// Chuyển chunk từ pre-loaded → full render.
    /// Data đã sẵn sàng nên chỉ cần tạo mesh — nhanh gấp đôi so với CreateChunk().
    /// </summary>
    private void PromotePreloadedChunk(Vector3Int chunkCoord, bool needsCollider)
    {
        Chunk chunk = preloadedChunks[chunkCoord];
        preloadedChunks.Remove(chunkCoord);
        
        Vector3Int chunkWorldPos = new Vector3Int(
            chunkCoord.x * Chunk.ChunkWidth, 0, chunkCoord.z * Chunk.ChunkWidth
        );

        GameObject chunkObject = new GameObject($"Chunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}");
        chunkObject.transform.parent = transform;
        chunkObject.transform.position = (Vector3)chunkWorldPos;

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = needsCollider ? chunkObject.AddComponent<MeshCollider>() : null;

        chunk.SetMeshComponents(meshFilter, meshCollider, meshRenderer);

        chunks[chunkCoord] = chunk;
        chunkObjects[chunkCoord] = chunkObject;

        // Chỉ cần build mesh (block data đã có sẵn từ pre-load!)
        chunk.RebuildMesh();
        UpdateNeighborMeshes(chunkCoord);
    }

    private void CreateChunk(Vector3Int chunkCoord, bool needsCollider)
    {
        Vector3Int chunkWorldPos = new Vector3Int(
            chunkCoord.x * Chunk.ChunkWidth,
            0,
            chunkCoord.z * Chunk.ChunkWidth
        );

        Chunk chunk = new Chunk();
        chunk.Init(matVertexColor, matGrassTexture, matWater, chunkWorldPos);
        chunk.PopulateBlocks(this, GetTerrainGenerator(), chunkWorldPos);

        GameObject chunkObject = new GameObject($"Chunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}");
        chunkObject.transform.parent = transform;
        chunkObject.transform.position = (Vector3)chunkWorldPos;

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = needsCollider ? chunkObject.AddComponent<MeshCollider>() : null;

        chunk.SetMeshComponents(meshFilter, meshCollider, meshRenderer);

        chunks[chunkCoord] = chunk;
        chunkObjects[chunkCoord] = chunkObject;

        chunk.RebuildMesh();
        UpdateNeighborMeshes(chunkCoord);
    }

    private void UpdateNeighborMeshes(Vector3Int chunkCoord)
    {
        Vector3Int[] neighbors = {
            chunkCoord + Vector3Int.right,
            chunkCoord + Vector3Int.left,
            chunkCoord + new Vector3Int(0, 0, 1),
            chunkCoord + new Vector3Int(0, 0, -1)
        };

        foreach (var n in neighbors)
        {
            if (chunks.ContainsKey(n))
            {
                chunks[n].RebuildMesh();
            }
        }
    }

    // --- Block Set/Get Methods ---

    public void SetBlock(Vector3Int worldPos, BlockType blockType)
    {
        Vector3Int chunkCoord = WorldToChunkCoord(worldPos);

        if (!chunks.ContainsKey(chunkCoord)) return;

        Chunk chunk = chunks[chunkCoord];
        Vector3Int localPos = WorldToLocalPos(worldPos, chunkCoord);

        if (chunk.SetBlock(localPos.x, localPos.y, localPos.z, blockType))
        {
            chunk.CalculateSunlight(localPos);
            chunk.RebuildMesh();
            
            if (localPos.x == 0) UpdateNeighborMeshIfLoaded(chunkCoord + Vector3Int.left, new Vector3Int(Chunk.ChunkWidth - 1, localPos.y, localPos.z));
            else if (localPos.x == Chunk.ChunkWidth - 1) UpdateNeighborMeshIfLoaded(chunkCoord + Vector3Int.right, new Vector3Int(0, localPos.y, localPos.z));
            
            if (localPos.z == 0) UpdateNeighborMeshIfLoaded(chunkCoord + new Vector3Int(0, 0, -1), new Vector3Int(localPos.x, localPos.y, Chunk.ChunkWidth - 1));
            else if (localPos.z == Chunk.ChunkWidth - 1) UpdateNeighborMeshIfLoaded(chunkCoord + new Vector3Int(0, 0, 1), new Vector3Int(localPos.x, localPos.y, 0));
            
            NotifyBlockUpdate(worldPos);
            NotifyNeighborsUpdate(worldPos);
        }
    }
    
    public void SetBlockAndWater(Vector3Int worldPos, BlockType blockType, byte waterLvl = 0)
    {
        Vector3Int chunkCoord = WorldToChunkCoord(worldPos);

        if (!chunks.ContainsKey(chunkCoord)) return;

        Chunk chunk = chunks[chunkCoord];
        Vector3Int localPos = WorldToLocalPos(worldPos, chunkCoord);

        bool changed = chunk.SetBlock(localPos.x, localPos.y, localPos.z, blockType);
        
        if (blockType == BlockType.Water) {
            changed |= chunk.SetWaterLevel(localPos.x, localPos.y, localPos.z, waterLvl);
        }

        if (changed)
        {
            chunk.CalculateSunlight(localPos);
            chunk.RebuildMesh();
            
            if (localPos.x == 0) UpdateNeighborMeshIfLoaded(chunkCoord + Vector3Int.left, new Vector3Int(Chunk.ChunkWidth - 1, localPos.y, localPos.z));
            else if (localPos.x == Chunk.ChunkWidth - 1) UpdateNeighborMeshIfLoaded(chunkCoord + Vector3Int.right, new Vector3Int(0, localPos.y, localPos.z));
            
            if (localPos.z == 0) UpdateNeighborMeshIfLoaded(chunkCoord + new Vector3Int(0, 0, -1), new Vector3Int(localPos.x, localPos.y, Chunk.ChunkWidth - 1));
            else if (localPos.z == Chunk.ChunkWidth - 1) UpdateNeighborMeshIfLoaded(chunkCoord + new Vector3Int(0, 0, 1), new Vector3Int(localPos.x, localPos.y, 0));
            
            NotifyBlockUpdate(worldPos);
            NotifyNeighborsUpdate(worldPos);
        }
    }
    
    public void NotifyBlockUpdate(Vector3Int pos)
    {
        if (waterSimulator != null && GetBlock(pos) == BlockType.Water) 
        {
            waterSimulator.EnqueueWaterUpdate(pos);
        }
    }
    
    public void NotifyNeighborsUpdate(Vector3Int pos)
    {
        Vector3Int[] neighbors = {
            pos + Vector3Int.left,
            pos + Vector3Int.right,
            pos + new Vector3Int(0, 1, 0),
            pos + new Vector3Int(0, -1, 0),
            pos + new Vector3Int(0, 0, 1),
            pos + new Vector3Int(0, 0, -1)
        };
        foreach(var n in neighbors) {
            NotifyBlockUpdate(n);
        }
    }
    
    private void UpdateNeighborMeshIfLoaded(Vector3Int neighborCoord, Vector3Int? boundaryLocalPos = null)
    {
        if (chunks.ContainsKey(neighborCoord))
        {
            chunks[neighborCoord].CalculateSunlight(boundaryLocalPos);
            chunks[neighborCoord].RebuildMesh();
        }
    }

    public BlockType GetBlock(Vector3Int worldPos)
    {
        Vector3Int chunkCoord = WorldToChunkCoord(worldPos);
        if (!chunks.ContainsKey(chunkCoord))
        {
            // Thử tìm trong pre-loaded chunks (hỗ trợ cross-chunk query)
            if (preloadedChunks.ContainsKey(chunkCoord))
            {
                Vector3Int localPos = WorldToLocalPos(worldPos, chunkCoord);
                return preloadedChunks[chunkCoord].GetBlockAt(localPos.x, localPos.y, localPos.z);
            }
            return BlockType.Air;
        }

        Vector3Int lp = WorldToLocalPos(worldPos, chunkCoord);
        return chunks[chunkCoord].GetBlockAt(lp.x, lp.y, lp.z);
    }

    public byte GetLight(Vector3Int worldPos)
    {
        Vector3Int chunkCoord = WorldToChunkCoord(worldPos);
        if (!chunks.ContainsKey(chunkCoord)) return 15;

        Vector3Int localPos = WorldToLocalPos(worldPos, chunkCoord);
        return chunks[chunkCoord].GetLightAt(localPos.x, localPos.y, localPos.z);
    }
    
    public byte GetWaterLevel(Vector3Int worldPos)
    {
        Vector3Int chunkCoord = WorldToChunkCoord(worldPos);
        if (!chunks.ContainsKey(chunkCoord))
        {
            if (preloadedChunks.ContainsKey(chunkCoord))
            {
                Vector3Int localPos = WorldToLocalPos(worldPos, chunkCoord);
                return preloadedChunks[chunkCoord].GetWaterLevelAt(localPos.x, localPos.y, localPos.z);
            }
            return 0;
        }

        Vector3Int lp = WorldToLocalPos(worldPos, chunkCoord);
        return chunks[chunkCoord].GetWaterLevelAt(lp.x, lp.y, lp.z);
    }
    
    public IEnumerable<Vector3Int> GetLoadedChunkCoords()
    {
        return chunks.Keys;
    }

    private Vector3Int WorldToChunkCoord(Vector3Int worldPos)
    {
        return new Vector3Int(
            Mathf.FloorToInt((float)worldPos.x / Chunk.ChunkWidth),
            0,
            Mathf.FloorToInt((float)worldPos.z / Chunk.ChunkWidth)
        );
    }

    private Vector3Int WorldToLocalPos(Vector3Int worldPos, Vector3Int chunkCoord)
    {
        return new Vector3Int(
            ((worldPos.x - chunkCoord.x * Chunk.ChunkWidth) % Chunk.ChunkWidth + Chunk.ChunkWidth) % Chunk.ChunkWidth,
            worldPos.y,
            ((worldPos.z - chunkCoord.z * Chunk.ChunkWidth) % Chunk.ChunkWidth + Chunk.ChunkWidth) % Chunk.ChunkWidth
        );
    }
}
