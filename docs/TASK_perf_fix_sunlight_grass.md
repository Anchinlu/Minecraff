# Task: Fix 2 vấn đề hiệu năng — Sunlight Recompute & Grass GC Allocation

## Bối cảnh

Review code hiện tại (`World.cs`, `Chunk.cs`, `GrassBladeManager.cs`) phát hiện 2 vấn đề hiệu năng "âm thầm" — không lỗi, không crash, nhưng gây stutter/giật khung hình định kỳ, khiến trải nghiệm chunk loading (vốn đã được che bằng Fog đúng cách) cảm giác giật cục không tự nhiên.

---

## Fix 1: `CalculateSunlight()` — optimization đã viết nhưng chưa đấu nối

### Vấn đề

`Chunk.cs` đã có sẵn code tối ưu: `CalculateSunlight(Vector3Int? modifiedLocalPos = null)` — khi có tham số, chỉ tính lại vùng ±15 quanh điểm sửa thay vì toàn chunk. Nhưng `World.cs` đang gọi hàm này **không truyền tham số** ở 3 chỗ, khiến optimization không bao giờ kích hoạt — mọi lần đào/đặt block vẫn full-chunk recompute.

### Sửa

Trong `World.cs`:

```csharp
// Trong SetBlock() — dòng đang là: chunk.CalculateSunlight();
chunk.CalculateSunlight(localPos);

// Trong SetBlockAndWater() — dòng đang là: chunk.CalculateSunlight();
chunk.CalculateSunlight(localPos);
```

Trong `UpdateNeighborMeshIfLoaded()`:

```csharp
private void UpdateNeighborMeshIfLoaded(Vector3Int neighborCoord)
{
    if (chunks.ContainsKey(neighborCoord))
    {
        // Không biết chính xác localPos phía chunk hàng xóm, nhưng sự kiện này LUÔN xảy ra
        // do 1 block đổi ở biên (x=0/ChunkWidth-1 hoặc z=0/ChunkWidth-1) — không cần full recompute
        // toàn bộ ChunkHeight, vẫn có thể giới hạn theo trục Y nếu World.cs biết y-position gốc.
        // Nếu không tiện truyền, ít nhất giữ localPos.x/z về đúng biên tương ứng để giới hạn phạm vi X/Z:
        chunks[neighborCoord].CalculateSunlight(); // xem "Lưu ý" bên dưới trước khi quyết định
    }
}
```

**Lưu ý cho AI code**: hàm `UpdateNeighborMeshIfLoaded` hiện chỉ nhận `neighborCoord`, không có thông tin vị trí Y hay X/Z cụ thể của block vừa đổi bên chunk gốc. Cách xử lý đề xuất — sửa chữ ký hàm để nhận thêm `Vector3Int originalLocalPos`, truyền từ nơi gọi (`SetBlock`/`SetBlockAndWater` đã biết `localPos`), rồi tính lại `localPos` tương ứng phía chunk hàng xóm (đảo trục biên: x=0 ↔ x=ChunkWidth-1). Nếu việc này phức tạp hơn dự kiến khi code thực tế, tạm giữ full recompute cho riêng nhánh "update hàng xóm qua biên" (tần suất xảy ra thấp hơn nhiều so với đào/đặt tại chỗ), ĐỪNG cố ép bằng mọi giá — ưu tiên đúng 2 chỗ chính trong `SetBlock`/`SetBlockAndWater` trước.

### Kiểm thử

1. Đào/đặt block liên tục ở vùng đã load nhiều chunk xung quanh — theo dõi Unity Profiler (CPU) trước/sau fix, thời gian xử lý `CalculateSunlight` phải giảm rõ rệt
2. Xác nhận ánh sáng vẫn cập nhật đúng — không có vùng nào bị "sáng sai" do giới hạn phạm vi tính toán quá hẹp (nếu thấy sai, có thể cần tăng bán kính từ ±15 lên cao hơn)

---

## Fix 2: `GrassBladeManager` — GC Allocation mỗi frame

### Vấn đề A: `UpdateGrassCache()` chạy mỗi frame dù không cần

```csharp
void Update()
{
    ...
    UpdateGrassCache(); // ← chạy 60 lần/giây, dùng LINQ + ToList() cấp phát List mới mỗi lần
    RenderInstanced();
}
```

### Sửa — chỉ chạy khi player đổi chunk (đúng pattern `World.cs` đã dùng cho `UpdateVisibleChunks()`)

