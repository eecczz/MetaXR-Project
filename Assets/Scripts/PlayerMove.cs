using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerMove : MonoBehaviour
{
    [Header("References")]
    [Tooltip("플레이어 루트(움직일 대상). 보통 CharacterController가 붙은 루트")]
    public CharacterController characterController;

    [Tooltip("HMD(보통 CenterEyeAnchor 카메라 Transform)")]
    public Transform head;

    [Tooltip("OVRCameraRig의 TrackingSpace. (없으면 head.parent를 사용)")]
    public Transform trackingSpace;

    [Header("Input Actions")]
    public InputActionReference moveAction;   // Left Stick (Vector2)
    public InputActionReference turnAction;   // Right Stick (Vector2 or Float)

    [Header("Move")]
    public float moveSpeed = 2.0f;
    public float gravity = -9.81f;
    public bool headRelative = true;

    [Header("Turn (Right Stick X)")]
    [Tooltip("deg/sec")]
    public float turnSpeed = 90f;
    [Tooltip("조이스틱 데드존")]
    public float turnDeadzone = 0.15f;

    [Header("Crouch / Stand (Right Stick Y)")]
    [Tooltip("앉을 때 TrackingSpace를 내릴 높이(미터)")]
    public float crouchOffset = 0.5f;

    [Tooltip("앉기/서기 보간 속도(초당)")]
    public float crouchLerpSpeed = 6f;

    [Tooltip("스틱 Y 데드존")]
    public float crouchDeadzone = 0.5f;

    float _verticalVelocity;

    float _targetCrouchY = 0f;     // trackingSpace local y offset target
    float _currentCrouchY = 0f;    // trackingSpace local y offset current

    void Reset()
    {
        characterController = GetComponent<CharacterController>();
        if (Camera.main) head = Camera.main.transform;
    }

    void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (trackingSpace == null && head != null)
            trackingSpace = head.parent; // 보통 CenterEyeAnchor의 부모가 TrackingSpace

        // 초기 crouch 오프셋(기본 0)
        _targetCrouchY = 0f;
        _currentCrouchY = 0f;
    }

    void OnEnable()
    {
        if (moveAction != null) moveAction.action.Enable();
        if (turnAction != null) turnAction.action.Enable();
    }

    void OnDisable()
    {
        if (moveAction != null) moveAction.action.Disable();
        if (turnAction != null) turnAction.action.Disable();
    }

    void Update()
    {
        if (characterController == null || head == null) return;

        float dt = Time.deltaTime;

        // =====================
        // 이동 (왼쪽 스틱)
        // =====================
        Vector2 move = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;

        Vector3 forward = headRelative ? Flatten(head.forward) : Flatten(transform.forward);
        Vector3 right = headRelative ? Flatten(head.right) : Flatten(transform.right);

        Vector3 moveWorld = (forward * move.y + right * move.x) * moveSpeed;

        // 중력
        if (characterController.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -1f;

        _verticalVelocity += gravity * dt;

        Vector3 velocity = moveWorld + Vector3.up * _verticalVelocity;
        characterController.Move(velocity * dt);

        // =====================
        // 오른쪽 스틱 (턴 + 앉기)
        // =====================
        float turnX = ReadTurnX();
        float crouchY = ReadTurnY(); // Vector2면 y, float이면 0 처리

        // ---- 좌우 회전 ----
        DoTurn(turnX, dt);

        // ---- 앉기/서기 목표 갱신 ----
        UpdateCrouchTarget(crouchY);

        // ---- TrackingSpace Y 오프셋 보간 적용 ----
        ApplyCrouch(dt);
    }

    // -----------------------------
    // Turn : "머리 위치 기준"으로 리그를 회전 (VR에서 체감 좋음)
    // -----------------------------
    void DoTurn(float x, float dt)
    {
        if (Mathf.Abs(x) < turnDeadzone) return;

        // 회전량
        float yaw = x * turnSpeed * dt;

        // RotateAround 중심: HMD 위치(월드)
        Vector3 pivot = head.position;

        // 리그 루트를 pivot 기준으로 회전
        // (transform = 스크립트가 붙은 오브젝트. 보통 리그 루트여야 함)
        transform.RotateAround(pivot, Vector3.up, yaw);
    }

    // -----------------------------
    // Crouch : head가 아니라 trackingSpace를 내린다 (OVR 트래킹 덮어쓰기 회피)
    // -----------------------------
    void UpdateCrouchTarget(float y)
    {
        if (y > crouchDeadzone)
        {
            // 일어서기
            _targetCrouchY = 0f;
        }
        else if (y < -crouchDeadzone)
        {
            // 앉기 (TrackingSpace를 아래로)
            _targetCrouchY = -Mathf.Abs(crouchOffset);
        }
    }

    void ApplyCrouch(float dt)
    {
        if (trackingSpace == null) return;

        _currentCrouchY = Mathf.Lerp(_currentCrouchY, _targetCrouchY, dt * crouchLerpSpeed);

        Vector3 lp = trackingSpace.localPosition;
        lp.y = _currentCrouchY;
        trackingSpace.localPosition = lp;
    }

    // -----------------------------
    // Input reading (Vector2/Float 둘 다 대응)
    // -----------------------------
    float ReadTurnX()
    {
        if (turnAction == null) return 0f;
        var a = turnAction.action;
        if (a == null) return 0f;

        // Vector2면 x
        if (a.activeControl != null && a.activeControl.valueType == typeof(Vector2))
            return a.ReadValue<Vector2>().x;

        // Float(1D)면 그 값
        return a.ReadValue<float>();
    }

    float ReadTurnY()
    {
        if (turnAction == null) return 0f;
        var a = turnAction.action;
        if (a == null) return 0f;

        // Vector2일 때만 y 사용
        if (a.activeControl != null && a.activeControl.valueType == typeof(Vector2))
            return a.ReadValue<Vector2>().y;

        return 0f;
    }

    static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }
}
