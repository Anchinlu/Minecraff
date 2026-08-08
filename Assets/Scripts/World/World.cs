using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// World quản lý tất cả các chunk trong thế giới.
/// 
/// Giai đoạn 4: Multi-chunk (Thế giới vô tận).
/// - Theo dõi vị trí player, tự động load/unload chunk trong viewDistance.
/// - Xử lý biên chunk: trigger rebuild mesh láng giềng khi tạo chunk mới.
/// </summary>
public class World : MonoBehaviour
{
    [Header("Settings")]
    public int viewDistance = 3; // Bán kính load chunk (tính bằng số chunk)

    public Transform player;

    public Material matVertexColor;
    public Material matGrassTexture;

    private TerrainGenerator terrainGenerator;

    // Dictionary lưu chunk data đang load
    private Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
    
    // Lưu chunk GameObject để destroy khi unload
    private Dictionary<Vector3Int, GameObject> chunkObjects = new Dictionary<Vector3Int, GameObject>();

    private Vector3Int currentPlayerChunkCoord;
    private bool initialized = false;

    public void SetMaterials(Material vertexColorMat, Material grassTextureMat)
    {
        matVertexColor = vertexColorMat;
        matGrassTexture = grassTextureMat;
    }

    public TerrainGenerator GetTerrainGenerator()
    {
        return terrainGenerator;
    }

    public void Initialize()
    {
        terrainGenerator = new TerrainGenerator();
        Debug.Log($"[World] TerrainGenerator — seed: {terrainGenerator.seed:F1}");

        // Cập nhật chunk lần đầu (sẽ load các chunk xung quanh 0,0,0)
        currentPlayerChunkCoord = new Vector3Int(0, 0, 0); // Giả định player spawn ở 0,0
        UpdateVisibleChunks();
        
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
    }

    /// <summary>
    /// Load chunk trong viewDistance, unload chunk ngoài viewDistance.
    /// Chạy đồng bộ (sẽ gây khựng nhẹ, tối ưu ở Giai đoạn 6).
    /// </summary>
    private void UpdateVisibleChunks()
    {
        List<Vector3Int> chunksToKeep = new List<Vector3Int>();

        // Duyệt lưới vuông xung quanh player
        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                Vector3Int coord = new Vector3Int(currentPlayerChunkCoord.x + x, 0, currentPlayerChunkCoord.z + z);
                chunksToKeep.Add(coord);

                // Load nếu chưa có
                if (!chunks.ContainsKey(coord))
                {
                    CreateChunk(coord);
                }
            }
        }

        // Unload chunk nằm ngoài tầm
        List<Vector3Int> chunksToRemove = new List<Vector3Int>();
        foreach (var coord in chunks.Keys)
        {
            if (!chunksToKeep.Contains(coord))
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
    }

    private void CreateChunk(Vector3Int chunkCoord)
    {
        Vector3Int chunkWorldPos = new Vector3Int(
            chunkCoord.x * Chunk.ChunkWidth,
            0,
            chunkCoord.z * Chunk.ChunkWidth
        );

        // Khởi tạo chunk với 2 materials
        Chunk chunk = new Chunk();
        chunk.Init(matVertexColor, matGrassTexture, chunkWorldPos);

        // Populate truyền World (this) vào để query cross-chunk
        chunk.PopulateBlocks(this, terrainGenerator, chunkWorldPos);

        GameObject chunkObject = new GameObject($"Chunk_{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}");
        chunkObject.transform.parent = transform;
        chunkObject.transform.position = (Vector3)chunkWorldPos;

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();

        chunk.SetMeshComponents(meshFilter, meshCollider, meshRenderer);

        // Lưu vào dictionary TRƯỚC KHI generate mesh
        // để láng giềng có thể lấy data từ chunk này
        chunks[chunkCoord] = chunk;
        chunkObjects[chunkCoord] = chunkObject;

        // Generate mesh cho chunk mới
        chunk.RebuildMesh();

        // Neighbor Rebuild: báo cho 4 láng giềng rebuild để cập nhật biên giới
        UpdateNeighborMeshes(chunkCoord);
    }

    /// <summary>
    /// Update mesh của 4 chunk láng giềng.
    /// Cần thiết vì khi chunk mới xuất hiện, nó che lấp các mặt biên của chunk cũ.
    /// </summary>
    private void UpdateNeighborMeshes(Vector3Int chunkCoord)
    {
        Vector3Int[] neighbors = {
            chunkCoord + Vector3Int.right,
            chunkCoord + Vector3Int.left,
            chunkCoord + new Vector3Int(0, 0, 1), // forward
            chunkCoord + new Vector3Int(0, 0, -1) // back
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
            chunk.CalculateSunlight();
            chunk.RebuildMesh();
            
            // Xử lý rebuild láng giềng nếu đặt/đào sát viền chunk
            if (localPos.x == 0) UpdateNeighborMeshIfLoaded(chunkCoord + Vector3Int.left);
            else if (localPos.x == Chunk.ChunkWidth - 1) UpdateNeighborMeshIfLoaded(chunkCoord + Vector3Int.right);
            
            if (localPos.z == 0) UpdateNeighborMeshIfLoaded(chunkCoord + new Vector3Int(0, 0, -1));
            else if (localPos.z == Chunk.ChunkWidth - 1) UpdateNeighborMeshIfLoaded(chunkCoord + new Vector3Int(0, 0, 1));
        }
    }
    
    private void UpdateNeighborMeshIfLoaded(Vector3Int neighborCoord)
    {
        if (chunks.ContainsKey(neighborCoord))
        {
            chunks[neighborCoord].CalculateSunlight();
            chunks[neighborCoord].RebuildMesh();
        }
    }

    public BlockType GetBlock(Vector3Int worldPos)
    {
        Vector3Int chunkCoord = WorldToChunkCoord(worldPos);
        if (!chunks.ContainsKey(chunkCoord)) return BlockType.Air;

        Vector3Int localPos = WorldToLocalPos(worldPos, chunkCoord);
        return chunks[chunkCoord].GetBlockAt(localPos.x, localPos.y, localPos.z);
    }

    public byte GetLight(Vector3Int worldPos)
    {
        Vector3Int chunkCoord = WorldToChunkCoord(worldPos);
        if (!chunks.ContainsKey(chunkCoord)) return 15; // Mặc định sáng tối đa ngoài biên

        Vector3Int localPos = WorldToLocalPos(worldPos, chunkCoord);
        return chunks[chunkCoord].GetLightAt(localPos.x, localPos.y, localPos.z);
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
