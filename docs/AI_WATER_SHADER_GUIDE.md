# Hướng dẫn AI: xây dựng shader nước voxel đẹp và ổn định

## 1. Mục tiêu

Hãy nâng cấp shader nước hiện tại của dự án Minecraft voxel Unity thành một shader có hình ảnh đẹp, ổn định và phù hợp với kiến trúc đang có.

Các yêu cầu ưu tiên:

1. Mặt nước phẳng, không bị rách tại ranh giới giữa các chunk.
2. Có chuyển động mặt nước bằng normal/procedural noise, nhưng không làm thay đổi topology mesh.
3. Có phản chiếu bầu trời, ánh sáng mặt trời và phản xạ Fresnel hợp lý.
4. Nước nông nhìn trong hơn; nước sâu có màu đậm hơn.
5. Có hiệu ứng bọt nhẹ ở bờ nước hoặc nơi độ sâu nhỏ.
6. Hình ảnh ổn định ở xa, không nhấp nháy, không bị viền trắng/đen và không bị sorting quá rõ.
7. Không làm vỡ hệ thống `World`, `Chunk`, `WaterSimulator`, day/night và các material hiện có.
8. Có tham số dễ tinh chỉnh trong Inspector và có fallback an toàn khi một texture hoặc feature URP không khả dụng.

Đây là dự án học tập, vì vậy mọi thay đổi phải ưu tiên tính dễ hiểu, khả năng debug và hiệu năng thực tế hơn là một shader quá phức tạp.

## 2. Bối cảnh kiến trúc hiện tại

Đọc các file sau trước khi sửa:

- `Assets/Shaders/VoxelWater.shader`
- `Assets/Scripts/World/Chunk.cs`
- `Assets/Scripts/World/World.cs`
- `Assets/Scripts/World/WaterSimulator.cs`
- `Assets/Scripts/GameSetup.cs`
- `Assets/Scripts/World/DayNightCycle.cs`

Luồng dữ liệu hiện tại:

```text
TerrainGenerator
    -> Chunk.blocks / Chunk.waterLevel
    -> Chunk.GenerateMesh()
    -> Mesh submesh 2
    -> Material matWater
    -> Custom/VoxelWater
```

`Chunk.GenerateMesh()` tạo ba submesh:

- Submesh 0: block opaque.
- Submesh 1: grass texture.
- Submesh 2: water.

`World` gán material theo thứ tự:

```csharp
meshRenderer.materials = new Material[]
{
    matVertexColor,
    matGrassTexture,
    matWater
};
```

Vì vậy, không được đổi thứ tự submesh và không được biến nước thành một GameObject riêng cho từng block.

## 3. Nguyên tắc bắt buộc

### 3.1 Không displacement hình học trong vertex shader

Không di chuyển vertex theo thời gian trong `vert`. Mesh nước hiện tại được chia theo block và chunk; displacement sẽ tạo:

- rách mặt nước ở biên chunk;
- khe hở giữa cột nước liền kề;
- sai lệch collider và raycast;
- flicker khi hai mặt có độ cao gần nhau.

Chuyển động phải được giả lập bằng normal, roughness, màu và specular trong fragment shader. Nếu cần gợn sóng hình học trong tương lai, phải thiết kế lại mesh nước riêng, có seam stitching ở biên chunk; không tự ý thêm vào shader hiện tại.

### 3.2 Không dùng screen-space reflection như yêu cầu bắt buộc

Screen-space reflection dễ mất phản chiếu khi tia phản xạ đi ra ngoài màn hình, phụ thuộc depth/opaque texture và thường gây nhiễu ở vùng nước trong suốt. Bản đầu tiên nên dùng:

- `SampleSH(reflectionVector)` cho ánh sáng môi trường;
- màu bầu trời từ sky/gradient hiện tại;
- highlight mặt trời bằng GGX hoặc Blinn-Phong có giới hạn cường độ;
- Fresnel để điều khiển tỷ lệ phản chiếu.

Nếu sau này thêm SSR, phải có keyword bật/tắt và fallback về sky reflection.

