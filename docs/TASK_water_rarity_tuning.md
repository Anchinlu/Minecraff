# Task: Tinh chỉnh Độ Hiếm & Phân Bố Tự Nhiên cho Hồ/Sông [SỬA TASK "Water Lake River Generation"]

## Bối cảnh

Task sinh Hồ/Sông tự động (Water Table + River Domain Warping) đã chạy, nhưng hồ/sông xuất hiện **quá thường xuyên và tràn lan**, không có cảm giác hiếm/quý theo kích thước. Task này thêm lớp "Mask tần suất" để kiểm soát: (1) hồ/sông chỉ xuất hiện ở 1 số vùng hiếm, không phải khắp nơi; (2) kích thước càng lớn càng hiếm gặp — đúng phân bố tự nhiên (nhiều ao nhỏ, ít hồ lớn).

## Nguyên lý cốt lõi

Tách biệt 2 câu hỏi thành 2 lớp Noise độc lập:
1. **"Vùng này CÓ ĐƯỢC PHÉP có hồ/sông không?"** — Mask tần số cực thấp, ngưỡng cao → phần lớn bản đồ trả lời "không"
2. **"Nếu được phép, TO CỠ NÀO?"** — Dùng `Mathf.Pow()` với số mũ > 1 để bias về phía nhỏ, kích thước lớn chỉ đạt được khi noise ngẫu nhiên rơi đúng vùng cực hiếm

---

## Phần 1: Hồ (Water Table) — thêm Lake Region Mask

### Sửa hàm `GetWaterTableHeight()` trong `TerrainGenerator.cs`

```csharp
[Header("Water Table (Hồ) — ĐÃ SỬA: thêm Rarity Control")]
public float baseWaterTableY = 30f;
public float waterTableVariance = 8f;
public float waterTableNoiseScale = 0.004f;

[Header("Lake Rarity — MỚI")]
public float lakeRegionNoiseScale = 0.0012f;   // CỰC thấp — quyết định vùng nào có thể có hồ, số càng nhỏ vùng càng rộng và hiếm
public float lakeRegionThreshold = 0.72f;      // 0-1, càng cao càng HIẾM vùng được phép có hồ (0.72 = chỉ ~28% diện tích được phép)
public float lakeSizeRarityExponent = 2.5f;    // càng cao, hồ lớn càng hiếm (hồ nhỏ vẫn phổ biến trong vùng được phép)

float GetWaterTableHeight(float x, float z)
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
```

### Vì sao cách này tạo đúng hiệu ứng mong muốn

- `lakeRegionThreshold = 0.72` → chỉ ~28% diện tích bản đồ **có khả năng** chứa hồ — phần còn lại trả về `-1000f`, không bao giờ có nước dù terrain có trũng thế nào
- `lakeSizeRarityExponent = 2.5` → với `sizeNoise` phân bố đều 0-1, sau khi qua `Pow(x, 2.5)`, phần lớn kết quả dồn về gần 0 (nhỏ), chỉ hiếm khi `sizeNoise` gần 1 mới cho ra hồ lớn — đúng nguyên lý "to càng hiếm"
- Nhân thêm `lakeMask` ở cuối → hồ tại rìa vùng cho phép sẽ nhỏ dần mượt mà, tránh viền cắt cụt như hình vuông (nếu chỉ dùng ngưỡng cứng)

---

## Phần 2: Sông/Suối — thêm River Presence Mask + Width Rarity

### Sửa hàm `GetRiverMask()` và `CarveRiver()` trong `TerrainGenerator.cs`

```csharp
[Header("River (Sông/Suối) — ĐÃ SỬA: thêm Rarity Control")]
public float riverWarpStrength = 40f;
public float riverWarpScale = 0.008f;
public float riverNoiseScale = 0.006f;
public float riverWidth = 0.02f;
public int riverDepth = 4;

[Header("River Rarity — MỚI")]
public float riverPresenceNoiseScale = 0.001f;  // CỰC thấp — quyết định khu vực nào có sông chảy qua
public float riverPresenceThreshold = 0.65f;    // càng cao càng hiếm khu vực có sông
public float riverWidthRarityExponent = 3f;     // càng cao, sông LỚN (rộng) càng hiếm

float GetRiverPresenceMask(float x, float z)
{
    float presenceNoise = Mathf.PerlinNoise(x * riverPresenceNoiseScale + 500f, z * riverPresenceNoiseScale + 500f);
    return Mathf.Clamp01((presenceNoise - riverPresenceThreshold) / (1f - riverPresenceThreshold));
}

void CarveRiver(int worldX, int worldZ, ref int terrainHeight)
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
```

### Vì sao sông lớn hiếm hơn sông nhỏ

`widthFactor` dùng `Pow(presenceMask, 3f)` — với `presenceMask` phân bố đều 0→1 sau khi qua mask ngưỡng, đa số giá trị dồn về gần 0 (sông hẹp, `Lerp` cho ra gần 40% độ rộng gốc), chỉ hiếm khi `presenceMask` gần 1 (vùng "lõi" sâu nhất của khu vực sông) mới đạt gần 150% độ rộng — tạo sông lớn cực hiếm, đa số là suối nhỏ.

## Tích hợp — thứ tự gọi hàm KHÔNG đổi so với task gốc

```csharp
int terrainHeight = GetBiomeHeight(worldX, worldZ);
CarveRiver(worldX, worldZ, ref terrainHeight); // giờ tự bỏ qua sớm nếu presenceMask <= 0, không tốn thêm chi phí đáng kể
// ... phần đặt block + Water Table dùng GetWaterTableHeight() đã sửa, không đổi gì thêm ở đây
```

## Kiểm thử

1. Generate world rộng (nhiều chunk theo mọi hướng) — quan sát tần suất hồ/sông xuất hiện phải **giảm rõ rệt** so với trước, phần lớn bản đồ không có nước
2. Trong số hồ/sông xuất hiện, phần lớn phải **nhỏ** — hồ/sông lớn chỉ xuất hiện lác đác, hiếm
3. Kiểm tra viền hồ tại ranh giới vùng mask — phải mượt dần (nhỏ dần), không bị cắt cụt hình vuông đột ngột
4. Nếu tần suất vẫn chưa "vừa mắt" — đây là bước cần **Play thử và chỉnh tay**, không có số chuẩn tuyệt đối:
   - Muốn hiếm hơn nữa: tăng `lakeRegionThreshold`/`riverPresenceThreshold` (tối đa gần 0.95)
   - Muốn hồ/sông lớn hiếm hơn nữa: tăng `lakeSizeRarityExponent`/`riverWidthRarityExponent` (thử 3-5)
   - Muốn vùng hồ/sông rộng hơn (dù hiếm): giảm `lakeRegionNoiseScale`/`riverPresenceNoiseScale` (số càng nhỏ, vùng noise càng rộng)

## Việc KHÔNG làm trong task này

- Không đổi kiến trúc Water Table/River Carving gốc (vẫn per-column, không BFS, không cần đồng bộ chunk) — chỉ thêm lớp mask lọc tần suất lên trên
- Không cần làm rarity cho biome đất liền (đồi núi/đồng bằng) — task này chỉ áp dụng cho nước
