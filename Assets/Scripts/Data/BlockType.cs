/// <summary>
/// Định nghĩa các loại block trong thế giới voxel.
/// Air = block rỗng (không vẽ), dùng để xác định face nào cần render.
/// </summary>
public enum BlockType
{
    Air = 0,    // Block rỗng — face tiếp giáp Air sẽ được vẽ
    Stone,      // Đá — block chính cho demo giai đoạn 1
    Grass,      // Cỏ — dùng từ giai đoạn 2 (Perlin Noise terrain)
    Dirt        // Đất — dùng từ giai đoạn 2
}
