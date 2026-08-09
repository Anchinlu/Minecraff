using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Chunk chứa block data và tự generate mesh của chính nó.
/// 
/// === KÍCH THƯỚC ===
/// Giai đoạn 1: 16×16×16 (cube test)
/// Giai đoạn 2+: 16×128×16 (ChunkWidth × ChunkHeight × ChunkWidth)
///   - Width = 16: kích thước ngang (X, Z)
///   - Height = 128: chiều cao (Y), đủ cho địa hình đồi núi
///
/// === CƠ CHẾ FACE CULLING ===
/// Mỗi block có 6 mặt (Top, Bottom, Front, Back, Left, Right).
/// Với mỗi mặt, ta kiểm tra block hàng xóm theo hướng đó:
///   - Hàng xóm = Air (hoặc ngoài biên chunk) → VẼ mặt này
///   - Hàng xóm ≠ Air → BỎ QUA (bị che khuất)
///
/// Vì phần lớn block trên bề mặt là Air, face culling cực kỳ hiệu quả:
/// 32,768 block nhưng chỉ vẽ vài nghìn face ở bề mặt địa hình.
/// </summary>
public class Chunk
{
    // === CONSTANTS ===
    public const int ChunkWidth = 16;
    public const int ChunkHeight = 128;

    // Giữ ChunkSize để tương thích với World.cs (dùng cho tọa độ XZ)
    public const int ChunkSize = ChunkWidth;

    // === BLOCK DATA ===
    // Mảng 3D: blocks[x, y, z] với x,z ∈ [0, ChunkWidth) và y ∈ [0, ChunkHeight)
    private BlockType[,,] blocks;
    
    // Mảng 3D lưu độ sáng (0 đến 15)
    private byte[,,] lightMap;
    public byte[,,] waterLevel; // Phase 1: 0 = Không nước, 1-7 = Nước chảy, 8 = Nước nguồn

    // Material fields (cho submeshes)
    private Material matVertexColor;
    private Material matGrassTexture;
    private Material matWater;

    // === MESH COMPONENT REFERENCES ===
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private MeshRenderer meshRenderer;

    // World reference
    private World world;
    public Vector3Int chunkWorldPos;

    // === MESH DATA ===
    private List<Vector3> vertices;
    private List<int> trianglesVertexColor;
    private List<int> trianglesGrass;
    private List<Color> colors;
    private List<Vector2> uvs;
    private List<Vector3> normals;

    public static class GrassUV
    {
        public const float TILE = 1f / 3f;

        public static readonly Vector2 Top    = new Vector2(0f, 0f);
        public static readonly Vector2 Side   = new Vector2(TILE, 0f);
        public static readonly Vector2 Bottom = new Vector2(TILE * 2, 0f);
    }

    // === FACE DIRECTIONS ===
    private static readonly Vector3Int[] faceDirections = new Vector3Int[]
    {
        new Vector3Int( 1,  0,  0),  // Right  (+X)
        new Vector3Int(-1,  0,  0),  // Left   (-X)
        new Vector3Int( 0,  1,  0),  // Top    (+Y)
        new Vector3Int( 0, -1,  0),  // Bottom (-Y)
        new Vector3Int( 0,  0,  1),  // Front  (+Z)
        new Vector3Int( 0,  0, -1)   // Back   (-Z)
    };

    // 6 bộ vertex offset cho mỗi face.
    // Thứ tự: chiều kim đồng hồ khi nhìn từ ngoài block → normal hướng ra ngoài.
    private static readonly Vector3[][] faceVertices = new Vector3[][]
    {
        // Right (+X)
        new Vector3[] {
            new Vector3(1, 0, 0), new Vector3(1, 1, 0),
            new Vector3(1, 1, 1), new Vector3(1, 0, 1)
        },
        // Left (-X)
        new Vector3[] {
            new Vector3(0, 0, 1), new Vector3(0, 1, 1),
            new Vector3(0, 1, 0), new Vector3(0, 0, 0)
        },
        // Top (+Y)
        new Vector3[] {
            new Vector3(0, 1, 0), new Vector3(0, 1, 1),
            new Vector3(1, 1, 1), new Vector3(1, 1, 0)
        },
        // Bottom (-Y)
        new Vector3[] {
            new Vector3(0, 0, 1), new Vector3(0, 0, 0),
            new Vector3(1, 0, 0), new Vector3(1, 0, 1)
        },
        // Front (+Z)
        new Vector3[] {
            new Vector3(1, 0, 1), new Vector3(1, 1, 1),
            new Vector3(0, 1, 1), new Vector3(0, 0, 1)
        },
        // Back (-Z)
        new Vector3[] {
            new Vector3(0, 0, 0), new Vector3(0, 1, 0),
            new Vector3(1, 1, 0), new Vector3(1, 0, 0)
        }
    };

