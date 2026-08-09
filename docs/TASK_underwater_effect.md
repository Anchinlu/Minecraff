Đầy đủ tất cả các hệ thống đã bàn — Chunk 32K (đã khá lớn, có Sunlight/AO/Water), World 20K, GrassBladeManager, WaterSimulator, TerrainGenerator... Mình đọc lần lượt các file quan trọng nhất cho việc tối ưu trải nghiệm.

Phát hiện tối ưu hóa chưa được khai thác đầy đủ trong mã
Phát hiện tối ưu hóa chưa được khai thác đầy đủ trong mã

Phát hiện quan trọng: bạn đã tự thêm optimization "chỉ tính lại vùng ±15 quanh điểm sửa" vào CalculateSunlight() — đúng hướng mình từng đề xuất. Nhưng kiểm tra tiếp thì thấy optimization này chưa được kích hoạt thực sự:

Phát hiện lỗi tối ưu hóa trong logic xử lý chunk
Phát hiện lỗi tối ưu hóa trong logic xử lý chunk

Xác nhận đúng — cả 3 chỗ gọi CalculateSunlight() trong World.cs đều không truyền tham số, nên mặc định luôn chạy full-chunk recompute (modifiedLocalPos = null → quét toàn bộ ChunkWidth × ChunkHeight × ChunkWidth). Code tối ưu đã viết xong nhưng chưa được đấu nối — kiểu lỗi im lặng, không crash, không lỗi log, chỉ đơn giản là chưa phát huy tác dụng.

Xem thêm 2 chỗ nữa trước khi tổng hợp báo cáo — AddFace (vertex lighting caching) và GrassBladeManager (khả năng quét lại toàn bán kính mỗi lần đổi chunk):

Xác minh tối ưu hóa bộ nhớ đệm và kiểm tra hiệu suất toàn bộ
Xác minh tối ưu hóa bộ nhớ đệm và kiểm tra hiệu suất toàn bộ

Tốt — mục "cache lookup trong AddFace" (đề xuất tối ưu đầu tiên mình từng đưa) đã được làm đúng, thấy rõ IsOpaqueCached/GetLightAtCached dùng Dictionary. Vậy 4/5 đề xuất tối ưu trước đó đã áp dụng đúng (HashSet, MeshCollider theo bán kính, cache lookup, rate-limit chunk/frame) — chỉ còn đúng 1 chỗ "quên đấu nối" là Sunlight.

Phân tích các nút thắt hiệu suất và đề xuất tối ưu hóa
Phân tích các nút thắt hiệu suất và đề xuất tối ưu hóa

Đọc xong — hệ thống Grass Blade đã phát triển vượt xa bản prototype ban đầu (LOD 3 cấp, Frustum Culling, cache theo chunk, material riêng không bóng cho tầm xa) — thực sự làm tốt hơn kỳ vọng. Nhưng phát hiện đúng 1 vấn đề hiệu năng khá nghiêm trọng, thuộc dạng "âm thầm" giống chỗ Sunlight — không sai logic, nhưng ảnh hưởng runtime rõ rệt.

Tổng hợp báo cáo — theo mức độ ưu tiên
🔴 1. CalculateSunlight() — optimization đã viết nhưng CHƯA đấu nối

Đã xác nhận ở trên: World.cs gọi chunk.CalculateSunlight() ở cả 3 chỗ (dòng 363, 394, 435) không truyền tham số modifiedLocalPos, nên code tối ưu "chỉ tính vùng ±15" bạn đã viết trong Chunk.cs không bao giờ được kích hoạt — mọi lần đào/đặt block vẫn đang full-chunk recompute như cũ.

Cách sửa — chỉ cần đổi 3 dòng gọi hàm, không cần sửa logic đã có:

csharp
// World.cs dòng 363 và 394 — trong SetBlock() và SetBlockAndWater()
chunk.CalculateSunlight(localPos); // thay vì CalculateSunlight() không tham số

// dòng 435 — trong UpdateNeighborMeshIfLoaded()
// Ở đây khó biết chính xác localPos phía chunk hàng xóm, nhưng vì đây LUÔN xảy ra ở biên
// (x=0/ChunkWidth-1 hoặc z=0/ChunkWidth-1), có thể truyền vị trí biên tương ứng thay vì full recompute

