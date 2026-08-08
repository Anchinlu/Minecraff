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

    // Material fields (cho submeshes)
    private Material matVertexColor;
    private Material matGrassTexture;

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

                for (int y = 0; y < ChunkHeight; y++)
                {
                    blocks[x, y, z] = generator.GetBlockType(worldX, y, worldZ);
                }
            }
        }

        CalculateSunlight();
    }

    public void Init(Material vertexColorMat, Material grassTextureMat, Vector3Int worldPos)
    {
        matVertexColor = vertexColorMat;
        matGrassTexture = grassTextureMat;
        chunkWorldPos = worldPos;
    }

    public void CalculateSunlight()
    {
        Queue<Vector3Int> lightBfsQueue = new Queue<Vector3Int>();

        // Pass 1: Tia nắng chiếu thẳng từ trên xuống
        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int z = 0; z < ChunkWidth; z++)
            {
                byte currentLight = 15;
                for (int y = ChunkHeight - 1; y >= 0; y--)
                {
                    if (blocks[x, y, z] != BlockType.Air)
                    {
                        currentLight = 0; // Đụng vật cản là tắt nắng
                    }
                    lightMap[x, y, z] = currentLight;

                    // Nếu có ánh sáng, đưa vào hàng đợi BFS để loang ra xung quanh
                    if (currentLight > 0)
                    {
                        lightBfsQueue.Enqueue(new Vector3Int(x, y, z));
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
                    // Chỉ lan truyền nếu láng giềng là Air và ánh sáng của láng giềng nhỏ hơn mức truyền tới
                    if (blocks[neighborPos.x, neighborPos.y, neighborPos.z] == BlockType.Air)
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
    public bool SetBlock(int x, int y, int z, BlockType newType)
    {
        if (x < 0 || x >= ChunkWidth ||
            y < 0 || y >= ChunkHeight ||
            z < 0 || z >= ChunkWidth)
        {
            return false;
        }

        blocks[x, y, z] = newType;
        return true;
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
            meshCollider.sharedMesh = mesh;

        if (meshRenderer != null)
            meshRenderer.materials = new Material[] { matVertexColor, matGrassTexture };
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
        return GetBlockAt(pos.x, pos.y, pos.z) != BlockType.Air;
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
        colors = new List<Color>();
        uvs = new List<Vector2>();
        normals = new List<Vector3>();

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

                        if (neighbor == BlockType.Air)
                        {
                            AddFace(blockPos, face, currentBlock);
                        }
                    }
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        
        mesh.subMeshCount = 2;
        mesh.SetTriangles(trianglesVertexColor.ToArray(), 0);
        mesh.SetTriangles(trianglesGrass.ToArray(), 1);

        mesh.colors = colors.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Thêm 1 face (4 vertices + 2 triangles + 4 colors) vào mesh data.
    /// </summary>
    private void AddFace(Vector3Int blockPos, int faceIndex, BlockType blockType)
    {
        int vertexIndex = vertices.Count;

        for (int i = 0; i < 4; i++)
        {
            vertices.Add((Vector3)blockPos + faceVertices[faceIndex][i]);
        }

        // Add triangles to the correct submesh list
        if (blockType == BlockType.Grass)
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
            
            bool oSide1 = IsOpaque(posSide1);
            bool oSide2 = IsOpaque(posSide2);
            bool oCorner = IsOpaque(posCorner);
            
            int lightCenter = GetLightAt(center.x, center.y, center.z);
            int lightSide1 = GetLightAt(posSide1.x, posSide1.y, posSide1.z);
            int lightSide2 = GetLightAt(posSide2.x, posSide2.y, posSide2.z);
            int lightCorner = GetLightAt(posCorner.x, posCorner.y, posCorner.z);
            
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
            default:              return Color.magenta;
        }
    }
}
