using UnityEngine;
using System.Collections.Generic;

public class CloudManager : MonoBehaviour
{
    public float voxelSize = 4f;
    public int layerRadius = 40; // Số ô mây xung quanh -> 40 * 4 = 160m
    public float driftSpeed = 1.0f;
    public float regenerateThreshold = 20f; // Khoảng cách trôi trước khi rebuild
    public Material cloudMaterial;

    private float timeOffset = 0f;
    private Vector3 lastGeneratedPos;
    
    private GameObject cloudMeshObj;
    private MeshFilter mf;
    private MeshRenderer mr;
    
    private Transform player;

    public void Initialize(Transform playerTransform, Material mat)
    {
        player = playerTransform;
        cloudMaterial = mat;
        
        cloudMeshObj = new GameObject("CloudMesh");
        cloudMeshObj.transform.parent = transform;
        mf = cloudMeshObj.AddComponent<MeshFilter>();
        mr = cloudMeshObj.AddComponent<MeshRenderer>();
        mr.material = cloudMaterial;
        
        // Bật bóng râm đổ xuống đất, tắt nhận bóng râm lên chính mây
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        mr.receiveShadows = false;
        
        RegenerateCloudMesh();
    }

    void Update()
    {
        if (player == null) return;
        
        // Mây trôi vật lý (Mesh trôi theo X) để mượt mà ở 60FPS
        cloudMeshObj.transform.position += Vector3.right * driftSpeed * Time.deltaTime;
        
        // Dịch timeOffset ngầm để chuẩn bị cho lần Rebuild tiếp theo
        // Chia cho voxelSize để đồng bộ với thế giới noise thực
        timeOffset += driftSpeed * Time.deltaTime / voxelSize;

        // Nếu lưới mây đã trôi đi quá xa (quá giới hạn che phủ), ta Rebuild và giật nó về giữa Player
        float dist = Vector3.Distance(new Vector3(cloudMeshObj.transform.position.x, 0, cloudMeshObj.transform.position.z), 
                                      new Vector3(lastGeneratedPos.x, 0, lastGeneratedPos.z));
                                      
        if (dist > regenerateThreshold)
        {
            RegenerateCloudMesh();
        }
    }
    
    // Thuật toán sinh mây tự nhiên bằng fBm Noise
    float CloudNoise(float x, float z, float tOffset)
    {
        float n = 0f;
        float amplitude = 1f;
        // Tăng frequency nhẹ lên 0.03 để các cụm mây không bị bành trướng quá to
        float frequency = 0.03f;
        float maxValue = 0f;

        for (int octave = 0; octave < 3; octave++)
        {
            n += Mathf.PerlinNoise((x - tOffset) * frequency, z * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= 0.5f;
            // Đổi từ 2.5 -> 2.0 theo chuẩn fBm, giảm noise vụn
            frequency *= 2.0f;
        }
        return n / maxValue;
    }

    bool IsCloudVoxel(int x, int y, int z, float tOffset)
    {
        // Trở lại độ dày 3D (Y từ 0 đến 3) để có mây dạng khối bồng bềnh
        if (y < 0 || y > 3) return false;
        
        float density = CloudNoise(x, z, tOffset);
        
        // Trừ bớt density khi lên cao để mây tạo hình vòm/búp trên đỉnh
        float heightFalloff = y * 0.1f; 
        float finalDensity = density - heightFalloff;
        
        // Nâng threshold lên 0.5f để mây không thành 1 tảng khổng lồ mà tách thành nhiều cụm liên kết
        return finalDensity > 0.5f;
    }

    void RegenerateCloudMesh()
    {
        if (player == null) return;
        
        // Khóa trung tâm tại Player
        float px = Mathf.Round(player.position.x / voxelSize) * voxelSize;
        float pz = Mathf.Round(player.position.z / voxelSize) * voxelSize;
        
        lastGeneratedPos = new Vector3(px, 0, pz);
        // Trả mây về đúng độ cao chuẩn Y=100
        cloudMeshObj.transform.position = new Vector3(px, 100f, pz); 
        
        // Thuật toán Face Culling tái sử dụng từ Chunk
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        
        int halfRadius = layerRadius / 2;
        
        for (int x = -halfRadius; x <= halfRadius; x++)
        {
            for (int z = -halfRadius; z <= halfRadius; z++)
            {
                for (int y = 0; y < 4; y++) // Độ dày tối đa: 4 khối (từ 0 đến 3)
                {
                    // Tọa độ Voxel toàn cục (dùng để tra Noise)
                    float globalX = (px / voxelSize) + x;
                    float globalZ = (pz / voxelSize) + z;
                    
                    if (!IsCloudVoxel((int)globalX, y, (int)globalZ, timeOffset))
                        continue;
                        
                    Vector3 localPos = new Vector3(x * voxelSize, y * voxelSize, z * voxelSize);
                    
                    // CHUẨN HÓA LẠI THEO CHUNK.CS: mảng Vector3Int 6 hướng
                    Vector3Int[] faceDirections = new Vector3Int[]
                    {
                        new Vector3Int( 1,  0,  0),  // Right  (+X)
                        new Vector3Int(-1,  0,  0),  // Left   (-X)
                        new Vector3Int( 0,  1,  0),  // Top    (+Y)
                        new Vector3Int( 0, -1,  0),  // Bottom (-Y)
                        new Vector3Int( 0,  0,  1),  // Front  (+Z)
                        new Vector3Int( 0,  0, -1)   // Back   (-Z)
                    };
                    
                    for (int f = 0; f < 6; f++)
                    {
                        Vector3Int dir = faceDirections[f];
                        // Kiểm tra xem láng giềng ở hướng này CÓ PHẢI LÀ MÂY KHÔNG
                        // Nếu láng giềng KHÔNG phải mây (hoặc là Air), ta mới vẽ mặt này để che lại
                        if (!IsCloudVoxel((int)(globalX + dir.x), y + dir.y, (int)(globalZ + dir.z), timeOffset))
                        {
                            AddFace(localPos, f, vertices, triangles);
                        }
                    }
                }
            }
        }
        
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        
        mf.mesh = mesh;
    }
    
    void AddFace(Vector3 pos, int faceIndex, List<Vector3> verts, List<int> tris)
    {
        int vIdx = verts.Count;
        
        // Cấu trúc đỉnh MỚI, khớp 100% với Chunk.cs
        Vector3[][] faceVerts = new Vector3[][]
        {
            // Right (+X)
            new Vector3[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) }, 
            // Left (-X)
            new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) }, 
            // Top (+Y)
            new Vector3[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) }, 
            // Bottom (-Y)
            new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1) }, 
            // Front (+Z)
            new Vector3[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) }, 
            // Back (-Z)
            new Vector3[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }  
        };
        
        for (int i = 0; i < 4; i++)
        {
            verts.Add(pos + faceVerts[faceIndex][i] * voxelSize);
        }
        
        // KHỚP VỚI Chunk.cs: Winding order cho mặt trước và mặt sau
        tris.Add(vIdx);
        tris.Add(vIdx + 1);
        tris.Add(vIdx + 2);

        tris.Add(vIdx);
        tris.Add(vIdx + 2);
        tris.Add(vIdx + 3);
    }
}
