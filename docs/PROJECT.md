# PROJECT.md — Minecraft-Clone Unity (Voxel 3D)

> File này dùng để onboard AI assistant (Claude, GPT, Copilot...) vào ngữ cảnh dự án. Đọc file này trước khi hỗ trợ code.

## 1. Mục tiêu dự án

Xây dựng một game voxel 3D kiểu Minecraft trong Unity, mục đích **học kỹ thuật procedural generation, mesh generation và tối ưu hiệu năng**, không nhắm tới sản phẩm thương mại. Ưu tiên: hiểu rõ cơ chế trước, mở rộng tính năng sau.

## 2. Nguyên tắc làm việc

- Build demo nhỏ, xác nhận chạy đúng, rồi mới scale lên (không viết full hệ thống ngay từ đầu)
- Ưu tiên đúng logic trước, tối ưu hiệu năng sau
- Giai đoạn đầu **không dùng asset ngoài** — dùng màu đặc (vertex color / unlit material) thay texture để tập trung vào logic
- Texture atlas thật (pixel art tự vẽ bằng Aseprite) chỉ thêm vào khi core mechanic đã ổn định

## 3. Kiến trúc kỹ thuật

```
World (quản lý toàn bộ chunk đang load)
 └── Chunk (khối 16x128x16 block)
      └── BlockType[,,] — mảng lưu loại block
      └── Mesh (generate động từ block data, KHÔNG phải mỗi block 1 GameObject)
```

**Nguyên lý cốt lõi**: mỗi chunk là 1 mesh duy nhất. Chỉ vẽ mặt (face) của block nào tiếp giáp với Air (face culling). Không tạo GameObject riêng cho từng block.

### Các thành phần chính
| Thành phần | Vai trò |
|---|---|
| `BlockType` (enum) | Định nghĩa loại block: Air, Grass, Dirt, Stone, Wood, Leaves... |
| `Chunk` | Lưu block data + tự generate mesh của chính nó |
| `World` | Quản lý `Dictionary<Vector3Int, Chunk>`, load/unload theo vị trí player |
| `TerrainGenerator` | Sinh địa hình bằng Perlin Noise |
| `PlayerInteraction` | Raycast để đào/đặt block |

## 4. Roadmap theo milestone

- [ ] **Giai đoạn 1 — Mesh cơ bản**: 1 chunk tĩnh 16x16x16 toàn block Stone, generate mesh bằng face culling, dùng màu đặc thay texture. Mục tiêu: xác nhận mesh generation đúng, không lỗi face/normal.
- [ ] **Giai đoạn 2 — World generation**: Thêm Perlin Noise tạo địa hình (Grass/Dirt/Stone theo độ cao). Vẫn dùng màu đặc.
- [ ] **Giai đoạn 3 — Tương tác**: Player controller (CharacterController, FPS view) + raycast đào/đặt block, update lại mesh khi block thay đổi.
- [ ] **Giai đoạn 4 — Multi-chunk**: World quản lý nhiều chunk, load/unload theo khoảng cách player, xử lý mesh ở biên chunk (chunk boundary).
- [x] **Giai đoạn 5 — Texture thật**: Thay màu đặc bằng texture atlas, chia Submeshes, cấu hình Material URP và xử lý triệt để UV bleeding.
- [ ] **Giai đoạn 6 — Tối ưu & mở rộng**: Greedy meshing, save/load block data, inventory system, Job System nếu cần đa luồng.

## 5. Trạng thái hiện tại

- ✅ Giai đoạn 1 hoàn thành: mesh generation (1 mesh/chunk), face culling, 
  cavity test, vertex color (Grass xanh, Stone xám)

- ⚠️ Bài học kỹ thuật: Shader Graph URP (Unlit Shader Graph + node Vertex Color) 
  KHÔNG hiển thị đúng vertex color trong trường hợp này — lý do cụ thể chưa rõ, 
  có thể do cách Unity build vertex stream hoặc setting Graph. Đã chuyển sang 
  custom shader viết tay VoxelVertexColor.shader, hoạt động ổn định. Giữ file 
  shader này lại, sẽ mở rộng thêm UV mapping ở Giai đoạn 5 thay vì quay lại 
  Shader Graph.