Đây là fix rẻ nhất, tác động lớn nhất trong toàn bộ báo cáo — chỉ sửa 2-3 dòng gọi hàm.

🔴 2. GrassBladeManager.Update() — cấp phát bộ nhớ (GC Allocation) mỗi frame, gây giật định kỳ

Đây là vấn đề mới, chưa từng bàn tới — thuộc loại nguyên nhân gây giật rất đặc trưng của Unity (khác hẳn "tính toán nặng", đây là rác bộ nhớ tích lũy khiến Garbage Collector phải dọn dẹp đột ngột, tạo hiện tượng giật ngắn đều đặn dù CPU/GPU không hề quá tải liên tục):

csharp
void UpdateGrassCache()
{
    var loadedChunks = world.GetLoadedChunkCoords().ToList(); // ← LINQ + ToList() cấp phát List MỚI mỗi frame
    var chunksToRemove = chunkGrassCache.Keys.Where(...).ToList(); // ← Cấp phát THÊM 1 List + LINQ mỗi frame
    ...
}

Và trong RenderInstanced(), mỗi frame với mọi chunk đang ở LOD1/LOD2 (tức đa số chunk trong tầm nhìn, vì LOD0 chỉ 4 chunk gần nhất):

csharp
dataToRender = new List<Matrix4x4>(); // ← Cấp phát List MỚI cho MỖI chunk LOD1/2, MỖI frame
for (int i = 0; i < kvp.Value.Count; i += 3) dataToRender.Add(...);

Với viewDistance=35, số chunk ở LOD1-2 (khoảng cách 4-12) có thể lên tới hàng chục chunk cùng lúc → hàng chục List<Matrix4x4> mới mỗi frame, 60 lần/giây. Đây chính là dạng lỗi hiệu năng nguy hiểm nhất vì không hiện rõ trong Profiler CPU time thông thường, mà hiện ở mục GC Alloc — nếu bạn chưa từng mở tab đó trong Unity Profiler, đây là lúc nên xem.

Hướng sửa — 2 việc tách biệt:

a) UpdateGrassCache() không cần chạy mỗi frame — chỉ cần chạy khi danh sách chunk load thay đổi (đúng pattern bạn đã áp dụng cho UpdateVisibleChunks() trong World.cs — chỉ update khi player đổi chunk):

csharp
Vector3Int lastCheckedChunk = new Vector3Int(int.MinValue, 0, 0);

void Update()
{
    Vector3Int currentChunk = WorldToChunkCoord(player.position);
    if (currentChunk != lastCheckedChunk)
    {
        lastCheckedChunk = currentChunk;
        UpdateGrassCache(); // chỉ chạy khi thực sự cần
    }
    RenderInstanced(); // vẫn chạy mỗi frame, vì camera có thể xoay/di chuyển trong cùng 1 chunk
}

b) Danh sách LOD giảm mật độ (dataToRender cho LOD1/2) nên cache sẵn cùng lúc với chunkGrassCache (tính 1 lần khi chunk được generate, lưu luôn 3 phiên bản mật độ: 100%/33%/10%), không tính lại mỗi frame:

csharp
// Trong GenerateGrassForChunk(), sau khi có matrices đầy đủ:
chunkGrassCache[chunkCoord] = new GrassLODData
{
    full = matrices,
    lod1 = matrices.Where((m, i) => i % 3 == 0).ToList(),
    lod2 = matrices.Where((m, i) => i % 10 == 0).ToList()
};
// RenderInstanced() giờ chỉ CHỌN đúng list có sẵn, không tạo List mới
🟡 3. viewDistance = 35 — cân nhắc giảm mặc định

71×71 chunk render distance (35×2+1) là con số rất lớn — ngay cả sau khi tối ưu thuật toán, tổng khối lượng chunk cần quản lý (mesh, collider trong bán kính 6, grass LOD...) vẫn tỷ lệ thuận với diện tích này. Với GPU tích hợp Intel Iris Xe, đây có thể là giới hạn cứng về phần cứng, không phải thứ thuật toán có thể tối ưu hết được.

