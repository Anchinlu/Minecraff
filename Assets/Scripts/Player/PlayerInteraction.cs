using UnityEngine;

/// <summary>
/// PlayerInteraction — đào và đặt block bằng raycast.
/// 
/// === CƠ CHẾ RAYCAST ===
/// 1. Bắn 1 tia từ giữa màn hình (camera forward) ra phía trước
/// 2. Tia va chạm MeshCollider của chunk → nhận được:
///    - hit.point: điểm va chạm chính xác (float)
///    - hit.normal: hướng mặt bị hit (dùng để tính block nào)
///
/// === TÍNH BLOCK TỪ HIT POINT ===
/// Vấn đề: hit.point nằm TRÊN BỀ MẶT face, không nằm bên trong block.
/// Giải pháp: dùng hit.normal để "dịch" vào trong hoặc ra ngoài:
///
/// Đào (phá block):
///   blockPos = floor(hit.point - hit.normal * 0.5)
///   → Dịch vào TRONG block bị hit, rồi floor → tọa độ block
///
/// Đặt (thêm block):
///   blockPos = floor(hit.point + hit.normal * 0.5)
///   → Dịch ra NGOÀI block (phía Air), rồi floor → tọa độ đặt block mới
///
/// Ví dụ: hit mặt Top (normal = 0,1,0) của block (3, 5, 2):
///   - Đào: floor(hit - (0, 0.5, 0)) = (3, 5, 2) → phá block này
///   - Đặt: floor(hit + (0, 0.5, 0)) = (3, 6, 2) → đặt block phía trên
/// </summary>
public class PlayerInteraction : MonoBehaviour
{
    [Header("Interaction")]
    [Tooltip("Khoảng cách tối đa để đào/đặt block (tính bằng block/unit)")]
    [SerializeField] private float reachDistance = 6f;

    [Tooltip("Loại block sẽ đặt khi click phải")]
    [SerializeField] private BlockType placeBlockType = BlockType.Stone;

    // Reference tới World — để gọi SetBlock/GetBlock
    private World world;
    private Camera playerCamera;

    /// <summary>
    /// Gán reference tới World. Gọi từ GameSetup sau khi tạo player.
    /// </summary>
    public void SetWorld(World world)
    {
        this.world = world;
    }

    private void Start()
    {
        playerCamera = GetComponentInChildren<Camera>();
    }

    private void Update()
    {
        // Chỉ xử lý khi cursor đang locked (đang chơi, không phải UI)
        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        if (Input.GetMouseButtonDown(0))    // Click trái → đào
        {
            BreakBlock();
        }

        if (Input.GetMouseButtonDown(1))    // Click phải → đặt
        {
            PlaceBlock();
        }

        if (Input.GetMouseButtonDown(2))    // Nút cuộn chuột (Middle Click) → Pick Block
        {
            PickBlock();
        }
    }

    /// <summary>
    /// Pick block (chọn block bằng nút cuộn chuột).
    /// </summary>
    private void PickBlock()
    {
        if (world == null) return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, reachDistance))
        {
            // Tính vị trí block đang nhìn vào (giống hệt logic đào)
            Vector3Int blockPos = Vector3Int.FloorToInt(hit.point - hit.normal * 0.5f);
            BlockType targetBlock = world.GetBlock(blockPos);

            if (targetBlock != BlockType.Air)
            {
                placeBlockType = targetBlock;
                Debug.Log($"[Interaction] Picked block: {placeBlockType}");
            }
        }
    }

    /// <summary>
    /// Đào (phá) block: bắn ray → tìm block bị hit → set thành Air → rebuild mesh.
    /// </summary>
    private void BreakBlock()
    {
        if (world == null) return;

        // Bắn ray từ giữa màn hình
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, reachDistance))
        {
            // Tính block bị hit: dịch vào trong 0.5 unit theo hướng ngược normal
            Vector3Int blockPos = Vector3Int.FloorToInt(hit.point - hit.normal * 0.5f);

            // Set block thành Air (phá block)
            world.SetBlock(blockPos, BlockType.Air);

            Debug.Log($"[Interaction] Break block at {blockPos}");
        }
    }

    /// <summary>
    /// Đặt block: bắn ray → tìm mặt block → đặt block mới phía ngoài → rebuild mesh.
    /// </summary>
    private void PlaceBlock()
    {
        if (world == null) return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, reachDistance))
        {
            // Tính vị trí đặt block: dịch ra ngoài 0.5 unit theo normal
            Vector3Int placePos = Vector3Int.FloorToInt(hit.point + hit.normal * 0.5f);

            // Kiểm tra: không đặt block vào vị trí player đang đứng
            // (tránh bị kẹt trong block)
            Vector3Int playerBlockPos = Vector3Int.FloorToInt(transform.position);
            Vector3Int playerHeadPos = playerBlockPos + Vector3Int.up;

            if (placePos == playerBlockPos || placePos == playerHeadPos)
            {
                Debug.Log("[Interaction] Cannot place block at player position!");
                return;
            }

            // Đặt block
            if (placeBlockType == BlockType.Water)
            {
                // Người chơi đặt nước luôn là nguồn (Level 8)
                world.SetBlockAndWater(placePos, BlockType.Water, 8);
            }
            else
            {
                world.SetBlock(placePos, placeBlockType);
            }

            Debug.Log($"[Interaction] Place {placeBlockType} at {placePos}");
        }
    }
}