```csharp
Vector3Int lastCheckedChunk = new Vector3Int(int.MinValue, 0, 0);

void Update()
{
    if (world == null || grassBladeMaterial == null || bladeMesh == null) return;

    if (grassMaterialNoShadow == null)
    {
        grassMaterialNoShadow = new Material(grassBladeMaterial);
        grassMaterialNoShadow.enableInstancing = true;
        grassMaterialNoShadow.DisableKeyword("_MAIN_LIGHT_SHADOWS");
        grassMaterialNoShadow.DisableKeyword("_MAIN_LIGHT_SHADOWS_CASCADE");
        grassMaterialNoShadow.DisableKeyword("_SHADOWS_SOFT");
    }

    Vector3Int currentChunk = new Vector3Int(
        Mathf.FloorToInt(player.position.x / Chunk.ChunkWidth), 0,
        Mathf.FloorToInt(player.position.z / Chunk.ChunkWidth));

    if (currentChunk != lastCheckedChunk)
    {
        lastCheckedChunk = currentChunk;
        UpdateGrassCache(); // giờ chỉ chạy khi thực sự cần
    }

    RenderInstanced(); // vẫn chạy mỗi frame — camera có thể xoay/di chuyển trong cùng 1 chunk, cần render liên tục
}
```

### Vấn đề B: `RenderInstanced()` tạo `List<Matrix4x4>` mới mỗi frame cho mọi chunk LOD1/LOD2

```csharp
// Hiện tại — chạy lại mỗi frame cho MỌI chunk đang ở LOD1/LOD2 (đa số chunk trong tầm nhìn)
dataToRender = new List<Matrix4x4>();
for (int i = 0; i < kvp.Value.Count; i += 3) dataToRender.Add(kvp.Value[i]);
```

### Sửa — tính sẵn 3 phiên bản mật độ MỘT LẦN khi generate chunk, không tính lại mỗi frame

Đổi cấu trúc lưu trữ từ `Dictionary<Vector3Int, List<Matrix4x4>>` sang struct chứa sẵn cả 3 mức LOD:

```csharp
private struct GrassLODData
{
    public List<Matrix4x4> full;
    public List<Matrix4x4> lod1;
    public List<Matrix4x4> lod2;
}

private Dictionary<Vector3Int, GrassLODData> chunkGrassCache = new Dictionary<Vector3Int, GrassLODData>();
```

Trong `GenerateGrassForChunk()`, sau khi có `matrices` đầy đủ như cũ, thêm tính sẵn 2 list còn lại **một lần duy nhất**:

```csharp
List<Matrix4x4> lod1List = new List<Matrix4x4>();
for (int i = 0; i < matrices.Count; i += 3) lod1List.Add(matrices[i]);

List<Matrix4x4> lod2List = new List<Matrix4x4>();
for (int i = 0; i < matrices.Count; i += 10) lod2List.Add(matrices[i]);

chunkGrassCache[chunkCoord] = new GrassLODData { full = matrices, lod1 = lod1List, lod2 = lod2List };
```

Trong `RenderInstanced()`, thay đoạn tính toán bằng chọn thẳng từ dữ liệu đã cache:

```csharp
List<Matrix4x4> dataToRender;
if (chunkDist <= LOD0_DISTANCE) { mat = grassBladeMaterial; dataToRender = kvp.Value.full; }
else if (chunkDist <= LOD1_DISTANCE) { mat = grassMaterialNoShadow; dataToRender = kvp.Value.lod1; }
else { mat = grassMaterialNoShadow; dataToRender = kvp.Value.lod2; }
// KHÔNG còn new List<>() hay vòng lặp copy nào trong hàm này nữa
```

### Kiểm thử

1. Mở Unity Profiler, tab **GC Alloc** (không phải CPU Time thông thường) — trước fix sẽ thấy spike đều đặn liên quan `GrassBladeManager.Update`, sau fix phải gần như bằng 0 khi đứng yên trong 1 chunk
2. Di chuyển liên tục qua nhiều chunk — xác nhận cỏ vẫn cập nhật đúng LOD theo khoảng cách, không có cỏ "kẹt" sai mật độ
3. So sánh cảm giác chơi tổng thể trước/sau — đặc biệt chú ý cảm giác "giật khi chunk load" có giảm không (đây là mục tiêu thực sự của cả 2 fix trong task này)

## Việc KHÔNG làm trong task này

- Không đổi `viewDistance` — để riêng, người dùng tự điều chỉnh qua Inspector sau khi đánh giá 2 fix này có đủ cải thiện chưa
- Không thêm LOD mới hay thay đổi công thức mật độ cỏ — giữ nguyên hành vi hiển thị, chỉ đổi CÁCH tính toán để tránh cấp phát bộ nhớ thừa
- Không sửa `WaterSimulator` hay các hệ thống khác không liên quan tới 2 vấn đề trên