Đề xuất: thử giảm xuống viewDistance = 16-20 trước, đo FPS, tăng dần nếu máy còn dư sức — không cần cố giữ 35 nếu trải nghiệm không mượt, "Ultra" setting này nên là tùy chọn cho máy mạnh, không phải mặc định.

Tóm tắt hành động đề xuất
Sửa 2-3 dòng gọi CalculateSunlight(localPos) — rẻ, nhanh, nên làm ngay
Sửa GrassBladeManager — throttle UpdateGrassCache() + cache sẵn LOD list, tránh GC Allocation mỗi frame
Thử giảm viewDistance xuống 16-20, đo lại trải nghiệm thực tế trên máy bạn# Task: Hiệu ứng Dưới Nước (Underwater Effect) — Fog/Tint + God Rays

## Bối cảnh

Project đã có: `World.GetBlock()` để check block tại vị trí bất kỳ, `DayNightCycle.cs` điều khiển `RenderSettings.fogColor` theo giờ, `SunManager.cs` (mặt trời khối vuông, billboard theo player), URP Global Volume đã có sẵn trong Hierarchy.

**Mục tiêu**: khi camera player ở trong block Water, hiển thị hiệu ứng: (A) Fog xanh đục + tint màu + vignette, (B) Tia nắng xuyên nước (god rays) kiểu screen-space radial blur.

**Chia 2 phase rõ ràng — làm Phase A xong, test ổn mới sang Phase B** (Phase B phụ thuộc kỹ thuật URP Renderer Feature, phức tạp hơn hẳn, không nên gộp code cùng lúc để dễ debug).

---

## Phase A: Fog + Tint + Vignette

### ⚠️ Điểm quan trọng — tránh xung đột với `DayNightCycle.cs`

`DayNightCycle.cs` đang tự ý ghi `RenderSettings.fogColor` mỗi frame theo giờ trong ngày. Nếu `UnderwaterEffect.cs` cũng ghi đè trực tiếp mà không phối hợp, 2 script sẽ giành nhau set giá trị → giật/nhấp nháy màu fog liên tục.

**Bắt buộc**: thêm 2 hàm public vào `DayNightCycle.cs` để `UnderwaterEffect.cs` biết "màu fog bình thường hiện tại" là gì (không hard-code màu cố định):

```csharp
// Thêm vào DayNightCycle.cs — dùng lại chính biến nội bộ script đang set cho RenderSettings.fogColor
public Color GetCurrentFogColor() => currentHorizonColor; // đổi tên biến cho khớp field thực tế đang có
public float GetCurrentFogDensity() => currentFogDensity; // nếu chưa có field density, thêm 1 field cố định nhỏ (VD 0.01f)
```