### 3.3 Alpha phải được kiểm soát

Không ép alpha quá cao ở mọi khoảng cách. Alpha cần phụ thuộc:

- độ sâu phía sau mặt nước;
- góc nhìn, thông qua Fresnel;
- độ xa camera;
- foam và vùng nước rất nông.

Tránh alpha bằng 1 cho toàn bộ mặt nước vì nó làm mất cảm giác trong suốt. Tuy nhiên, ở góc nhìn lướt sát mặt nước, Fresnel có thể làm mép nước đục hơn.

## 4. Thiết kế shader đề xuất

Giữ shader tên `Custom/VoxelWater` để `GameSetup.cs` và material hiện tại tiếp tục hoạt động.

### 4.1 Properties nên có

Thêm các property có giá trị mặc định an toàn:

```text
_ShallowColor       màu nước nông
_DeepColor          màu nước sâu
_FoamColor          màu bọt
_BaseAlpha          alpha cơ bản
_DepthFadeDistance  khoảng cách để nước chuyển từ nông sang sâu
_RefractionStrength độ lệch màu rất nhẹ, mặc định thấp
_ReflectionStrength cường độ phản chiếu
_FresnelPower       độ gắt của Fresnel
_Smoothness         độ bóng
_NormalStrength     cường độ normal giả lập
_WaveScale          kích thước sóng
_WaveSpeed          tốc độ chuyển động
_FoamDistance       khoảng cách tạo foam ở bờ
_FogStrength        ảnh hưởng của fog trong nước
```

Không hard-code toàn bộ màu và cường độ như shader hiện tại. Các giá trị mặc định phải tạo ra hình ảnh chấp nhận được ngay cả khi material chưa được tinh chỉnh.

### 4.2 Normal động

Dùng hai hoặc ba lớp value noise/hash noise có hướng chuyển động khác nhau:

- lớp lớn: sóng chậm, scale thấp;
- lớp trung bình: chuyển động chính;
- lớp nhỏ: chỉ tạo highlight nhỏ, không được gây nhiễu mạnh.

Trộn các lớp thành `float2 bump`, sau đó tạo normal ổn định:

```hlsl
float3 waterNormal = normalize(float3(bump.x * _NormalStrength, 1.0, bump.y * _NormalStrength));
```

Giữ normal hướng lên. Không để bump làm normal quay xuống dưới hoặc tạo phản xạ ngược mạnh. Có thể dùng:

```hlsl
waterNormal.y = max(waterNormal.y, 0.35);
waterNormal = normalize(waterNormal);
```

Tốc độ sóng nên dùng `_Time.y * _WaveSpeed`, không dùng delta time tự quản lý trong shader.

### 4.3 Tính độ sâu

Dùng `_CameraDepthTexture` qua `DeclareDepthTexture.hlsl` và kiểm tra depth texture có giá trị hợp lệ.

```hlsl
float2 screenUV = IN.positionHCS.xy / _ScaledScreenParams.xy;
float rawSceneDepth = SampleSceneDepth(screenUV);
float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
float waterEyeDepth = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);
float depthDifference = max(0.0, sceneEyeDepth - waterEyeDepth);
float depth01 = saturate(depthDifference / max(_DepthFadeDistance, 0.001));
```

Nếu depth texture không khả dụng, phải fallback về `depth01 = 1.0` thay vì trả về màu đen hoặc tạo NaN.

Màu cơ bản:

```hlsl
float3 waterColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
```

Nước nông phải sáng và trong hơn; nước sâu phải đậm và ít nhìn thấy nền phía sau hơn.

### 4.4 Fresnel và phản chiếu

Dùng view direction và normal động:

```hlsl
float NdotV = saturate(dot(waterNormal, viewDir));
float fresnel = pow(1.0 - NdotV, max(_FresnelPower, 0.01));
float reflectionAmount = saturate(fresnel * _ReflectionStrength);
```

Phản chiếu nên gồm:

