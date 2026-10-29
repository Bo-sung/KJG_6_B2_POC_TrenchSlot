using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 카메라 정면 거리 안의 IInteractable을 찾아 안내 문구를 알리고, 상호작용 키가 눌리면 실행한다.
public sealed class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private InputActionReference interactAction;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private PlayerHealth health;
    [SerializeField] private LayerMask interactLayers;

    private float maxDistance;
    private string lastPrompt = "";

    public event Action<string> PromptChanged;

    private void OnEnable() => interactAction?.action?.Enable();
    private void OnDisable() => interactAction?.action?.Disable();

    public void Configure(float _maxDistance)
    {
        maxDistance = Mathf.Max(0f, _maxDistance);
    }

    private void Update()
    {
        if ((health != null && !health.IsAlive) || !InputGate.PlayerEnabled)
        {
            Notify("");
            return;
        }

        IInteractable target = FindTarget();
        bool usable = target != null && target.CanInteract();
        Notify(usable ? target.GetPrompt() : "");

        if (usable && interactAction != null && interactAction.action != null && interactAction.action.WasPressedThisFrame())
        {
            target.Interact();
        }
    }

    private IInteractable FindTarget()
    {
        if (viewCamera == null || maxDistance <= 0f)
        {
            return null;
        }
        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, interactLayers, QueryTriggerInteraction.Ignore))
        {
            return null;
        }
        return hit.collider.GetComponentInParent<IInteractable>();
    }

    private void Notify(string _prompt)
    {
        if (_prompt == lastPrompt)
        {
            return;
        }
        lastPrompt = _prompt;
        PromptChanged?.Invoke(_prompt);
    }
}
