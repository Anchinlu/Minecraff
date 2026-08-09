using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Hệ thống mô phỏng nước chảy (Water Simulator).
/// Sử dụng hàng đợi (Queue) và chạy theo dạng Tick (giống Minecraft).
/// </summary>
public class WaterSimulator : MonoBehaviour
{
    private World world;
    
    // Hàng đợi các khối nước cần update
    private Queue<Vector3Int> updateQueue = new Queue<Vector3Int>();
    
    // HashSet để tránh đưa cùng 1 block vào hàng đợi nhiều lần trong 1 tick
    private HashSet<Vector3Int> queuedBlocks = new HashSet<Vector3Int>();

    [Header("Cấu hình Tick")]
    public float tickRate = 0.5f; // Chậm lại: 2 tick / giây (giảm lag và mượt mà hơn)
    private float timer = 0f;

    public void Initialize(World w)
    {
        this.world = w;
    }

    /// <summary>
    /// Thêm 1 block vào hàng đợi để xử lý dòng chảy ở Tick tiếp theo.
    /// Thường được gọi khi: 
    /// - 1 block bên cạnh bị phá
    /// - 1 khối nước lan đến
    /// </summary>
    public void EnqueueWaterUpdate(Vector3Int worldPos)
    {
        if (!queuedBlocks.Contains(worldPos))
        {
            updateQueue.Enqueue(worldPos);
            queuedBlocks.Add(worldPos);
        }
    }

    private void Update()
    {
        if (world == null) return;

        timer += Time.deltaTime;
        if (timer >= tickRate)
        {
            timer = 0f;
            Tick();
        }
    }

    /// <summary>
    /// Xử lý tất cả các block đang nằm trong hàng đợi tại thời điểm này.
    /// </summary>
    private void Tick()
    {
        int count = updateQueue.Count;
        if (count == 0) return;

        // Chỉ lấy ra số lượng block đang có sẵn (không xử lý ngay block vừa bị enqueue trong tick này)
        for (int i = 0; i < count; i++)
        {
            Vector3Int pos = updateQueue.Dequeue();
            queuedBlocks.Remove(pos);

            ProcessWaterBlock(pos);
        }
    }

    /// <summary>
    /// Thuật toán chảy của nước
    /// Level 8: Nguồn cố định
    /// Level 1-7: Nước chảy lan ngang
    /// Level 9: Nước rơi thẳng đứng
    /// </summary>
    private void ProcessWaterBlock(Vector3Int pos)
    {
        BlockType type = world.GetBlock(pos);
        if (type != BlockType.Water) return; 

        byte currentLevel = world.GetWaterLevel(pos);
        if (currentLevel == 0) return;

        // --- BƯỚC 1: RÚT NƯỚC (DECAY) NẾU MẤT NGUỒN ---
        if (currentLevel < 8) 
        {
            bool hasSource = false;
            BlockType upType = world.GetBlock(pos + Vector3Int.up);
            if (upType == BlockType.Water) 
            {
                hasSource = true;
            }
            else
            {
                Vector3Int[] horizontalNeighbors = {
                    pos + Vector3Int.left, pos + Vector3Int.right,
                    pos + new Vector3Int(0, 0, 1), pos + new Vector3Int(0, 0, -1)
                };
                foreach (var n in horizontalNeighbors)
                {
                    if (world.GetBlock(n) == BlockType.Water && world.GetWaterLevel(n) > currentLevel)
                    {
                        hasSource = true;
                        break;
                    }
                }
            }

            if (!hasSource)
            {
                world.SetBlockAndWater(pos, BlockType.Air, 0);
                return; // Bay hơi xong thì ngừng xử lý
            }
        }
        else if (currentLevel == 9) 
        {
            // Nước rơi (9) BẮT BUỘC phải có nước ở ngay trên đầu
            BlockType upType = world.GetBlock(pos + Vector3Int.up);
            if (upType != BlockType.Water)
            {
                world.SetBlockAndWater(pos, BlockType.Air, 0);
                return;
            }
        }

        // --- BƯỚC 2: RƠI XUỐNG DƯỚI ---
        Vector3Int downPos = pos + Vector3Int.down;
        BlockType downType = world.GetBlock(downPos);
        
        if (downType == BlockType.Air)
        {
            world.SetBlockAndWater(downPos, BlockType.Water, 9);
            return; // Đang rơi thì KHÔNG lan ngang
        }
        else if (downType == BlockType.Water)
        {
            byte downLevel = world.GetWaterLevel(downPos);
            // Nếu bên dưới là dòng chảy yếu hơn (1-7), hoặc đang là cột nước rơi (9)
            // thì khối hiện tại chỉ tập trung rơi tiếp, KHÔNG lan ngang!
            if (downLevel < 8 || downLevel == 9) 
            {
                if (downLevel < 8) 
                {
                    world.SetBlockAndWater(downPos, BlockType.Water, 9);
                }
                return; // Dừng lại, không lan ngang trên không trung!
            }
            // Nếu bên dưới là 8 (Mặt hồ tĩnh / Nguồn), thì coi như chạm đáy hồ -> chuyển qua lan ngang
        }

        // --- BƯỚC 3: LAN XUNG QUANH ---
        byte nextLevel = 0;
        if (currentLevel == 9 || currentLevel == 8) nextLevel = 7;
        else if (currentLevel > 1) nextLevel = (byte)(currentLevel - 1);
        
        if (nextLevel > 0)
        {
            Vector3Int[] neighbors = {
                pos + Vector3Int.left, pos + Vector3Int.right,
                pos + new Vector3Int(0, 0, 1), pos + new Vector3Int(0, 0, -1)
            };

            foreach (var n in neighbors)
            {
                BlockType nType = world.GetBlock(n);
                if (nType == BlockType.Air)
                {
                    world.SetBlockAndWater(n, BlockType.Water, nextLevel);
                }
                else if (nType == BlockType.Water)
                {
                    byte nLevel = world.GetWaterLevel(n);
                    if (nLevel < nextLevel)
                    {
                        world.SetBlockAndWater(n, BlockType.Water, nextLevel);
                    }
                }
            }
        }
    }
}