1. `SampleSH(reflectionVector)` cho ambient/sky.
2. Màu sky gradient nếu có thể lấy từ biến hoặc material hiện tại.
3. Sun specular có cường độ bị giới hạn.

Không để specular vượt quá khoảng 1–2 lần màu HDR nếu chưa có tone mapping rõ ràng. Các công thức GGX hiện tại cần được kiểm tra lại ở các trường hợp `dotLH` gần 0 để tránh chia cho số quá nhỏ.

Luôn dùng epsilon:

```hlsl
float safeDenom = max(dotLH * dotLH, 0.0001);
```

### 4.5 Ánh sáng mặt trời và ngày/đêm

Shader có thể tiếp tục đọc các global variable đang được `DayNightCycle` cung cấp:

```text
_SunVisibility
_SunVisibility2
_SunFactor
_NoonFactor
_NightFactor
_ShadowTime
_RainFactor
```

Không giả định các biến này luôn tồn tại với giá trị hợp lệ. Cần thiết kế sao cho giá trị mặc định bằng 0 hoặc 1 vẫn không làm shader đen hoàn toàn.

Sun highlight nên giảm khi:

- ban đêm;
- trời mưa;
- mặt nước quay lưng với nguồn sáng.

### 4.6 Foam

Foam chỉ nên xuất hiện ở vùng nông hoặc mép giao nhau với địa hình. Dùng `depth01`:

```hlsl
float shallowMask = 1.0 - smoothstep(0.0, _FoamDistance, depthDifference);
```

Kết hợp thêm noise động rất nhẹ để foam không phải một đường viền tĩnh. Không dùng `frac(positionWS.y)` làm điều kiện chính vì nó phụ thuộc vị trí block và tạo pattern lặp theo từng block.

```hlsl
float foamNoise = smoothNoise2D(IN.positionWS.xz * 0.08 + _Time.y * 0.02).x;
float foam = shallowMask * smoothstep(0.45, 0.75, foamNoise);
```

Foam phải được clamp và giới hạn alpha, tránh phủ trắng toàn bộ hồ.

### 4.7 Refraction và màu nền

Chỉ thêm refraction nhẹ nếu URP asset hiện tại hỗ trợ opaque texture. Không được coi `_CameraOpaqueTexture` là luôn sẵn có.

Nếu opaque texture không bật:

- bỏ qua refraction;
- giữ water color + depth fade;
- không sample texture null.

Refraction nên nhỏ, khoảng 0.005–0.02 UV. Refraction quá mạnh làm nền rung, đặc biệt ở khoảng cách xa và trên mặt chunk.

### 4.8 Fog và khoảng cách

Áp dụng fog sau khi trộn nước, phản chiếu và foam:

```hlsl
finalColor = MixFog(finalColor, IN.fogFactor);
```

Ở xa, giảm chi tiết normal và foam theo `fogFactor` hoặc khoảng cách camera để giảm shimmer. Không làm thay đổi silhouette của mesh.

## 5. Render state khuyến nghị

Bắt đầu với cấu hình ổn định sau:

```text
Queue       Transparent
RenderType  Transparent
Blend       SrcAlpha OneMinusSrcAlpha
ZWrite      Off
Cull        Back
```

Không đổi sang `ZWrite On` nếu chưa kiểm tra sorting giữa nhiều mặt nước và terrain. Nếu cần ổn định sorting, ưu tiên sửa material/render queue hoặc tách water pass, không tự ý bật depth write cho mọi mặt nước.

Nếu cần hiển thị nước từ mặt dưới, có thể cân nhắc `Cull Off`, nhưng phải đánh giá chi phí fill-rate và hiện tượng mặt nước bị nhìn xuyên hai lần. Mặc định giữ `Cull Back`.

## 6. Tương thích với Chunk và WaterSimulator

Không thay đổi các nguyên tắc sau:

