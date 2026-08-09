using UnityEngine;

/// <summary>
/// FPS Player Controller — di chuyển kiểu Minecraft.
/// 
/// === CONTROLS ===
/// WASD / Arrow keys  → Di chuyển
/// Mouse              → Nhìn quanh (FPS view)
/// Space              → Nhảy
/// Escape             → Mở/khóa cursor
///
/// === CƠ CHẾ ===
/// Dùng CharacterController (không phải Rigidbody) vì:
/// - Phù hợp cho FPS: không bị trượt, không bị xoay khi va chạm
/// - Có sẵn collision detection với MeshCollider của chunk
/// - Gravity xử lý thủ công (velocity.y += gravity * deltaTime)
///
/// Mouse look:
/// - Mouse X → xoay Player quanh trục Y (nhìn trái/phải)
/// - Mouse Y → xoay Camera quanh trục X (nhìn lên/xuống, clamp ±90°)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Tốc độ di chuyển (block/giây)")]
    [SerializeField] private float moveSpeed = 5f;

    [Tooltip("Tốc độ bay")]
    [SerializeField] private float flySpeed = 20f;

    [Tooltip("Lực nhảy")]
    [SerializeField] private float jumpForce = 8f;

    [Tooltip("Trọng lực (âm = kéo xuống)")]
    [SerializeField] private float gravity = -20f;

    [Header("Mouse Look")]
    [Tooltip("Độ nhạy chuột")]
    [SerializeField] private float mouseSensitivity = 2f;

    // Components
    private CharacterController controller;
    private Transform cameraTransform;

    // State
    private Vector3 velocity;           // Vận tốc hiện tại (chủ yếu dùng cho Y: gravity + jump)
    private float cameraPitch = 0f;     // Góc nghiêng camera (trục X), clamp ±90°
    private bool cursorLocked = true;

    // Fly state
    private bool isFlying = false;
    private float lastSpaceTime = -1f;
    private float doubleTapThreshold = 0.3f;

    private void Start()
    {
        controller = GetComponent<CharacterController>();

        // Camera là con của Player (được setup trong GameSetup)
        cameraTransform = GetComponentInChildren<Camera>().transform;

        // Lock cursor khi bắt đầu
        SetCursorLock(true);
    }

    private void Update()
    {
        HandleCursorToggle();

        if (cursorLocked)
        {
            HandleMouseLook();
            HandleMovement();
        }
    }

    /// <summary>
    /// Nhấn Escape để toggle lock/unlock cursor.
    /// Khi cursor unlock → không xoay camera, cho phép click UI.
    /// Click chuột trái khi cursor unlock → lock lại cursor.
    /// </summary>
    private void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLock(!cursorLocked);
        }

        // Click để lock lại cursor khi đang unlock
        if (!cursorLocked && Input.GetMouseButtonDown(0))
        {
            SetCursorLock(true);
        }
    }

    /// <summary>
    /// Xoay camera theo chuyển động chuột.
    /// 
    /// Mouse X → xoay PLAYER quanh Y (nhìn trái/phải)
    ///   - Xoay player thay vì camera để hướng di chuyển đồng bộ với hướng nhìn
    /// 
    /// Mouse Y → xoay CAMERA quanh X (nhìn lên/xuống)
    ///   - Clamp ±90° để không bị lật ngược
    ///   - Dùng biến cameraPitch để tích lũy góc
    /// </summary>
    private void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Xoay player trái/phải
        transform.Rotate(Vector3.up * mouseX);

        // Xoay camera lên/xuống (clamp để không lật)
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -90f, 90f);
        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    /// <summary>
    /// Di chuyển player bằng WASD + gravity + jump.
    /// 
    /// CharacterController.Move() tự động xử lý collision:
    /// - Va chạm tường → dừng lại
    /// - Va chạm sàn → isGrounded = true
    /// - Trượt dọc slope
    /// 
    /// Gravity:
    /// - Mỗi frame: velocity.y += gravity * deltaTime
    /// - Khi chạm đất (isGrounded): reset velocity.y = -2 (giá trị nhỏ âm
    ///   để giữ chân dính đất, tránh "nhảy nhót" do floating point)
    /// </summary>
    private void HandleMovement()
    {
        // Double-tap Space để bật/tắt chế độ bay
        if (Input.GetButtonDown("Jump"))
        {
            if (Time.time - lastSpaceTime < doubleTapThreshold)
            {
                isFlying = !isFlying;
                if (!isFlying) velocity.y = 0f; // Reset lực khi tắt bay
            }
            lastSpaceTime = Time.time;
        }

        // Áp dụng tốc độ tùy theo trạng thái
        float currentSpeed = isFlying ? flySpeed : moveSpeed;

        // Input di chuyển ngang (local space)
        float moveX = Input.GetAxis("Horizontal");  // A/D
        float moveZ = Input.GetAxis("Vertical");    // W/S

        // Chuyển từ local → world direction
        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        controller.Move(move * currentSpeed * Time.deltaTime);

        if (isFlying)
        {
            // Trạng thái bay: Không có gravity, Space để bay lên, Shift/Ctrl để bay xuống
            float flyVertical = 0f;
            if (Input.GetButton("Jump")) flyVertical = currentSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.LeftControl)) flyVertical = -currentSpeed;

            velocity.y = flyVertical;
            controller.Move(velocity * Time.deltaTime);
        }
        else
        {
            // Trạng thái bình thường: đi bộ, nhảy, có trọng lực
            if (controller.isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;  // Giá trị nhỏ âm giữ chân dính đất
            }

            if (Input.GetButtonDown("Jump") && controller.isGrounded)
            {
                velocity.y = jumpForce;
            }

            velocity.y += gravity * Time.deltaTime;
            controller.Move(velocity * Time.deltaTime);
        }
    }

    private void SetCursorLock(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