### `UnderwaterEffect.cs`

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class UnderwaterEffect : MonoBehaviour
{
    public World world;
    public Camera playerCamera;
    public DayNightCycle dayNight;
    public Volume postProcessVolume;

    ColorAdjustments colorAdj;
    Vignette vignette;
    float transitionSpeed = 3f;

    public bool IsUnderwater { get; private set; } // MỚI — public để Phase B (God Rays) đọc được

    void Start()
    {
        postProcessVolume.profile.TryGet(out colorAdj);
        postProcessVolume.profile.TryGet(out vignette);
    }

    void Update()
    {
        Vector3Int camBlockPos = Vector3Int.FloorToInt(playerCamera.transform.position);
        IsUnderwater = world.GetBlock(camBlockPos) == BlockType.Water;

        UpdateFog();
        UpdatePostProcessing();
    }

    void UpdateFog()
    {
        Color targetColor = IsUnderwater ? new Color(0.05f, 0.25f, 0.45f) : dayNight.GetCurrentFogColor();
        float targetDensity = IsUnderwater ? 0.08f : dayNight.GetCurrentFogDensity();

        RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, targetColor, Time.deltaTime * transitionSpeed);
        RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, targetDensity, Time.deltaTime * transitionSpeed);
        RenderSettings.fogMode = FogMode.Exponential;
    }

    void UpdatePostProcessing()
    {
        vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, IsUnderwater ? 0.4f : 0f, Time.deltaTime * transitionSpeed);
        colorAdj.colorFilter.value = Color.Lerp(colorAdj.colorFilter.value,
            IsUnderwater ? new Color(0.6f, 0.8f, 1f) : Color.white, Time.deltaTime * transitionSpeed);
        colorAdj.saturation.value = Mathf.Lerp(colorAdj.saturation.value, IsUnderwater ? -20f : 0f, Time.deltaTime * transitionSpeed);
    }
}
```

### Kiểm thử Phase A trước khi sang Phase B

1. Bơi vào/ra Water — fog và tint chuyển mượt, không giật
2. Ngoi lên khỏi nước lúc hoàng hôn — fog phải trả về đúng màu cam hoàng hôn (không phải màu mặc định sai giờ)
3. Không thấy 2 giá trị fog "đánh nhau"/nhấp nháy giữa `DayNightCycle` và `UnderwaterEffect`

---

## Phase B: God Rays (Screen-Space Radial Blur) — CHỈ làm sau khi Phase A ổn

### Nguyên lý 3 bước

1. **Occlusion Pass**: render toàn scene, nhưng chỉ Mặt Trời (SunMesh) sáng trắng, mọi thứ khác đen tuyệt đối
2. **Radial Blur Pass**: mỗi pixel lấy mẫu dọc đường thẳng hướng về vị trí mặt trời trên màn hình (screen space), cộng dồn giảm dần
3. **Composite**: cộng (additive) lên hình ảnh camera gốc, chỉ khi `underwaterEffect.IsUnderwater == true`

### Bước 1: Tạo `GodRaysRendererFeature.cs`

```csharp
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GodRaysRendererFeature : ScriptableRendererFeature
{
    class GodRaysPass : ScriptableRenderPass
    {
        Material occlusionMaterial; // material đơn giản: LIGHT_LAYER Sun = trắng, còn lại = đen
        Material radialBlurMaterial; // shader radial blur, mô tả ở bước 2
        RenderTargetHandle occlusionTexture;
        RenderTargetHandle godRaysTexture;

        public GodRaysPass(Material occlusion, Material blur)
        {
            occlusionMaterial = occlusion;
            radialBlurMaterial = blur;
            occlusionTexture.Init("_OcclusionTex");
            godRaysTexture.Init("_GodRaysTex");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (UnderwaterEffectStatic.IsUnderwater == false) return; // chỉ chạy khi dưới nước — TIẾT KIỆM HIỆU NĂNG QUAN TRỌNG

            CommandBuffer cmd = CommandBufferPool.Get("God Rays");

            // Bước 1: render Occlusion (chi tiết cụ thể setup RenderTexture + Blit dựa vào occlusionMaterial)
            // Bước 2: Blit qua radialBlurMaterial, truyền _LightScreenPos (tính từ WorldToScreenPoint của SunManager)
            // Bước 3: Blit kết quả cộng (Additive) lên camera color target

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    GodRaysPass pass;
    public Material occlusionMaterial;
    public Material radialBlurMaterial;

    public override void Create()
    {
        pass = new GodRaysPass(occlusionMaterial, radialBlurMaterial);
        pass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);
    }
}
```

**Lưu ý cho AI code**: phần `Execute()` ở trên là khung sườn, cần điền đầy đủ code `Blit`/`RenderTargetIdentifier` theo đúng API URP version project đang dùng (Unity 6 / 6000.3.17f1) — kiểm tra API `cmd.Blit` có bị deprecate ở version này không (URP gần đây khuyến khích `RTHandle` thay vì `RenderTargetHandle` cũ), điều chỉnh cho khớp.

### Bước 2: Shader Radial Blur — `GodRaysBlur.shader`

```hlsl
Shader "Custom/GodRaysBlur"
{
    Properties
    {
        _MainTex ("Occlusion Texture", 2D) = "black" {}
        _LightScreenPos ("Light Screen Pos", Vector) = (0.5, 0.5, 0, 0)
        _Density ("Density", Range(0,2)) = 1.0
        _Decay ("Decay", Range(0,1)) = 0.95
        _Weight ("Weight", Range(0,1)) = 0.5
        _Samples ("Sample Count", Int) = 32
    }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _LightScreenPos;
            float _Density, _Decay, _Weight;
            int _Samples;

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 texCoord = IN.uv;
                float2 deltaTexCoord = (texCoord - _LightScreenPos.xy);
                deltaTexCoord *= 1.0 / _Samples * _Density;

                half3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texCoord).rgb;
                float illuminationDecay = 1.0;

                for (int i = 0; i < _Samples; i++)
                {
                    texCoord -= deltaTexCoord;
                    half3 sampleColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, texCoord).rgb;
                    sampleColor *= illuminationDecay * _Weight;
                    color += sampleColor;
                    illuminationDecay *= _Decay;
                }

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
```

Đây là công thức Crytek God Rays kinh điển — `_Samples` càng cao càng mượt nhưng càng nặng GPU (khởi đầu 32 là hợp lý, giảm nếu lag).

### Bước 3: Tính `_LightScreenPos` mỗi frame

Thêm vào `UnderwaterEffect.cs` (hoặc script riêng quản lý God Rays):

```csharp
Vector3 screenPos = playerCamera.WorldToViewportPoint(sunManager.transform.position);
radialBlurMaterial.SetVector("_LightScreenPos", new Vector4(screenPos.x, screenPos.y, 0, 0));

