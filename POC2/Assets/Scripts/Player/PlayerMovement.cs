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

    private CharacterController controller;
    private float verticalSpeed;
    private float pitch;
    private float standingHeight;
    private Vector3 standingCenter;
    private Vector3 standingViewPosition;
    private float cameraVelocity;
    private GameConfigData feedbackConfig;

    public bool IsCrouched { get; private set; }
    public float CurrentMoveSpeed => moveSpeed * (IsCrouched ? GameConfigInfo.CrouchSpeedRatio(feedbackConfig) : 1f);

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        standingHeight = controller.height;
        standingCenter = controller.center;
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
            return;
        }

        if (viewPivot != null && lookAction != null && lookAction.action != null)
        {
            // Pointer delta is already accumulated per frame; do not multiply by deltaTime.
            var action = lookAction.action;
            float scale = action.activeControl != null && action.activeControl.device is Gamepad
                ? gamepadLookSpeed * Time.deltaTime : mouseSensitivity;
            Vector2 look = action.ReadValue<Vector2>() * scale;
            transform.Rotate(Vector3.up * look.x);
            pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
            viewPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

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