    /// <summary>
    /// Khởi tạo chunk: cấp phát mảng block data 16×128×16.
    /// </summary>
    public Chunk()
    {
        blocks = new BlockType[ChunkWidth, ChunkHeight, ChunkWidth];
        lightMap = new byte[ChunkWidth, ChunkHeight, ChunkWidth];
        waterLevel = new byte[ChunkWidth, ChunkHeight, ChunkWidth];
    }

    /// <summary>
    /// Populate block data bằng TerrainGenerator.
    /// 
    /// Cần worldPos để tính Perlin Noise và để query block chunk lân cận.
    /// </summary>
    public void PopulateBlocks(World world, TerrainGenerator generator, Vector3Int chunkWorldPos)
    {
        this.world = world;
        this.chunkWorldPos = chunkWorldPos;

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int z = 0; z < ChunkWidth; z++)
            {
                // Tọa độ world = tọa độ local + offset chunk
                int worldX = x + chunkWorldPos.x;
                int worldZ = z + chunkWorldPos.z;

                int terrainHeight = generator.GetHeight(worldX, worldZ);
                float waterTable = generator.GetWaterTableHeight(worldX, worldZ);
                int waterTableInt = Mathf.FloorToInt(waterTable);

                for (int y = 0; y < ChunkHeight; y++)
                {
                    BlockType type;
                    if (y <= terrainHeight)
                    {
                        type = generator.GetTerrainBlockType(y, terrainHeight, waterTableInt);
                    }
                    else if (y <= waterTableInt)
                    {
                        type = BlockType.Water;
                    }
                    else
                    {
                        type = BlockType.Air;
                    }

                    blocks[x, y, z] = type;
                    if (type == BlockType.Water) 
                    {
                        waterLevel[x, y, z] = 8; // Mặc định khối nước sinh ra là Nguồn
                    }
                }
            }
        }

        CalculateSunlight();
    }

    public void Init(Material vertexColorMat, Material grassTextureMat, Material waterMat, Vector3Int worldPos)
    {
        matVertexColor = vertexColorMat;
        matGrassTexture = grassTextureMat;
        matWater = waterMat;
        chunkWorldPos = worldPos;
    }

    public void CalculateSunlight(Vector3Int? modifiedLocalPos = null)
    {
        Queue<Vector3Int> lightBfsQueue = new Queue<Vector3Int>();

        int startX = 0; int endX = ChunkWidth - 1;
        int startY = 0; int endY = ChunkHeight - 1;
        int startZ = 0; int endZ = ChunkWidth - 1;

        if (modifiedLocalPos.HasValue)
        {
            Vector3Int p = modifiedLocalPos.Value;
            startX = Mathf.Max(0, p.x - 15);
            endX = Mathf.Min(ChunkWidth - 1, p.x + 15);
            startY = Mathf.Max(0, p.y - 15);
            endY = Mathf.Min(ChunkHeight - 1, p.y + 15);
            startZ = Mathf.Max(0, p.z - 15);
            endZ = Mathf.Min(ChunkWidth - 1, p.z + 15);
        }

        // Pass 1: Tia nắng chiếu thẳng từ trên xuống (quét các cột trong vùng ảnh hưởng)
        for (int x = startX; x <= endX; x++)
        {
            for (int z = startZ; z <= endZ; z++)
            {
                byte currentLight = 15;
                for (int y = ChunkHeight - 1; y >= 0; y--)
                {
                    BlockType b = blocks[x, y, z];
                    if (b != BlockType.Air && b != BlockType.Water)
                    {
                        currentLight = 0; // Đụng vật cản rắn là tắt nắng
                    }
                    
                    if (y <= endY && y >= startY)
                    {
                        lightMap[x, y, z] = currentLight;
                        if (currentLight > 0)
                        {
                            lightBfsQueue.Enqueue(new Vector3Int(x, y, z));
                        }
                    }
                }
            }
        }

        // Đưa các block viền của vùng ảnh hưởng (đang có ánh sáng) vào Queue để loang ngược vào trong
        if (modifiedLocalPos.HasValue)
        {
            for (int x = startX; x <= endX; x++)
            for (int y = startY; y <= endY; y++)
            for (int z = startZ; z <= endZ; z++)
            {
                if (x == startX || x == endX || y == startY || y == endY || z == startZ || z == endZ)
                {
                    Vector3Int[] dirs = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };
                    foreach (var d in dirs)
                    {
                        int nx = x + d.x, ny = y + d.y, nz = z + d.z;
                        if (nx >= 0 && nx < ChunkWidth && ny >= 0 && ny < ChunkHeight && nz >= 0 && nz < ChunkWidth)
                        {
                            if (nx < startX || nx > endX || ny < startY || ny > endY || nz < startZ || nz > endZ)
                            {
                                if (lightMap[nx, ny, nz] > 0)
                                    lightBfsQueue.Enqueue(new Vector3Int(nx, ny, nz));
                            }
                        }
                    }
                }
            }
        }

        // Pass 2: Lan truyền ánh sáng (Flood Fill BFS)
        while (lightBfsQueue.Count > 0)
        {
            Vector3Int node = lightBfsQueue.Dequeue();
            byte lightLevel = lightMap[node.x, node.y, node.z];

            for (int i = 0; i < 6; i++)
            {
                Vector3Int neighborPos = node + faceDirections[i];
                
                // Chỉ lan truyền bên trong Chunk hiện tại (giai đoạn này chưa làm lan xuyên Chunk)
                if (neighborPos.x >= 0 && neighborPos.x < ChunkWidth &&
                    neighborPos.y >= 0 && neighborPos.y < ChunkHeight &&
                    neighborPos.z >= 0 && neighborPos.z < ChunkWidth)
                {
                    // Chỉ lan truyền nếu láng giềng là trong suốt (Air/Water)
                    BlockType nb = blocks[neighborPos.x, neighborPos.y, neighborPos.z];
                    if (nb == BlockType.Air || nb == BlockType.Water)
                    {
                        byte propagatedLight = (byte)(lightLevel - 1);
                        if (lightMap[neighborPos.x, neighborPos.y, neighborPos.z] < propagatedLight)
                        {
                            lightMap[neighborPos.x, neighborPos.y, neighborPos.z] = propagatedLight;
                            lightBfsQueue.Enqueue(neighborPos);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gán reference tới mesh components. Gọi từ World sau khi tạo GameObject.
    /// </summary>
    public void SetMeshComponents(MeshFilter filter, MeshCollider collider, MeshRenderer renderer)
    {
        meshFilter = filter;
        meshCollider = collider;
        meshRenderer = renderer;
    }

    /// <summary>
    /// Đổi block tại vị trí local (x, y, z).
    /// Trả về true nếu thay đổi thành công, false nếu ngoài biên.
    /// Sau khi gọi, cần gọi RebuildMesh() để cập nhật visual.
    /// </summary>
    public bool SetBlock(int x, int y, int z, BlockType blockType)
    {
        if (x < 0 || x >= ChunkWidth || y < 0 || y >= ChunkHeight || z < 0 || z >= ChunkWidth) return false;
        
        if (blocks[x, y, z] != blockType)
        {
            blocks[x, y, z] = blockType;
            return true; // Trả về true báo hiệu chunk đã thay đổi, cần build lại mesh
        }
        return false;
    }

    public bool SetWaterLevel(int x, int y, int z, byte level)
    {
        if (x < 0 || x >= ChunkWidth || y < 0 || y >= ChunkHeight || z < 0 || z >= ChunkWidth) return false;
        
        if (waterLevel[x, y, z] != level)
        {
            waterLevel[x, y, z] = level;
            return true; // Trả về true báo hiệu chunk đã thay đổi
        }
        return false;
    }

    private float GetVertexWaterHeight(int x, int y, int z, int cornerDX, int cornerDZ)
    {
        float totalHeight = 0f;
        int count = 0;

        for (int dx = 0; dx <= 1; dx++)
        {
            for (int dz = 0; dz <= 1; dz++)
            {
                int nx = x + (dx == 0 ? 0 : cornerDX);
                int nz = z + (dz == 0 ? 0 : cornerDZ);

                BlockType neighborType = GetBlockAt(nx, y, nz);
                if (neighborType == BlockType.Water)
                {
                    BlockType upNeighbor = GetBlockAt(nx, y + 1, nz);
                    if (upNeighbor == BlockType.Water)
                    {
                        totalHeight += 1.0f;
                    }
                    else
                    {
                        byte level = GetWaterLevelAt(nx, y, nz);
                        totalHeight += (level >= 8) ? 0.9f : (level / 8f) * 0.9f;
                    }
                    count++;
                }
                else if (neighborType == BlockType.Air)
                {
                    // LỖI CŨ: totalHeight += 0f; (Kéo tụt nước xuống 0 tạo ra dốc và rách lưới ở chunk boundary)
                    // SỬA: Giữ nguyên mặt phẳng nước 0.9f để nước phẳng như gương, không bị rách
                    totalHeight += 0.9f; 
                    count++;
                }
            }
        }

        return count > 0 ? totalHeight / count : 0.9f;
    }

    /// <summary>
    /// Generate lại mesh và gán cho MeshFilter + MeshCollider.
    /// Gọi sau khi SetBlock() để cập nhật visual realtime.
    /// </summary>
    public void RebuildMesh()
    {
        Mesh mesh = GenerateMesh();

        if (meshFilter != null)
            meshFilter.mesh = mesh;

        if (meshCollider != null)
        {
            // Tối ưu: Tạo Mesh riêng cho Collider, CHỈ bao gồm Opaque (0) và Grass (1)
            // Lọc bỏ Water (2) để Raycast xuyên qua nước, người chơi bơi qua nước,
            // và click đặt block sẽ thay thế nước thay vì đè lên trên.
            Mesh colMesh = new Mesh();
            colMesh.vertices = mesh.vertices;
            
            List<int> colTriangles = new List<int>();
            if (mesh.subMeshCount > 0) colTriangles.AddRange(mesh.GetTriangles(0)); // VertexColor (Opaque)
            if (mesh.subMeshCount > 1) colTriangles.AddRange(mesh.GetTriangles(1)); // Grass
            
            colMesh.SetTriangles(colTriangles.ToArray(), 0);

            meshCollider.cookingOptions = MeshColliderCookingOptions.None;
            meshCollider.sharedMesh = colMesh;
        }

        if (meshRenderer != null)
            meshRenderer.materials = new Material[] { matVertexColor, matGrassTexture, matWater };
    }

    /// <summary>
    /// Thêm MeshCollider động cho các chunk khi người chơi đến gần (nếu chưa có).
    /// Giúp tiết kiệm cực nhiều CPU Physics Bake ở những chunk xa.
    /// </summary>
    public void EnsureColliderAdded()
    {
        if (meshCollider != null || meshFilter == null || meshFilter.sharedMesh == null) return;
        
        meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
        
        Mesh fullMesh = meshFilter.sharedMesh;
        Mesh colMesh = new Mesh();
        colMesh.vertices = fullMesh.vertices;
        
        List<int> colTriangles = new List<int>();
        if (fullMesh.subMeshCount > 0) colTriangles.AddRange(fullMesh.GetTriangles(0)); // Opaque
        if (fullMesh.subMeshCount > 1) colTriangles.AddRange(fullMesh.GetTriangles(1)); // Grass
        
        colMesh.SetTriangles(colTriangles.ToArray(), 0);
        
        meshCollider.cookingOptions = MeshColliderCookingOptions.None;
        meshCollider.sharedMesh = colMesh;
    }

    /// <summary>
    /// Lấy BlockType tại vị trí local (x, y, z).
    /// Nếu ngoài biên chunk → hỏi World để lấy block từ chunk láng giềng.
    /// Nếu láng giềng chưa load, World trả về Air (sẽ được vẽ mặt).
    /// </summary>
    public BlockType GetBlockAt(int x, int y, int z)
    {
        if (y < 0 || y >= ChunkHeight) return BlockType.Air;

        if (x < 0 || x >= ChunkWidth || z < 0 || z >= ChunkWidth)
        {
            if (world != null)
                return world.GetBlock(chunkWorldPos + new Vector3Int(x, y, z));
            return BlockType.Air;
        }

        return blocks[x, y, z];
    }

    public byte GetWaterLevelAt(int x, int y, int z)
    {
        if (y < 0 || y >= ChunkHeight) return 0;

        if (x < 0 || x >= ChunkWidth || z < 0 || z >= ChunkWidth)
        {
            if (world != null)
                return world.GetWaterLevel(chunkWorldPos + new Vector3Int(x, y, z));
            return 0;
        }

        return waterLevel[x, y, z];
    }

    /// <summary>
    /// Lấy giá trị ánh sáng tại vị trí local (x, y, z).
    /// Hỗ trợ cross-chunk query qua World.
    /// </summary>
    public byte GetLightAt(int x, int y, int z)
    {
        if (y < 0 || y >= ChunkHeight) return 15; // Ngoài trời luôn sáng tối đa

        if (x < 0 || x >= ChunkWidth || z < 0 || z >= ChunkWidth)
        {
            if (world != null)
                return world.GetLight(chunkWorldPos + new Vector3Int(x, y, z));
            return 15; // Mặc định sáng
        }

        return lightMap[x, y, z];
    }

    public bool IsOpaque(Vector3Int pos)
    {
        BlockType b = GetBlockAt(pos.x, pos.y, pos.z);
        return b != BlockType.Air && b != BlockType.Water;
    }

    /// <summary>
    /// Generate mesh cho toàn bộ chunk.
    /// 
    /// Duyệt 16×128×16 block, chỉ vẽ face tiếp giáp Air.
    /// Phần lớn block trên bề mặt = Air → skip nhanh.
    /// </summary>
    public Mesh GenerateMesh()
    {
        vertices = new List<Vector3>();
        trianglesVertexColor = new List<int>();
        trianglesGrass = new List<int>();
        List<int> trianglesWater = new List<int>();
        colors = new List<Color>();
        uvs = new List<Vector2>();
        normals = new List<Vector3>();

        Dictionary<Vector3Int, bool> opaqueCache = new Dictionary<Vector3Int, bool>();
        Dictionary<Vector3Int, byte> lightCache = new Dictionary<Vector3Int, byte>();

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                for (int z = 0; z < ChunkWidth; z++)
                {
                    BlockType currentBlock = blocks[x, y, z];

                    if (currentBlock == BlockType.Air)
                        continue;

                    Vector3Int blockPos = new Vector3Int(x, y, z);

                    for (int face = 0; face < 6; face++)
                    {
                        Vector3Int neighborPos = blockPos + faceDirections[face];
                        BlockType neighbor = GetBlockAt(neighborPos.x, neighborPos.y, neighborPos.z);

                        bool drawFace = false;
                        if (currentBlock == BlockType.Water)
                        {
                            // Water-Water: Xử lý khe hở (Gap) do chênh lệch độ cao
                            if (neighbor == BlockType.Water) 
                            {
                                if (face == 2 || face == 3) 
                                {
                                    drawFace = false; // Mặt Top/Bottom của cột nước luôn nối liền
                                }
                                else
                                {
                                    // So sánh độ cao để quyết định vẽ mặt bên
                                    bool hasWaterAboveCurrent = GetBlockAt(blockPos.x, blockPos.y + 1, blockPos.z) == BlockType.Water;
                                    bool hasWaterAboveNeighbor = GetBlockAt(neighborPos.x, neighborPos.y + 1, neighborPos.z) == BlockType.Water;
                                    
                                    if (hasWaterAboveCurrent && !hasWaterAboveNeighbor) 
                                    {
                                        drawFace = true; // Mình là cột đứng, hàng xóm là mặt thoáng -> Vẽ mặt để lấp gap
                                    }
                                    else
                                    {
                                        // Các trường hợp còn lại (mặt hồ phẳng, hoặc sâu dưới lòng hồ) -> ẩn mặt nối
                                        drawFace = false;
                                    }
                                }
                            }
                            // Water-Air hoặc Water-Solid: LUÔN vẽ
                            else
                            {
                                drawFace = true;
                            }
                        }
                        else
                        {
                            // Đất đá vẽ nếu kề nước hoặc khí
                            if (neighbor == BlockType.Air || neighbor == BlockType.Water) drawFace = true;
                        }

                        if (drawFace)
                        {
                            AddFace(blockPos, face, currentBlock, opaqueCache, lightCache, trianglesWater);
                        }
                    }
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        
        mesh.subMeshCount = 3;
        mesh.SetTriangles(trianglesVertexColor.ToArray(), 0);
        mesh.SetTriangles(trianglesGrass.ToArray(), 1);
        mesh.SetTriangles(trianglesWater.ToArray(), 2);

        mesh.colors = colors.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private bool IsOpaqueCached(Vector3Int pos, Dictionary<Vector3Int, bool> cache)
    {
        if (cache.TryGetValue(pos, out bool isOpaque)) return isOpaque;
        bool result = IsOpaque(pos);
        cache[pos] = result;
        return result;
    }

    private byte GetLightAtCached(Vector3Int pos, Dictionary<Vector3Int, byte> cache)
    {
        if (cache.TryGetValue(pos, out byte light)) return light;
        byte result = GetLightAt(pos.x, pos.y, pos.z);
        cache[pos] = result;
        return result;
    }

    /// <summary>
    /// Thêm 1 face (4 vertices + 2 triangles + 4 colors) vào mesh data.
    /// </summary>
    private void AddFace(Vector3Int blockPos, int faceIndex, BlockType blockType, Dictionary<Vector3Int, bool> opaqueCache, Dictionary<Vector3Int, byte> lightCache, List<int> trianglesWater)
    {
        int vertexIndex = vertices.Count;

        for (int i = 0; i < 4; i++)
        {
            Vector3 v = (Vector3)blockPos + faceVertices[faceIndex][i];
            
            // Xử lý co lại Y của nước dựa trên waterLevel
            // Dùng Vertex Interpolation để tạo độ dốc mượt mà
            if (blockType == BlockType.Water && v.y > blockPos.y) 
            {
                BlockType blockAbove = GetBlockAt(blockPos.x, blockPos.y + 1, blockPos.z);
                if (blockAbove != BlockType.Water)
                {
                    int cornerDX = (v.x > blockPos.x) ? 1 : -1;
                    int cornerDZ = (v.z > blockPos.z) ? 1 : -1;
                    
                    float height = GetVertexWaterHeight(blockPos.x, blockPos.y, blockPos.z, cornerDX, cornerDZ);
                    v.y = blockPos.y + height;
                }
            }

            vertices.Add(v);
        }

        // Add triangles to the correct submesh list
        if (blockType == BlockType.Water)
        {
            trianglesWater.Add(vertexIndex + 0);
            trianglesWater.Add(vertexIndex + 1);
            trianglesWater.Add(vertexIndex + 2);
            trianglesWater.Add(vertexIndex + 0);
            trianglesWater.Add(vertexIndex + 2);
            trianglesWater.Add(vertexIndex + 3);
        }
        else if (blockType == BlockType.Grass)
        {
            trianglesGrass.Add(vertexIndex + 0);
            trianglesGrass.Add(vertexIndex + 1);
            trianglesGrass.Add(vertexIndex + 2);
            trianglesGrass.Add(vertexIndex + 0);
            trianglesGrass.Add(vertexIndex + 2);
            trianglesGrass.Add(vertexIndex + 3);
        }
        else
        {
            trianglesVertexColor.Add(vertexIndex + 0);
            trianglesVertexColor.Add(vertexIndex + 1);
            trianglesVertexColor.Add(vertexIndex + 2);
            trianglesVertexColor.Add(vertexIndex + 0);
            trianglesVertexColor.Add(vertexIndex + 2);
            trianglesVertexColor.Add(vertexIndex + 3);
        }

        Vector3Int center = blockPos + faceDirections[faceIndex];
        Vector3 faceNormal = faceDirections[faceIndex];
        Color baseColor = (blockType == BlockType.Grass) ? Color.white : GetBlockColor(blockType);

        // Tính Smooth Lighting & AO cho 4 đỉnh của mặt
        for (int i = 0; i < 4; i++)
        {
            Vector3 v = faceVertices[faceIndex][i];
            
            Vector3Int d1 = Vector3Int.zero;
            Vector3Int d2 = Vector3Int.zero;
            
            if (faceDirections[faceIndex].x == 0) {
                if (d1 == Vector3Int.zero) d1 = new Vector3Int(v.x == 1 ? 1 : -1, 0, 0);
                else d2 = new Vector3Int(v.x == 1 ? 1 : -1, 0, 0);
            }
            if (faceDirections[faceIndex].y == 0) {
                if (d1 == Vector3Int.zero) d1 = new Vector3Int(0, v.y == 1 ? 1 : -1, 0);
                else d2 = new Vector3Int(0, v.y == 1 ? 1 : -1, 0);
            }
            if (faceDirections[faceIndex].z == 0) {
                if (d1 == Vector3Int.zero) d1 = new Vector3Int(0, 0, v.z == 1 ? 1 : -1);
                else d2 = new Vector3Int(0, 0, v.z == 1 ? 1 : -1);
            }

            Vector3Int posSide1 = center + d1;
            Vector3Int posSide2 = center + d2;
            Vector3Int posCorner = center + d1 + d2;
            
            bool oSide1 = IsOpaqueCached(posSide1, opaqueCache);
            bool oSide2 = IsOpaqueCached(posSide2, opaqueCache);
            bool oCorner = IsOpaqueCached(posCorner, opaqueCache);
            
            int lightCenter = GetLightAtCached(center, lightCache);
            int lightSide1 = GetLightAtCached(posSide1, lightCache);
            int lightSide2 = GetLightAtCached(posSide2, lightCache);
            int lightCorner = GetLightAtCached(posCorner, lightCache);
            
            int count = 1;
            int totalLight = lightCenter;
            int ao = 3;
            
            if (!oSide1) { count++; totalLight += lightSide1; } else ao--;
            if (!oSide2) { count++; totalLight += lightSide2; } else ao--;
            
            if (oSide1 && oSide2) {
                ao--; // Góc bị chặn hoàn toàn
            } else {
                if (!oCorner) { count++; totalLight += lightCorner; } else ao--;
            }
            
            // Tính độ sáng cơ bản (từ 0 đến 1)
            float normalizedLight = (totalLight / (float)count) / 15f;
            
            // Tinh chỉnh đường cong ánh sáng (Light Curve)
            // Cài đặt tối kịch khung (2% sáng) để hang động cực kỳ khó nhìn
            float minLight = 0.02f;
            float vertexLight = minLight + Mathf.Pow(normalizedLight, 0.7f) * (1f - minLight);
            
            float vertexAO = ao / 3f; // 0.0 tới 1.0
            float aoMult = 0.5f + (vertexAO * 0.5f); // Hệ số làm tối của AO (0.5 đến 1.0)
            
            float totalLightMult = vertexLight * aoMult;
            
            Color vertexColor = baseColor * totalLightMult;
            vertexColor.a = 1f;
            colors.Add(vertexColor);
            normals.Add(faceNormal);
        }

        if (blockType == BlockType.Grass)
        {
            Vector2 uvOrigin = GrassUV.Side;
            if (faceIndex == 2) uvOrigin = GrassUV.Top;
            else if (faceIndex == 3) uvOrigin = GrassUV.Bottom;

            // Cắt hẳn 1 pixel nguyên để triệt tiêu mọi khả năng làm mờ của Unity
            float shrinkX = 1.0f / 48f; 
            float shrinkY = 1.0f / 16f;

            uvs.Add(uvOrigin + new Vector2(shrinkX, shrinkY));
            uvs.Add(uvOrigin + new Vector2(shrinkX, 1f - shrinkY));
            uvs.Add(uvOrigin + new Vector2(GrassUV.TILE - shrinkX, 1f - shrinkY));
            uvs.Add(uvOrigin + new Vector2(GrassUV.TILE - shrinkX, shrinkY));
        }
        else if (blockType == BlockType.Water)
        {
            // Truyền cờ "bề mặt trên cùng" qua UV.y để Shader làm gợn sóng mà không bị rách lưới
            BlockType blockAbove = GetBlockAt(blockPos.x, blockPos.y + 1, blockPos.z);
            bool isTopSurface = (blockAbove != BlockType.Water);
            
            for (int i = 0; i < 4; i++)
            {
                Vector3 localVert = faceVertices[faceIndex][i];
                // Nếu đỉnh nằm ở y=1 và khối này là bề mặt trên cùng của khối nước
                if (localVert.y > 0.5f && isTopSurface) 
                    uvs.Add(new Vector2(0, 1)); // 1 = có gợn sóng
                else 
                    uvs.Add(new Vector2(0, 0)); // 0 = đứng im
            }
        }
        else
        {
            // UV rỗng
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.zero);
        }
    }

    /// <summary>
    /// Trả về tọa độ (Cột X, Hàng Y) trên Texture Atlas cho từng loại Block.
    /// Tính từ góc trên-trái (Cột 0, Hàng 0).
    /// </summary>
    private Color GetBlockColor(BlockType blockType)
    {
        switch (blockType)
        {
            case BlockType.Stone: return new Color(0.5f, 0.5f, 0.5f);
            case BlockType.Dirt:  return new Color(0.55f, 0.35f, 0.17f);
            case BlockType.Water: return new Color(0.2f, 0.5f, 0.75f); // Xanh dương
            default:              return Color.magenta;
        }
    }
}
