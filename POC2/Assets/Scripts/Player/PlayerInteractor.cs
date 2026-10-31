using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 카메라 정면 거리 안의 대상을 찾아 안내 문구를 알린다.
// F(interactAction)는 IInteractable을, 픽업 키(pickupAction)는 WeaponPickup을 대상으로 한다. 두 프롬프트는 서로 독립이다.
public sealed class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private InputActionReference interactAction;
    [SerializeField] private InputActionReference pickupAction;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private PlayerHealth health;
    [SerializeField] private LayerMask interactLayers;

    private static readonly RaycastHit[] OCCLUSION_BUFFER = new RaycastHit[16];

    private float maxDistance;
    private string lastPrompt = "";
    private string lastPickupPrompt = "";

    // 프레임 단위 판정 캐시. Update 순서와 상관없이 같은 프레임의 PlayerWeapon·PlayerInteractor가 같은 결과를 본다.
    private int aimFrame = -1;
    private WeaponPickup aimPickup;
    private int claimFrame = -1;
    private WeaponPickup claimPickup;
    private int pickupHandledFrame = -1;

    // 픽업 가능 여부 판정. GameManager가 등록한다(자리·상태 확인). 없으면 픽업 불가로 본다.
    public Func<WeaponPickup, bool> CanPickup;
    // 픽업 안내 문구. GameManager가 빈 슬롯 여부에 맞춰 등록한다. 없으면 기본 문구를 쓴다.
    public Func<WeaponPickup, string> PickupPromptText;

    // 지금 카메라 정면으로 조준된 유효한 픽업 대상. 없으면 null이다. 이번 프레임에 새로 판정한다.
    public WeaponPickup AimedPickup
    {
        get
        {
            ResolveAim();
            return aimPickup;
        }
    }

    // 이번 프레임에 픽업 키로 예약된 줍기 대상. 없으면 null이다.
    // 같은 키(게임패드 X)가 Reload에도 묶여 있으므로, 이 값이 있으면 이번 프레임 재장전은 어느 총에도 시작하지 않는다.
    // 한 번 정해지면 줍기로 대상이 소비된 뒤에도 그 프레임 동안 유지된다.
    public WeaponPickup PickupClaimThisFrame
    {
        get
        {
            ResolveClaim();
            return claimPickup;
        }
    }

    public event Action<string> PromptChanged;
    public event Action<string> PickupPromptChanged;
    // 픽업 키가 눌린 순간 조준 대상. 실제 줍기 판정은 받는 쪽(GameManager)이 한다.
    public event Action<WeaponPickup> PickupPressed;

    private void OnEnable()
    {
        interactAction?.action?.Enable();
        pickupAction?.action?.Enable();
    }

    private void OnDisable()
    {
        interactAction?.action?.Disable();
        pickupAction?.action?.Disable();
    }

    public void Configure(float _maxDistance)
    {
        maxDistance = Mathf.Max(0f, _maxDistance);
    }

    private void Update()
    {
        if (!InputAllowed())
        {
            Notify("");
            NotifyPickup("");
            return;
        }

        bool aimFound = TryAim(out RaycastHit aimHit);
        IInteractable target = aimFound ? aimHit.collider.GetComponentInParent<IInteractable>() : null;
        bool usable = target != null && target.CanInteract();
        Notify(usable ? target.GetPrompt() : "");

        if (usable && interactAction != null && interactAction.action != null && interactAction.action.WasPressedThisFrame())
        {
            target.Interact();
        }

        WeaponPickup pickup = AimedPickup;
        NotifyPickup(pickup != null ? PickupPrompt(pickup) : "");

        // 예약된 줍기는 프레임당 한 번만 실행한다. 실제 줍기 판정은 받는 쪽(GameManager)이 다시 한다.
        WeaponPickup claim = PickupClaimThisFrame;
        if (claim != null && pickupHandledFrame != Time.frameCount)
        {
            pickupHandledFrame = Time.frameCount;
            PickupPressed?.Invoke(claim);
        }
    }

    // 조준 판정은 프레임마다 처음 묻는 쪽이 한 번 계산한다.
    private void ResolveAim()
    {
        if (aimFrame == Time.frameCount)
        {
            return;
        }
        aimFrame = Time.frameCount;
        aimPickup = null;
        if (!InputAllowed() || !TryAim(out RaycastHit aimHit))
        {
            return;
        }
        WeaponPickup pickup = aimHit.collider.GetComponentInParent<WeaponPickup>();
        if (pickup != null && !pickup.IsConsumed && !IsOccluded(aimHit, pickup.transform) && CanPickup != null && CanPickup(pickup))
        {
            aimPickup = pickup;
        }
    }

    private void ResolveClaim()
    {
        if (claimFrame == Time.frameCount)
        {
            return;
        }
        claimFrame = Time.frameCount;
        bool pressed = pickupAction != null && pickupAction.action != null && pickupAction.action.WasPressedThisFrame();
        claimPickup = pressed ? AimedPickup : null;
    }

    private bool InputAllowed()
    {
        return gameObject.activeInHierarchy && (health == null || health.IsAlive) && InputGate.PlayerEnabled;
    }

    // 줍기 직전 재검증용. 지금 카메라에서 상호작용 거리 안으로 쏜 시선이 가려지지 않고 그 픽업에 처음 닿는지 본다.
    public bool IsPickupInView(WeaponPickup _pickup)
    {
        if (_pickup == null || !InputAllowed() || !TryAim(out RaycastHit hit))
        {
            return false;
        }
        return !_pickup.IsConsumed && hit.collider.GetComponentInParent<WeaponPickup>() == _pickup && !IsOccluded(hit, _pickup.transform);
    }

    // 조준 질의는 상호작용 레이어만 보므로, 벽 너머 픽업을 거르려고 모든 솔리드로 시선을 다시 확인한다.
    // 플레이어 자신과 대상 계층의 콜라이더는 가림으로 보지 않는다.
    private bool IsOccluded(RaycastHit _hit, Transform _target)
    {
        Vector3 origin = viewCamera.transform.position;
        Vector3 direction = _hit.point - origin;
        float distance = direction.magnitude;
        if (distance <= Mathf.Epsilon)
        {
            return false;
        }
        int count = Physics.RaycastNonAlloc(origin, direction / distance, OCCLUSION_BUFFER, distance, ~0, QueryTriggerInteraction.Ignore);
        Transform self = health != null ? health.transform : transform.root;
        for (int i = 0; i < count; i++)
        {
            Transform other = OCCLUSION_BUFFER[i].collider.transform;
            if (!other.IsChildOf(self) && !other.IsChildOf(_target))
            {
                return true;
            }
        }
        return false;
    }

    // 픽업 안내는 키 표기(F·X)와 무기 이름을 보인다. 이름이 비어 있으면 id를 쓴다.
    private string PickupPrompt(WeaponPickup _pickup)
    {
        if (PickupPromptText != null)
        {
            return PickupPromptText(_pickup);
        }
        string label = string.IsNullOrEmpty(_pickup.DisplayName) ? _pickup.WeaponId : _pickup.DisplayName;
        return $"[F / X] {label} 줍기 · 현재 무기는 바닥에 놓임";
    }

    private bool TryAim(out RaycastHit _hit)
    {
        _hit = default;
        if (viewCamera == null || maxDistance <= 0f)
        {
            return false;
        }
        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        return Physics.Raycast(ray, out _hit, maxDistance, interactLayers, QueryTriggerInteraction.Ignore);
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

    private void NotifyPickup(string _prompt)
    {
        if (_prompt == lastPickupPrompt)
        {
            return;
        }
        lastPickupPrompt = _prompt;
        PickupPromptChanged?.Invoke(_prompt);
    }
}