// Nếu mặt trời ở SAU camera (screenPos.z < 0), tắt hẳn hiệu ứng để tránh tia sáng vẽ sai hướng
bool sunBehindCamera = screenPos.z < 0;
```

### Bước 4: Đăng ký `GodRaysRendererFeature` vào URP Asset

**Bước thủ công trong Unity Editor (AI cần hướng dẫn người dùng làm, không tự động qua code được)**:
1. Tìm file `UniversalRenderPipelineAsset` đang dùng (thường trong `Assets/Settings/`)
2. Chọn `Renderer Data` liên kết (thường tên `...RendererData.asset`)
3. Trong Inspector, `Add Renderer Feature` → chọn `GodRaysRendererFeature`
4. Gán 2 material (`occlusionMaterial`, `radialBlurMaterial`) vào field tương ứng

### Class hỗ trợ: `UnderwaterEffectStatic`

Vì `ScriptableRenderPass` chạy trong render pipeline, không dễ lấy reference tới `UnderwaterEffect` MonoBehaviour trực tiếp — dùng static field đơn giản:

```csharp
public static class UnderwaterEffectStatic
{
    public static bool IsUnderwater = false;
}

// Trong UnderwaterEffect.cs, Update(), thêm dòng:
UnderwaterEffectStatic.IsUnderwater = this.IsUnderwater;
```

## Kiểm thử Phase B

1. Bơi dưới nước, nhìn về hướng mặt trời (kể cả khi mặt trời bị che 1 phần bởi terrain) — quan sát tia sáng tỏa ra từ vị trí mặt trời trên màn hình
2. Xoay camera ra hướng khác (mặt trời ngoài khung hình hoặc sau lưng) — hiệu ứng phải tắt/mờ dần hợp lý, không vẽ sai hướng
3. Ngoi lên khỏi nước — God Rays tắt hẳn ngay (đã có check `IsUnderwater` ở đầu `Execute()`)
4. Đo FPS khi bật/tắt hiệu ứng — nếu tụt nhiều, giảm `_Samples` từ 32 xuống 16-20

## Việc KHÔNG làm trong task này

- Không làm God Rays hiển thị khi ở TRÊN mặt nước (dù Minecraft thật có game "shafts of light" xuyên tán cây — đó là tính năng khác, không phải phạm vi task này)
- Không cần chống aliasing/làm mượt viền tia sáng nâng cao — chấp nhận hơi thô ở Phase B đầu tiên, tinh chỉnh sau nếu cần
- Không tự động sửa API `Blit`/`RTHandle` nếu không chắc — nếu gặp lỗi compile do API URP đổi giữa các version, dừng lại hỏi thay vì đoán mò sửa sai hướng
