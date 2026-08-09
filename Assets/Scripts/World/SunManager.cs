using UnityEngine;
using System.Collections.Generic;

public class SunManager : MonoBehaviour
{
    public float voxelSize = 25f; // Tăng kích thước bự hơn
    public float distanceFromPlayer = 1500f; // Cách xa 1500 block (700 + 800 thêm)
    public Material sunMaterial;

    Transform player;
    DayNightCycle dayNight;
    MeshFilter mf;

    static readonly int[,] SunPattern = new int[7,7]
    {
        {0,0,1,1,1,0,0},
        {0,1,1,1,1,1,0},
        {1,1,1,1,1,1,1},
        {1,1,1,1,1,1,1},
        {1,1,1,1,1,1,1},
        {0,1,1,1,1,1,0},
        {0,0,1,1,1,0,0},
    };

    public void Initialize(Transform playerTransform, DayNightCycle dayNightRef, Material mat)
    {
        player = playerTransform;
        dayNight = dayNightRef;
        sunMaterial = mat;

        GameObject sunObj = new GameObject("SunMesh");
        sunObj.transform.parent = transform;
        mf = sunObj.AddComponent<MeshFilter>();
        MeshRenderer mr = sunObj.AddComponent<MeshRenderer>();
        mr.material = sunMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        BuildSunMesh(mf);
    }

    void LateUpdate()
    {
        if (player == null || dayNight == null) return;

        // Vector3.forward của Directional Light chỉ hướng chiếu sáng (từ trời xuống đất)
        // Nên lấy hướng ngược lại (-forward) để tìm vị trí mặt trời trên trời
        Vector3 sunDirection = -dayNight.GetSunDirection();
        transform.position = player.position + sunDirection * distanceFromPlayer;
        transform.LookAt(player.position);

        // Đồng bộ màu mặt trời: Neon rực + pha theo màu bầu trời
        if (sunMaterial != null)
        {
            Color baseColor = dayNight.GetSunColor();
            Color skyTint = dayNight.GetSkyHorizonColor();
            
            // Pha 70% màu nắng + 30% màu chân trời → mặt trời đổi sắc theo bầu trời
            Color blended = Color.Lerp(baseColor, skyTint, 0.3f);
            
            // Đẩy lên Neon HDR: nhân hệ số > 1 để phát sáng chói lòa vượt giới hạn màn hình
            float neonBoost = 2.5f;
            Color neonColor = new Color(
                blended.r * neonBoost,
                blended.g * neonBoost,
                blended.b * neonBoost,
                1f
            );
            
            sunMaterial.SetColor("_Color", neonColor);
        }
    }

    void BuildSunMesh(MeshFilter targetMf)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        int size = 7;
        int half = size / 2;

        Vector3Int[] faceDirections = new Vector3Int[]
        {
            new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
            new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1)
        };

        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            if (SunPattern[y, x] == 0) continue;

            // Chỉ 1 lớp Z=0..1 để có độ dày khối nhưng vẫn phẳng gọn (giống Minecraft sun)
            for (int f = 0; f < 6; f++)
            {
                Vector3Int dir = faceDirections[f];
                int nx = x + dir.x, ny = y + dir.y, nz = 0 + dir.z;

                bool neighborExists;
                if (nz != 0) neighborExists = false; // chỉ dày 1 lớp Z, mặt trước/sau luôn hở
                else if (nx < 0 || nx >= size || ny < 0 || ny >= size) neighborExists = false;
                else neighborExists = SunPattern[ny, nx] == 1;

                if (!neighborExists)
                {
                    Vector3 localPos = new Vector3((x - half) * voxelSize, (y - half) * voxelSize, 0);
                    AddFace(localPos, f, vertices, triangles);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        targetMf.mesh = mesh;
    }

    void AddFace(Vector3 pos, int faceIndex, List<Vector3> verts, List<int> tris)
    {
        int vIdx = verts.Count;
        Vector3[][] faceVerts = new Vector3[][]
        {
            // Right (+X)
            new Vector3[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) }, 
            // Left (-X)
            new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) }, 
            // Top (+Y)
            new Vector3[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) }, 
            // Bottom (-Y) - Khôi phục đúng nguyên mẫu của Chunk.cs để không bị Cull
            new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1) }, 
            // Front (+Z)
            new Vector3[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) }, 
            // Back (-Z)
            new Vector3[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }  
        };
        for (int i = 0; i < 4; i++)
            verts.Add(pos + faceVerts[faceIndex][i] * voxelSize);

        tris.Add(vIdx); tris.Add(vIdx+1); tris.Add(vIdx+2);
        tris.Add(vIdx); tris.Add(vIdx+2); tris.Add(vIdx+3);
    }
}
