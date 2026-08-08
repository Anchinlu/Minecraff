using UnityEngine;

/// <summary>
/// TerrainGenerator sinh địa hình bằng Perlin Noise.
/// 
/// === CƠ CHẾ PERLIN NOISE ===
/// Perlin Noise là hàm toán học tạo giá trị "mượt" (smooth) theo tọa độ 2D.
/// Khác với Random.Range() cho giá trị nhảy lung tung, Perlin Noise đảm bảo
/// các điểm gần nhau có giá trị gần nhau → tạo ra đồi núi tự nhiên.
///
/// Cách dùng:
///   float height = Mathf.PerlinNoise(x / scale, z / scale) * maxHeight;
///   - x, z: tọa độ block trong world
///   - scale: "zoom level" — lớn hơn = đồi thoải, nhỏ hơn = nhấp nhô
///   - maxHeight: biên độ dao động chiều cao
///
/// === PHÂN LỚP BLOCK ===
/// Sau khi có chiều cao bề mặt (surfaceHeight), phân lớp theo độ sâu:
///   y == surfaceHeight     → Grass  (lớp cỏ trên cùng)
///   y >= surfaceHeight - 3 → Dirt   (3 lớp đất bên dưới)
///   y <  surfaceHeight - 3 → Stone  (đá, phần sâu nhất)
///   y >  surfaceHeight     → Air    (không khí)
/// 
/// Kết quả: nhìn từ bên cạnh sẽ thấy 3 lớp màu rõ ràng.
/// </summary>
public class TerrainGenerator
{
    // === PARAMETERS ===

    /// <summary>Chiều cao tối thiểu của địa hình. Không có block nào dưới mức này là Air.</summary>
    public int baseHeight = 20;

    /// <summary>Biên độ dao động thêm. Chiều cao thực tế = baseHeight + (0..maxHeight).</summary>
    public int maxHeight = 25;

    /// <summary>
    /// Tần số Perlin Noise — quyết định "kích thước" đồi núi.
    /// - Nhỏ (5-10): đồi nhỏ, nhấp nhô nhiều, giống núi đá
    /// - Trung bình (15-25): đồi vừa, tự nhiên nhất
    /// - Lớn (30+): đồi rất thoải, gần như phẳng
    /// </summary>
    public float noiseScale = 20f;

    /// <summary>
    /// Offset ngẫu nhiên — mỗi giá trị seed tạo ra địa hình hoàn toàn khác.
    /// Dùng cùng seed → cùng địa hình (reproducible).
    /// </summary>
    public float seed;

    /// <summary>Số lớp Dirt bên dưới bề mặt Grass.</summary>
    public int dirtLayerDepth = 3;

    /// <summary>
    /// Khởi tạo với seed ngẫu nhiên.
    /// Range 0-10000 để tránh Perlin Noise bị lặp pattern ở gốc tọa độ.
    /// </summary>
    public TerrainGenerator()
    {
        seed = Random.Range(0f, 10000f);
    }

    /// <summary>
    /// Tính chiều cao địa hình tại tọa độ world (worldX, worldZ).
    /// 
    /// Công thức: baseHeight + PerlinNoise(x/scale, z/scale) * maxHeight
    /// 
    /// Ví dụ với baseHeight=20, maxHeight=25:
    ///   - Perlin trả 0.0 → height = 20 (thung lũng)
    ///   - Perlin trả 0.5 → height = 32 (trung bình)
    ///   - Perlin trả 1.0 → height = 45 (đỉnh đồi)
    /// </summary>
    public int GetHeight(int worldX, int worldZ)
    {
        // 1. Noise Lục địa (Biome Noise) - Phóng to bản đồ để tạo các vùng (Vùng đồng bằng vs Vùng đồi núi)
        float biomeScale = 100f; // Tần số rất thấp để tạo mảng lớn
        float biomeNoise = Mathf.PerlinNoise((worldX + seed) / biomeScale, (worldZ + seed) / biomeScale);
        
        float heightMultiplier = 0f;
        float baseElevation = 0f;
        
        // 2. Chia vùng địa hình
        if (biomeNoise < 0.4f)
        {
            // Đồng bằng (Plains): Thấp và rất phẳng
            heightMultiplier = 0.15f; 
            baseElevation = 0f;
        }
        else if (biomeNoise < 0.6f)
        {
            // Vùng chuyển tiếp (Transition): Cong mượt (SmoothStep) từ đồng bằng lên đồi núi
            float t = (biomeNoise - 0.4f) / 0.2f; 
            t = t * t * (3f - 2f * t); // SmoothStep
            heightMultiplier = Mathf.Lerp(0.15f, 1.2f, t);
            baseElevation = Mathf.Lerp(0f, 8f, t); // Đồi được đôn cao nền lên 8 block
        }
        else
        {
            // Đồi núi (Hills): Cao và nhấp nhô mạnh
            heightMultiplier = 1.2f;
            baseElevation = 8f;
        }
        
        // 3. Noise Chi tiết (Detail Noise) - Tạo độ nhấp nhô cục bộ trên bề mặt
        float detailScale = 25f;
        float detailNoise = Mathf.PerlinNoise((worldX + seed) / detailScale, (worldZ + seed) / detailScale);
        
        // 4. Tổng hợp
        float finalHeight = baseHeight + baseElevation + (detailNoise * maxHeight * heightMultiplier);
        return Mathf.FloorToInt(finalHeight);
    }

    /// <summary>
    /// Xác định loại block tại vị trí (worldX, y, worldZ) dựa trên chiều cao bề mặt.
    /// 
    /// Logic phân lớp:
    /// - Trên bề mặt → Air
    /// - Đúng bề mặt → Grass
    /// - 1-3 block dưới bề mặt → Dirt
    /// - Sâu hơn nữa → Stone
    /// </summary>
    public BlockType GetBlockType(int worldX, int y, int worldZ)
    {
        int surfaceHeight = GetHeight(worldX, worldZ);

        if (y > surfaceHeight)
            return BlockType.Air;       // Trên bề mặt = không khí

        if (y == surfaceHeight)
            return BlockType.Grass;     // Đúng bề mặt = cỏ

        if (y >= surfaceHeight - dirtLayerDepth)
            return BlockType.Dirt;      // Vài lớp dưới = đất

        return BlockType.Stone;         // Phần còn lại = đá
    }
}