- Water vẫn nằm trong submesh 2.
- `Chunk` vẫn tạo mặt nước phẳng bằng dữ liệu `waterLevel`.
- `WaterSimulator` vẫn chịu trách nhiệm dòng chảy logic; shader chỉ chịu trách nhiệm hình ảnh.
- Không dùng shader để quyết định block nào là Water.
- Không thêm material instance cho từng chunk nếu không cần thiết.
- Không gọi `RebuildMesh()` từ shader hoặc từ logic animation.

Nếu cần truyền thêm dữ liệu, ưu tiên dùng:

1. vertex color/UV đã có;
2. global shader parameters;
3. material properties dùng chung.

Không thêm vertex stream mới nếu chưa cập nhật đầy đủ `Chunk.AddFace()` và tất cả mesh path.

## 7. Checklist triển khai

AI phải thực hiện theo thứ tự:

1. Đọc shader và các file kiến trúc liên quan.
2. Giữ nguyên shader name `Custom/VoxelWater`.
3. Refactor shader thành các hàm nhỏ: `GetWaterNormal`, `GetDepthFade`, `GetFresnel`, `GetSunSpecular`, `GetFoam`.
4. Thêm properties có tên rõ ràng và giá trị mặc định an toàn.
5. Thêm epsilon cho mọi phép chia có nguy cơ bằng 0.
6. Giữ vertex position không đổi.
7. Kiểm tra alpha ở góc nhìn thẳng và góc nhìn lướt.
8. Kiểm tra mặt nước tại biên giữa hai chunk.
9. Kiểm tra nước nông, nước sâu, nước chảy level 1–7 và source level 8.
10. Kiểm tra ngày, đêm, mưa và fog.
11. Kiểm tra camera gần mặt nước và camera nhìn từ dưới nước.
12. Kiểm tra khi `CameraDepthTexture` hoặc opaque texture không khả dụng.
13. Kiểm tra không phát sinh lỗi shader compile trên Unity `6000.3.17f1`.
14. Chỉ sau khi hình ảnh ổn định mới tinh chỉnh thêm noise/foam/refraction.

## 8. Tiêu chí nghiệm thu hình ảnh

Shader chỉ được coi là đạt khi:

- không có đường rách giữa các chunk;
- mặt nước không nhấp nháy khi camera di chuyển;
- sóng chuyển động rõ nhưng không giống nhiễu TV;
- phản chiếu mạnh hơn ở góc nhìn ngang và yếu hơn khi nhìn thẳng xuống;
- nước nông phân biệt được đáy, nước sâu chuyển sang màu đậm;
- foam chỉ xuất hiện ở vùng hợp lý;
- không có viền sáng bất thường quanh từng block;
- không có NaN, toàn màn hình trắng/đen hoặc artifact khi nhìn ra ngoài map;
- hiệu năng vẫn chấp nhận được khi có nhiều chunk nước cùng lúc.

## 9. Các lỗi không được mắc phải

- Không displacement vertex để tạo sóng trong phiên bản này.
- Không ép alpha = 1 cho toàn bộ nước.
- Không dùng SSR bắt buộc không có fallback.
- Không sample `_CameraOpaqueTexture` khi chưa kiểm tra khả dụng.
- Không chia cho `dotLH`, `depthDifference` hoặc roughness mà không có epsilon.
- Không tạo material mới mỗi frame.
- Không đổi cấu trúc submesh của `Chunk` chỉ để làm shader đẹp hơn.
- Không thêm dependency ngoài hoặc asset ngoài khi chưa được yêu cầu.
- Không tối ưu bằng cách bỏ depth fade, fog hoặc shadow mà không đo lại hình ảnh.

## 10. Kết quả cần bàn giao

Sau khi thực hiện, AI phải báo cáo:

1. Các file đã thay đổi.
2. Các property mới của shader và ý nghĩa từng property.
3. Cách shader xử lý normal động, depth, Fresnel, reflection, foam và fog.
4. Những fallback khi URP không có depth/opaque texture.
5. Kiểm tra tương thích với `Chunk`, `World` và `WaterSimulator`.
6. Các bước test trong Unity Editor.
7. Nếu chưa thể chạy Unity Editor, phải nói rõ phần nào chưa được xác minh thực tế.