- ✅ Giai đoạn 2 hoàn thành: Perlin Noise terrain, chunk 16×128×16,
  phân lớp Grass/Dirt/Stone theo độ sâu, random seed mỗi lần Play

- ✅ Giai đoạn 3 hoàn thành: Player controller (FPS), đào/đặt block bằng raycast, 
  mesh cập nhật realtime. Fix lỗi Input System "Both".

- ✅ Giai đoạn 4 hoàn thành: Multi-chunk, tự động load/unload theo view distance,
  cross-chunk face culling không bị hở mép. Sửa lỗi đệ quy Y-axis.

- ✅ Giai đoạn 5 hoàn thành: Đưa Texture thực tế vào khối Grass bằng cấu trúc 2 Submeshes (vừa giữ được Vertex Color cũ cho Dirt/Stone, vừa chạy shader Texture mới cho Grass).

- ⚠️ Bài học kỹ thuật: **Texture Bleeding (Rác viền) trong Voxel Atlas**:
  Khi dùng chung ảnh texture liền nhau (atlas), Unity thường gây ra lỗi sọc viền ở mép block. Nguyên nhân do **MSAA** (khử răng cưa) và **Bilinear Filter**.
  Cách khắc phục triệt để:
  1. Tắt MSAA trong URP Asset.
  2. Chuyển Texture Filter Mode sang Point (no filter).
  3. Cắt (shrink) UV vào trong hẳn 1 pixel nguyên (`1.0f / chiều_rộng_ảnh`) để ngăn thuật toán Rasterizer lấy nhầm màu của ô bên cạnh. (Tránh lỗi lật texture do tính toán UV mirror ở các mặt đối xứng).

- ✅ Mở rộng 1: Hệ thống Môi trường (Environment).
  - Chu kỳ ngày đêm (`DayNightCycle.cs`) với ánh sáng tự động chuyển màu theo thời gian, mô phỏng chân thực "Golden Hour" giống bộ shader Complementary Unbound.
  - Mây thể tích 3D (`CloudManager.cs`) sinh bằng fBm Noise, tự động trôi theo gió, sử dụng Shader Lit để hiện hình khối mượt mà nhưng vẫn bán trong suốt.
  - Mặt trời Voxel (`SunManager.cs`) cấu tạo từ khối 3D tĩnh, tự động quay quanh người chơi.

- ✅ Mở rộng 2: Nâng cấp Sinh Địa Hình (`TerrainGenerator.cs`).
  - Sử dụng Multi-layer Noise (Biome Noise + Detail Noise) kết hợp SmoothStep để phân chia thế giới thành các vùng Đồng Bằng (rộng, bằng phẳng) xen kẽ với Đồi Núi (cao, nhấp nhô) tự nhiên hơn nhiều so với thuật toán cũ.

- Việc tiếp theo: Giai đoạn 6 — Tối ưu & mở rộng.

## 6. Quy ước code

- C#, Unity 6 (6000.3.17f1) với URP
- Folder structure:
  Assets/Scripts/
  ├── Data/BlockType.cs         — Enum loại block
  ├── World/Chunk.cs            — Block data + mesh generation
  ├── World/World.cs            — Quản lý chunks
  ├── World/TerrainGenerator.cs — Multi-layer Noise phân Biome
  ├── World/DayNightCycle.cs    — Ánh sáng ngày/đêm
  ├── World/CloudManager.cs     — Mây 3D Voxel
  ├── World/SunManager.cs       — Mặt Trời 3D
  └── GameSetup.cs              — Bootstrap scene
  ```
- Style: theo convention hiện có của các project Unity khác của tác giả (xem PROJECT.md của project Pokémon Merge Tower Defense nếu cần đối chiếu)

## 7. Ghi chú cho AI hỗ trợ

- Đây là dự án học tập cá nhân, không phải sản phẩm cần ship gấp — ưu tiên giải thích rõ cơ chế khi đề xuất code, không chỉ đưa code suông
- Khi đề xuất tối ưu (greedy meshing, threading...), giải thích đánh đổi (trade-off) trước khi áp dụng, tránh over-engineer sớm
- Không tự ý thêm asset/dependency ngoài trừ khi được yêu cầu
