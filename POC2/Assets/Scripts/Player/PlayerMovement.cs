using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private InputActionReference lookAction;
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

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        moveAction?.action?.Enable();
        lookAction?.action?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.action?.Disable();
        lookAction?.action?.Disable();
    }

    private void Update()
    {
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
        Vector3 velocity = (transform.right * input.x + transform.forward * input.y) * moveSpeed;
        velocity.y = verticalSpeed;
        controller.Move(velocity * Time.deltaTime);
    }
}
