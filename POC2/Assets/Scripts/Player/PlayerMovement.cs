using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
    [SerializeField] private InputActionReference crouchAction;
    [SerializeField] private Transform viewPivot;
    [SerializeField] private PlayerHealth health;
    [SerializeField, Min(0f)] private float moveSpeed;
    [SerializeField, Min(0f)] private float gravity;
    [SerializeField, Min(0f)] private float mouseSensitivity;
    [SerializeField, Min(0f)] private float gamepadLookSpeed = 180f;
    [SerializeField, Range(0f, 90f)] private float pitchLimit;

    // 씬 로드·GC 히치로 한 프레임이 길어질 때 큰 이동이 한 번에 나가 바닥을 뚫는 것을 막는다.
    private const float MAX_MOVE_DELTA_TIME = 0.05f;
    // 맵 아래로 떨어졌을 때 스폰 위치로 되돌리는 깊이.
    private const float FALL_RESET_DEPTH = 3f;

    private CharacterController controller;
    private float verticalSpeed;
    private float pitch;
    private float standingHeight;
    private Vector3 standingCenter;
    private Vector3 standingViewPosition;
    private float cameraVelocity;
    private float recoilPitch;
    private float recoilYaw;
    private float recoilRecovery;
    private GameConfigData feedbackConfig;
    private Vector3 spawnPosition;

    public bool IsCrouched { get; private set; }
    public bool IsControlEnabled => isActiveAndEnabled && controller != null && controller.enabled &&
        InputGate.PlayerEnabled && (health == null || health.IsAlive);
    // 이동속도 배율(이동속도 스탯 × 든 무기 무게). GameManager가 정한다.
    public float SpeedMultiplier { get; set; } = 1f;
    public float CurrentMoveSpeed => moveSpeed * Mathf.Max(0f, SpeedMultiplier) * (IsCrouched ? GameConfigInfo.CrouchSpeedRatio(feedbackConfig) : 1f);

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        standingHeight = controller.height;
        standingCenter = controller.center;
        spawnPosition = transform.position;
        if (viewPivot != null)
        {
            standingViewPosition = viewPivot.localPosition;
        }
    }

    private void Start()
    {
        feedbackConfig = TableManager.Instance.GameConfig;
    }

    private void OnEnable()
    {
        moveAction?.action?.Enable();
        lookAction?.action?.Enable();
        crouchAction?.action?.Enable();
    }

    private void OnDisable()
    {
        ResetRecoil();
        moveAction?.action?.Disable();
        lookAction?.action?.Disable();
        crouchAction?.action?.Disable();
    }

    private void Update()
    {
        bool enabled = (health == null || health.IsAlive) && InputGate.PlayerEnabled;
        UpdateCrouch(enabled && crouchAction != null && crouchAction.action != null && crouchAction.action.IsPressed(), Time.deltaTime);
        // 정비 상점·게임 오버 중에는 이동·시점 조작을 멈춘다. 슬롯 회전 중에는 막지 않는다.
        if ((health != null && !health.IsAlive) || !InputGate.PlayerEnabled)
        {
            ResetRecoil();
            return;
        }

        TickRecoil(Time.deltaTime);

        if (viewPivot != null && lookAction != null && lookAction.action != null)
        {
            // Pointer delta is already accumulated per frame; do not multiply by deltaTime.
            var action = lookAction.action;
            float scale = action.activeControl != null && action.activeControl.device is Gamepad
                ? gamepadLookSpeed * Time.deltaTime : mouseSensitivity;
            Vector2 look = action.ReadValue<Vector2>() * scale;
            transform.Rotate(Vector3.up * look.x);
            pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
        }
        ApplyViewRotation();

        Vector2 input = moveAction != null && moveAction.action != null
            ? Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f)
            : Vector2.zero;
        if (controller.isGrounded && verticalSpeed < 0f)
        {
            verticalSpeed = 0f;
        }

        verticalSpeed -= gravity * Time.deltaTime;
        Vector3 velocity = (transform.right * input.x + transform.forward * input.y) * CurrentMoveSpeed;
        velocity.y = verticalSpeed;
        controller.Move(velocity * Time.deltaTime);
    }

    // 반동은 마우스 입력과 분리된 일시적 시점 오프셋이다. 발사마다 누적되고 자연히 0으로 돌아간다.
    public void AddRecoil(float _pitch, float _yaw, float _recoveryPerSecond)
    {
        if (viewPivot == null || !InputGate.PlayerEnabled || (health != null && !health.IsAlive)) return;
        float maxPitch = GameConfigInfo.RecoilMaxPitchDegrees(feedbackConfig);
        float maxYaw = GameConfigInfo.RecoilMaxYawDegrees(feedbackConfig);
        recoilPitch = Mathf.Clamp(recoilPitch - Mathf.Max(0f, _pitch), -maxPitch, 0f);
        recoilYaw = Mathf.Clamp(recoilYaw + _yaw, -maxYaw, maxYaw);
        recoilRecovery = Mathf.Max(0f, _recoveryPerSecond);
        ApplyViewRotation();
    }

    public void TickRecoil(float _deltaTime)
    {
        float step = recoilRecovery * Mathf.Max(0f, _deltaTime);
        recoilPitch = Mathf.MoveTowards(recoilPitch, 0f, step);
        recoilYaw = Mathf.MoveTowards(recoilYaw, 0f, step);
        ApplyViewRotation();
    }

    private void ResetRecoil()
    {
        recoilPitch = 0f;
        recoilYaw = 0f;
        ApplyViewRotation();
    }

    private void ApplyViewRotation()
    {
        if (viewPivot != null)
            viewPivot.localRotation = Quaternion.Euler(Mathf.Clamp(pitch + recoilPitch, -pitchLimit, pitchLimit), recoilYaw, 0f);
    }

    // Same capsule and view path is used by input and deterministic physics verification.
    public void UpdateCrouch(bool _held, float _deltaTime)
    {
        if (controller == null)
        {
            return;
        }
        bool crouched = _held || (IsCrouched && !CanStand());
        float height = crouched ? Mathf.Max(controller.radius * 2f, standingHeight * GameConfigInfo.CrouchHeightRatio(feedbackConfig)) : standingHeight;
        Vector3 center = standingCenter + Vector3.up * ((height - standingHeight) * 0.5f);
        // Move the center by half the height change: world-space feet never move.
        controller.height = height;
        controller.center = center;
        IsCrouched = crouched;
        if (viewPivot != null)
        {
            Vector3 position = standingViewPosition;
            float target = standingViewPosition.y + height - standingHeight;
            position.y = Mathf.SmoothDamp(viewPivot.localPosition.y, target, ref cameraVelocity, GameConfigInfo.CrouchCameraSmoothSeconds(feedbackConfig), Mathf.Infinity, _deltaTime);
            // The camera stays inside the real capsule even during a fast crouch transition.
            float top = center.y + height * 0.5f - controller.skinWidth;
            if (position.y > top)
            {
                position.y = top;
                cameraVelocity = 0f;
            }
            viewPivot.localPosition = position;
        }
    }

    public bool CanStand()
    {
        Vector3 scale = transform.lossyScale;
        float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float half = Mathf.Max(radius, standingHeight * Mathf.Abs(scale.y) * 0.5f);
        Vector3 center = transform.TransformPoint(standingCenter);
        // Numerical contact epsilon only: using skinWidth here would miss thin ceilings.
        float inset = Mathf.Min(0.001f, radius * 0.01f);
        Collider[] overlaps = Physics.OverlapCapsule(center - transform.up * (half - radius), center + transform.up * (half - radius), radius - inset, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider overlap in overlaps)
        {
            if (overlap != controller && !overlap.transform.IsChildOf(transform))
            {
                return false;
            }
        }
        return true;
    }
}
