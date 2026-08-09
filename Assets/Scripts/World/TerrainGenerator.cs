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

    // === TÍNH NĂNG MỚI: HỒ & SÔNG ===
    [Header("Water Table (Hồ) — ĐÃ SỬA: thêm Rarity Control")]
    public float baseWaterTableY = 30f;
    public float waterTableVariance = 8f;
    public float waterTableNoiseScale = 0.004f;

    [Header("Lake Rarity — MỚI")]
    public float lakeRegionNoiseScale = 0.0012f;
    public float lakeRegionThreshold = 0.72f;
    public float lakeSizeRarityExponent = 2.5f;

    [Header("River (Sông/Suối) — ĐÃ SỬA: thêm Rarity Control")]
    public float riverWarpStrength = 40f;
    public float riverWarpScale = 0.008f;
    public float riverNoiseScale = 0.006f;
    public float riverWidth = 0.02f;
    public int riverDepth = 4;

    [Header("River Rarity — MỚI")]
    public float riverPresenceNoiseScale = 0.001f;
    public float riverPresenceThreshold = 0.65f;
    public float riverWidthRarityExponent = 3f;

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
        int terrainHeight = Mathf.FloorToInt(finalHeight);
        
        CarveRiver(worldX, worldZ, ref terrainHeight);
        
        return terrainHeight;
    }

    public float GetWaterTableHeight(float x, float z)
    {
        // BƯỚC 1: Mask vùng — quyết định nơi này có được phép có hồ không
        float regionNoise = Mathf.PerlinNoise(x * lakeRegionNoiseScale, z * lakeRegionNoiseScale);
        float lakeMask = Mathf.Clamp01((regionNoise - lakeRegionThreshold) / (1f - lakeRegionThreshold));

        if (lakeMask <= 0f)
        {
            // Vùng KHÔNG được phép có hồ — trả về giá trị cực thấp, không bao giờ giao với terrain thật
            return -1000f;
        }

        // BƯỚC 2: Trong vùng được phép, tính kích thước — dùng Pow() để bias về phía nhỏ
        float sizeNoise = Mathf.PerlinNoise(x * waterTableNoiseScale, z * waterTableNoiseScale);
        float biasedSize = Mathf.Pow(sizeNoise, lakeSizeRarityExponent); // mũ cao → phần lớn kết quả nhỏ, hiếm khi gần 1 (to)

        // Nhân thêm lakeMask để hồ ở rìa vùng mask (lakeMask gần 0) nhỏ dần, mượt mà không cắt cụt đột ngột
        return baseWaterTableY + biasedSize * waterTableVariance * lakeMask;
    }

    public float GetRiverMask(float x, float z)
    {
        // Domain Warping — làm méo tọa độ trước khi sample, tạo đường cong tự nhiên
        float warpX = x + Mathf.PerlinNoise((x + seed) * riverWarpScale, (z + seed) * riverWarpScale) * riverWarpStrength;
        float warpZ = z + Mathf.PerlinNoise((x + seed) * riverWarpScale + 100f, (z + seed) * riverWarpScale) * riverWarpStrength;

        float riverNoise = Mathf.PerlinNoise(warpX * riverNoiseScale, warpZ * riverNoiseScale);
        return Mathf.Abs(riverNoise - 0.5f); // gần 0 = giữa lòng sông
    }

    public float GetRiverPresenceMask(float x, float z)
    {
        float presenceNoise = Mathf.PerlinNoise(x * riverPresenceNoiseScale + 500f, z * riverPresenceNoiseScale + 500f);
        return Mathf.Clamp01((presenceNoise - riverPresenceThreshold) / (1f - riverPresenceThreshold));
    }

    public void CarveRiver(int worldX, int worldZ, ref int terrainHeight)
    {
        float presenceMask = GetRiverPresenceMask(worldX, worldZ);
        if (presenceMask <= 0f) return; // Khu vực này KHÔNG có sông — bỏ qua hoàn toàn, tiết kiệm tính toán luôn

        float riverDist = GetRiverMask(worldX, worldZ); // hàm cũ, giữ nguyên (Domain Warping)

        // Độ rộng thực tế = riverWidth cơ bản × hệ số hiếm theo presenceMask
        // presenceMask càng cao (sâu trong vùng "có sông"), sông càng CÓ THỂ rộng hơn — nhưng vẫn qua Pow() để hiếm
        float widthFactor = Mathf.Pow(presenceMask, riverWidthRarityExponent);
        float actualRiverWidth = riverWidth * Mathf.Lerp(0.4f, 1.5f, widthFactor); // dao động 40%-150% độ rộng gốc

        if (riverDist < actualRiverWidth)
        {
            float carveFactor = 1f - Mathf.SmoothStep(0f, actualRiverWidth, riverDist);
            terrainHeight -= Mathf.RoundToInt(carveFactor * riverDepth);
        }
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
    public BlockType GetTerrainBlockType(int y, int surfaceHeight, int waterTable)
    {
        if (y == surfaceHeight)
        {
            if (y < waterTable) return BlockType.Dirt; // Bề mặt dưới nước là Đất bùn, không mọc cỏ
            return BlockType.Grass;     // Bề mặt trên cạn = Cỏ
        }

        if (y >= surfaceHeight - dirtLayerDepth)
            return BlockType.Dirt;      // Vài lớp dưới = đất

        return BlockType.Stone;         // Phần còn lại = đá
    }
}
