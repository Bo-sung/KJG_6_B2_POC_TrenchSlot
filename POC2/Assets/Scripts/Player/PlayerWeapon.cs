using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 사용 중인 무기의 사격·재장전. 장전탄은 개별 무기(WeaponInstance)에 기록하므로 무기를 바꿔도 각자 유지된다.
// 아킴보면 두 자루(손 0 = 사용 중 슬롯, 손 1 = 보조 슬롯)를 함께 든다. 탄창·스탯·연사 간격은 손마다 따로이며,
// 좌우를 번갈아 쏘고 한쪽이 비면 남은 쪽만 쏜다. 재장전은 양손 동시이고 공유 예비탄에서 채운다.
// 휴대 슬롯 전환 입력은 여기서 받아 알리기만 하고, 실제 전환은 GameManager가 한다.
public sealed class PlayerWeapon : MonoBehaviour
{
    [Serializable]
    public struct WeaponSettings
    {
        [Min(0)] public int magazineSize;
        // 산탄총은 산탄 1발 피해다.
        [Min(0f)] public float damage;
        [Min(0f)] public float range;
        [Min(0f)] public float shotsPerSecond;
        [Min(0f)] public float reloadSeconds;
        public bool reloadPerRound;
        public bool automatic;
        // 방아쇠 1회당 발사체 수와 산포 반각(도).
        [Min(1)] public int pellets;
        [Min(0f)] public float spreadDegrees;
        // 발당 저지력, 산탄 발사 1회 합산 상한(0이면 없음), 넉백 세기. 적용은 전투 쪽(Enemy.TakeHit)이 한다.
        [Min(0f)] public float stoppingPower;
        [Min(0f)] public float pelletStoppingPowerCap;
        [Min(0f)] public float knockback;
        // 발사 시 탄창 탄을 소비하지 않을 확률(파츠).
        [Range(0f, 1f)] public float ammoSaveChance;
        // 발사체 탄도. 최대 비행 거리는 range를 쓴다.
        [Min(0f)] public float projectileSpeed;
        [Min(0f)] public float projectileLifetime;
        [Min(0f)] public float projectileRadius;
    }

    // 슬롯 전환 액션 이름. 참조 에셋이 연결되지 않은 씬에서는 사격 액션과 같은 맵에서 이 이름으로 찾는다.
    private static readonly string[] SLOT_ACTION_NAMES = { "WeaponSlot1", "WeaponSlot2" };

    [SerializeField] private InputActionReference fireAction;
    [SerializeField] private InputActionReference reloadAction;
    [SerializeField] private InputActionReference pickupAction;
    [SerializeField] private InputActionReference[] slotActions = Array.Empty<InputActionReference>();
    [SerializeField] private Camera aimCamera;
    [SerializeField] private PlayerHealth health;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private LayerMask hitLayers;
    [SerializeField] private WeaponSettings settings;

    // 카메라 기준 총구 위치(근사). 손에 든 무기 모델 위치(0.25, -0.22, 0.6)보다 조금 앞이다. 아킴보 왼손은 x를 뒤집는다.
    private static readonly Vector3 MUZZLE_OFFSET = new Vector3(0.25f, -0.22f, 0.9f);
    private static readonly Vector3 OFF_HAND_MUZZLE_OFFSET = new Vector3(-0.25f, -0.22f, 0.9f);

    // 개별 무기가 연결되지 않은 경우(씬 기본값)의 손 0 연사 시각. 연결된 무기는 WeaponInstance.NextShotTime을 쓴다.
    private float fallbackNextShotTime;
    private float reloadEndTime;
    // 진행 중인 재장전의 전체 시간. 아킴보는 배율이 붙으므로 진행률은 이 값을 기준으로 한다.
    private float reloadDuration;
    private bool isReloading;
    // 아킴보 보조 손(손 1). 아킴보가 아니면 offHand는 null이다.
    private bool akimbo;
    private WeaponInstance offHand;
    private WeaponSettings offSettings;
    private float akimboReloadMultiplier = 1f;
    // 다음에 쏠 손. 쏜 손의 반대쪽으로 넘긴다.
    private int nextHand;
    private int fallbackAmmo;
    private int pelletGroupCounter;
    // 산탄 한 발의 명중 표시 누적(그룹 번호, 머리·헬멧 명중 여부).
    private int markerGroup;
    private bool markerHead;
    private bool markerHelmet;
    // 조준점 판정과 총구 가림 검사에 쓰는 버퍼.
    private static readonly RaycastHit[] AIM_BUFFER = new RaycastHit[16];
    private WeaponInstance current;
    private AmmoInventory inventory;
    private InputAction[] slotInputs = Array.Empty<InputAction>();
    private InputAction aimInput;
    private float normalFieldOfView;
    private GameConfigData aimConfig;
    private WeaponHeldModels heldModels;
    private float aimAmount;
    public bool IsAiming { get; private set; }

    public void SetHeldModels(WeaponHeldModels _models)
    {
        if (heldModels != _models) heldModels?.SetAim(aimCamera, current, 0f);
        heldModels = _models;
    }

    public string AmmoId { get; private set; }
    public int ReserveAmmo => inventory != null ? inventory.GetReserve(AmmoId) : 0;
    public WeaponInstance Current => current;
    public WeaponSettings Settings => settings;
    public bool IsAkimbo => akimbo;
    public int HandCount => akimbo ? 2 : 1;
    // 마지막으로 쏜 손(0 또는 1). 아직 쏘지 않았으면 0이다.
    public int LastFiredHand { get; private set; }

    public WeaponInstance Hand(int _hand)
    {
        if (_hand == 0)
        {
            return current;
        }
        return _hand == 1 && akimbo ? offHand : null;
    }

    public WeaponSettings HandSettings(int _hand)
    {
        return _hand == 1 && akimbo ? offSettings : settings;
    }

    public int HandAmmo(int _hand)
    {
        if (_hand == 0)
        {
            return MainAmmo;
        }
        return _hand == 1 && akimbo ? offHand.Magazine : 0;
    }

    public int HandMagazineSize(int _hand)
    {
        if (_hand == 0)
        {
            return Mathf.Max(0, settings.magazineSize);
        }
        return _hand == 1 && akimbo ? Mathf.Max(0, offSettings.magazineSize) : 0;
    }

    // 장전탄. 아킴보면 양손 합계다(HUD 표시용). 손별 값은 HandAmmo를 쓴다. 내부 설정은 손 0에만 쓴다.
    public int Ammo
    {
        get => akimbo ? MainAmmo + offHand.Magazine : MainAmmo;
        private set => MainAmmo = value;
    }

    // 손 0 장전탄. 개별 무기가 연결돼 있으면 그 무기의 값을 읽고 쓴다.
    private int MainAmmo
    {
        get => current != null ? current.Magazine : fallbackAmmo;
        set
        {
            if (current != null)
            {
                current.Magazine = value;
            }
            else
            {
                fallbackAmmo = value;
            }
        }
    }
    // 탄창 크기. 아킴보면 양손 합계다.
    public int MagazineSize => HandMagazineSize(0) + HandMagazineSize(1);
    public bool IsReloading => isReloading;
    // 재장전 진행률 0~1. 재장전 중이 아니면 0이다. 즉시 재장전(시간 0)은 항상 0이다.
    public float ReloadProgress
    {
        get
        {
            if (!isReloading || reloadDuration <= 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(1f - (reloadEndTime - Time.time) / reloadDuration);
        }
    }
    // 발사체가 실제로 살아 있던 적에게 피해를 준 순간에만 나간다. bool은 그 피해로 처치됐는지다. 벽·지형 명중에는 나가지 않는다.
    public event Action<bool> EnemyHit;
    // 적 명중 상세(부위·저지력 등). 전투 피드백용.
    public event Action<Projectile.Impact> EnemyImpact;
    public event Action<int, int> AmmoChanged;
    public event Action Fired;
    // 쏜 손(0 또는 1). Fired 직후에 나간다.
    public event Action<int> HandFired;
    // 휴대 슬롯 전환 요청(0부터). 빈 슬롯 확인과 전환은 받는 쪽이 한다.
    public event Action<int> SlotSelected;

    private void Awake()
    {
        if (current == null)
        {
            fallbackAmmo = Mathf.Max(0, settings.magazineSize);
        }
        ResolveSlotInputs();
        aimInput = fireAction != null && fireAction.action != null ? fireAction.action.actionMap?.FindAction("Aim") : null;
        if (aimCamera != null) normalFieldOfView = aimCamera.fieldOfView;
        aimConfig = TableManager.Instance.GameConfig;
    }

    private void OnEnable()
    {
        fireAction?.action?.Enable();
        reloadAction?.action?.Enable();
        pickupAction?.action?.Enable();
        aimInput?.Enable();
        foreach (var input in slotInputs)
        {
            input?.Enable();
        }
    }

    private void OnDisable()
    {
        CancelReload();
        ResetAim();
        fireAction?.action?.Disable();
        reloadAction?.action?.Disable();
        pickupAction?.action?.Disable();
        aimInput?.Disable();
        foreach (var input in slotInputs)
        {
            input?.Disable();
        }
    }

    // 연결된 참조를 쓰고, 없으면 사격 액션과 같은 맵에서 이름으로 찾는다.
    private void ResolveSlotInputs()
    {
        slotInputs = new InputAction[SLOT_ACTION_NAMES.Length];
        var map = fireAction != null && fireAction.action != null ? fireAction.action.actionMap : null;
        for (int i = 0; i < slotInputs.Length; i++)
        {
            var reference = slotActions != null && i < slotActions.Length ? slotActions[i] : null;
            slotInputs[i] = reference != null && reference.action != null ? reference.action : map?.FindAction(SLOT_ACTION_NAMES[i]);
        }
    }

    private void Update()
    {
        if (health != null && !health.IsAlive)
        {
            CancelReload();
            ResetAim();
            return;
        }

        // 정비 상점·게임 오버 중에는 사격·재장전·전환을 막는다. 슬롯 회전 중에는 막지 않는다.
        if (!InputGate.PlayerEnabled)
        {
            CancelReload();
            ResetAim();
            return;
        }

        UpdateAim(aimInput != null && aimInput.IsPressed(), Time.deltaTime);

        // 끝난 재장전을 먼저 완료한다. 같은 프레임의 슬롯 전환이 다 된 재장전을 취소하지 않게 한다.
        if (isReloading && Time.time >= reloadEndTime)
        {
            CompleteReload();
        }

        for (int i = 0; i < slotInputs.Length; i++)
        {
            if (slotInputs[i] != null && slotInputs[i].WasPressedThisFrame())
            {
                SlotSelected?.Invoke(i);
            }
        }

        bool wantsReload = reloadAction != null && reloadAction.action != null && reloadAction.action.WasPressedThisFrame();
        // 이번 프레임에 픽업 키로 줍기가 예약됐으면 재장전보다 줍기가 앞선다(게임패드 X는 두 동작에 같이 묶여 있다).
        // 예약은 PlayerInteractor가 프레임 단위로 한 번 정하므로 두 컴포넌트의 Update 순서와 상관없이 어느 총도 재장전하지 않는다.
        if (wantsReload && ReloadBlockedByPickupInView())
        {
            CancelReload();
        }
        else if (wantsReload)
        {
            StartReload();
        }

        if (fireAction == null || fireAction.action == null)
        {
            return;
        }

        bool wantsFire = settings.automatic ? fireAction.action.IsPressed() : fireAction.action.WasPressedThisFrame();
        if (wantsFire)
        {
            TryFire();
        }
    }

    // 개별 무기를 손에 든다. 장전탄은 그 무기에 기록된 값을 그대로 쓰며, 진행 중이던 재장전은 취소한다(탄약 이득 없음).
    // 아킴보였다면 풀린다.
    public void Configure(WeaponInstance _weapon, WeaponSettings _settings, AmmoInventory _inventory, string _ammoId)
    {
        ResetAim();
        isReloading = false;
        akimbo = false;
        offHand = null;
        current = _weapon;
        inventory = _inventory;
        AmmoId = _ammoId;
        settings = _settings;
        // 연사 간격은 무기 개체에 남아 있으므로 초기화하지 않는다(슬롯 전환으로 간격을 건너뛰지 못한다).
        nextHand = 0;
        LastFiredHand = 0;
        MainAmmo = Mathf.Clamp(MainAmmo, 0, Mathf.Max(0, settings.magazineSize));
        AmmoChanged?.Invoke(Ammo, MagazineSize);
    }

    // 같은 종류의 서로 다른 두 자루를 양손에 든다. 두 무기는 같은 탄종(공유 예비탄)을 쓴다.
    // _reloadMultiplier는 양손 동시 재장전 시간 배율이다. 진행 중이던 재장전은 취소한다.
    public void ConfigureAkimbo(WeaponInstance _main, WeaponSettings _mainSettings, WeaponInstance _off, WeaponSettings _offSettings,
        AmmoInventory _inventory, string _ammoId, float _reloadMultiplier)
    {
        Configure(_main, _mainSettings, _inventory, _ammoId);
        if (_main == null || _off == null || _main == _off)
        {
            return;
        }
        akimbo = true;
        ResetAim();
        offHand = _off;
        offSettings = _offSettings;
        akimboReloadMultiplier = Mathf.Max(0f, _reloadMultiplier);
        offHand.Magazine = Mathf.Clamp(offHand.Magazine, 0, Mathf.Max(0, offSettings.magazineSize));
        AmmoChanged?.Invoke(Ammo, MagazineSize);
    }

    // 같은 무기의 스탯만 바뀐 경우(파츠·장전 스탯). 재장전 진행은 유지한다. 손 0의 스탯이다.
    public void UpdateSettings(WeaponSettings _settings)
    {
        settings = _settings;
        MainAmmo = Mathf.Clamp(MainAmmo, 0, Mathf.Max(0, settings.magazineSize));
        AmmoChanged?.Invoke(Ammo, MagazineSize);
    }

    // 들고 있는 무기 중 _weapon의 스탯만 바꾼다. 들고 있지 않은 무기면 false다.
    public bool UpdateSettings(WeaponInstance _weapon, WeaponSettings _settings)
    {
        if (_weapon != null && _weapon == current)
        {
            UpdateSettings(_settings);
            return true;
        }
        if (akimbo && _weapon != null && _weapon == offHand)
        {
            offSettings = _settings;
            offHand.Magazine = Mathf.Clamp(offHand.Magazine, 0, Mathf.Max(0, offSettings.magazineSize));
            AmmoChanged?.Invoke(Ammo, MagazineSize);
            return true;
        }
        return false;
    }

    // 이번 프레임 줍기 예약 여부. 이전 프레임 조준 캐시가 아니라 이번 프레임의 픽업 키 입력과 조준으로 정해진다.
    private bool ReloadBlockedByPickupInView()
    {
        return interactor != null && interactor.PickupClaimThisFrame != null;
    }

    // 무기를 내려놓거나 바꿀 때 재장전을 끊는다. 진행 중이던 재장전은 완료되지 않으며 탄약 이득도 없다.
    public void CancelReload()
    {
        if (!isReloading)
        {
            return;
        }
        isReloading = false;
        AmmoChanged?.Invoke(Ammo, MagazineSize);
    }

    public void StartReload()
    {
        if (!InputGate.PlayerEnabled || (health != null && !health.IsAlive) || isReloading || !NeedsReload() || ReserveAmmo <= 0)
        {
            return;
        }

        isReloading = true;
        reloadDuration = ReloadStepSeconds();
        reloadEndTime = Time.time + reloadDuration;
        if (reloadDuration <= 0f)
        {
            CompleteReload();
        }
        else
        {
            AmmoChanged?.Invoke(Ammo, MagazineSize);
        }
    }

    // 덜 찬 손이 하나라도 있는지.
    private bool NeedsReload()
    {
        for (int i = 0; i < HandCount; i++)
        {
            if (HandMagazineSize(i) > 0 && HandAmmo(i) < HandMagazineSize(i))
            {
                return true;
            }
        }
        return false;
    }

    // 아킴보는 양손 동시 재장전이라 두 자루 중 긴 시간에 아킴보 배율을 곱한다.
    private float ReloadSeconds()
    {
        float seconds = Mathf.Max(0f, settings.reloadSeconds);
        if (akimbo)
        {
            seconds = Mathf.Max(seconds, Mathf.Max(0f, offSettings.reloadSeconds)) * akimboReloadMultiplier;
        }
        return seconds;
    }

    // 조준은 유지 입력이며 아킴보·입력 차단·사망 중에는 허용하지 않는다.
    private void UpdateAim(bool _pressed, float _deltaTime)
    {
        IsAiming = _pressed && !akimbo && InputGate.PlayerEnabled && (health == null || health.IsAlive) && aimCamera != null;
        if (aimCamera == null || normalFieldOfView <= 0f) return;
        float ratio = GameConfigInfo.AimFovRatio(aimConfig);
        aimAmount = Mathf.MoveTowards(aimAmount, IsAiming ? 1f : 0f, Mathf.Max(0f, _deltaTime) / GameConfigInfo.AimTransitionSeconds(aimConfig));
        aimCamera.fieldOfView = Mathf.Lerp(normalFieldOfView, normalFieldOfView * ratio, aimAmount);
        heldModels?.SetAim(aimCamera, current, aimAmount);
    }

    private void ResetAim()
    {
        IsAiming = false;
        aimAmount = 0f;
        if (aimCamera != null && normalFieldOfView > 0f) aimCamera.fieldOfView = normalFieldOfView;
        heldModels?.SetAim(aimCamera, current, 0f);
    }

    // 기존 재장전 시간은 빈 탄창 전체 기준. 단발 삽입 시 한 발당 시간으로 나눈다.
    private float ReloadStepSeconds()
    {
        float total = ReloadSeconds();
        return settings.reloadPerRound ? total / Mathf.Max(1, settings.magazineSize) : total;
    }

    // 예비탄이 모자라면 장전탄이 적은 손부터 채운다(같으면 손 0). provisional 규칙이다.
    private void CompleteReload()
    {
        if (!isReloading)
        {
            return;
        }
        if (settings.reloadPerRound)
        {
            if (reloadDuration <= 0f && inventory != null)
            {
                FillHandFromReserve(0);
                if (akimbo)
                {
                    FillHandFromReserve(1);
                }
                isReloading = false;
                AmmoChanged?.Invoke(Ammo, MagazineSize);
                return;
            }
            int hand = akimbo && offHand.Magazine < MainAmmo ? 1 : 0;
            if (HandAmmo(hand) >= HandMagazineSize(hand) && akimbo)
            {
                hand = 1 - hand;
            }
            if (inventory != null && HandAmmo(hand) < HandMagazineSize(hand) && inventory.Consume(AmmoId, 1) == 1)
            {
                if (hand == 0)
                {
                    MainAmmo++;
                }
                else
                {
                    offHand.Magazine++;
                }
            }
            if (NeedsReload() && ReserveAmmo > 0 && reloadDuration > 0f)
            {
                reloadEndTime = Time.time + reloadDuration;
            }
            else
            {
                isReloading = false;
            }
            AmmoChanged?.Invoke(Ammo, MagazineSize);
            return;
        }
        isReloading = false;
        if (inventory != null)
        {
            int first = akimbo && offHand.Magazine < MainAmmo ? 1 : 0;
            FillHandFromReserve(first);
            if (akimbo)
            {
                FillHandFromReserve(1 - first);
            }
        }
        AmmoChanged?.Invoke(Ammo, MagazineSize);
    }

    private void FillHandFromReserve(int _hand)
    {
        int added = inventory.Consume(AmmoId, Mathf.Max(0, HandMagazineSize(_hand) - HandAmmo(_hand)));
        if (_hand == 0)
        {
            MainAmmo += added;
        }
        else
        {
            offHand.Magazine += added;
        }
    }

    // 슬롯 탄약 심볼: 공유 예비탄을 차감하지 않고 사용 중인 무기 탄창을 즉시 채운다. 아킴보면 양손 모두 채운다.
    public void GrantSlotAmmo()
    {
        if (health != null && !health.IsAlive)
        {
            return;
        }
        MainAmmo = HandMagazineSize(0);
        if (akimbo)
        {
            offHand.Magazine = HandMagazineSize(1);
        }
        // 탄창이 다 찼으니 진행 중이던 재장전은 더 할 일이 없다(예비탄 소비 없이 끝낸다).
        if (isReloading && !NeedsReload())
        {
            isReloading = false;
        }
        AmmoChanged?.Invoke(Ammo, MagazineSize);
    }

    // 쏠 손을 고른다. 아킴보면 차례인 손이 빈 경우에만 반대 손으로 넘어간다(연사 간격 대기 중이면 기다린다).
    private int PickHand(float _now)
    {
        if (!akimbo)
        {
            return CanFireHand(0, _now) ? 0 : -1;
        }
        if (HandAmmo(nextHand) > 0)
        {
            return CanFireHand(nextHand, _now) ? nextHand : -1;
        }
        return CanFireHand(1 - nextHand, _now) ? 1 - nextHand : -1;
    }

    // 손의 다음 발사 가능 시각. 무기 개체에 기록한다.
    public float HandNextShotTime(int _hand)
    {
        var weapon = Hand(_hand);
        if (weapon != null)
        {
            return weapon.NextShotTime;
        }
        return _hand == 0 ? fallbackNextShotTime : 0f;
    }

    private void SetHandNextShotTime(int _hand, float _time)
    {
        var weapon = Hand(_hand);
        if (weapon != null)
        {
            weapon.NextShotTime = _time;
        }
        else if (_hand == 0)
        {
            fallbackNextShotTime = _time;
        }
    }

    private bool CanFireHand(int _hand, float _now)
    {
        var handSettings = HandSettings(_hand);
        float ready = HandNextShotTime(_hand);
        return HandAmmo(_hand) > 0 && _now >= ready && handSettings.shotsPerSecond > 0f && handSettings.range > 0f;
    }

    // 한 발 소비: 장전탄(탄 절감 판정 _saveRoll이 확률 미만이면 소비 없음), 그 손의 연사 간격, 다음 차례 손을 갱신한다.
    private void TakeShot(int _hand, float _now, float _saveRoll)
    {
        var handSettings = HandSettings(_hand);
        if (handSettings.ammoSaveChance <= 0f || _saveRoll >= handSettings.ammoSaveChance)
        {
            if (_hand == 0)
            {
                MainAmmo--;
            }
            else
            {
                offHand.Magazine--;
            }
        }
        SetHandNextShotTime(_hand, _now + 1f / handSettings.shotsPerSecond);
        LastFiredHand = _hand;
        nextHand = akimbo ? 1 - _hand : 0;
        AmmoChanged?.Invoke(Ammo, MagazineSize);
    }

    private void TryFire()
    {
        // 한 발씩 장전하는 무기는 이미 들어간 탄을 보존하고 사격으로 장전을 중단한다.
        if (isReloading && settings.reloadPerRound && PickHand(Time.time) >= 0)
        {
            CancelReload();
        }
        if (isReloading || aimCamera == null)
        {
            return;
        }
        int hand = PickHand(Time.time);
        if (hand < 0)
        {
            return;
        }
        var handSettings = HandSettings(hand);
        TakeShot(hand, Time.time, handSettings.ammoSaveChance > 0f ? UnityEngine.Random.value : 1f);
        Fired?.Invoke();
        HandFired?.Invoke(hand);
        SoundPlayer.Play(SfxType.Shot);
        Vector3 muzzle = aimCamera.transform.TransformPoint(hand == 0 ? MUZZLE_OFFSET : OFF_HAND_MUZZLE_OFFSET);
        if (hand == 0 && aimAmount > 0f && heldModels != null && heldModels.TryGetAimFront(current, out Vector3 sight))
        {
            muzzle = Vector3.Lerp(muzzle, sight + aimCamera.transform.forward * 0.03f, aimAmount);
        }
        EffectSpawner.Spawn(EffectSpawner.MUZZLE_FLASH, muzzle, aimCamera.transform.rotation);
        // 카메라→총구 사이가 벽·적에 가리면 카메라 위치에서 쏴서, 벽에 붙어 쏘거나 근접한 적을 쏠 때 관통·누락이 없게 한다.
        Vector3 origin = MuzzleBlocked(muzzle) ? aimCamera.transform.position : muzzle;

        // 즉발 판정 대신 발사체를 보낸다. 피해는 탄이 실제로 닿는 순간 Projectile이 준다.
        // 산탄은 같은 그룹 번호를 붙여, 전투 쪽이 발사 1회 저지력 합산 상한과 1회 넉백을 적용하게 한다.
        int pellets = Mathf.Max(1, handSettings.pellets);
        int group = pellets > 1 ? ++pelletGroupCounter : 0;
        var firedWeapon = Hand(hand);
        string firedWeaponId = firedWeapon != null ? firedWeapon.WeaponId : "";
        int shotId = GameTelemetry.PlayerShotFired(firedWeaponId, hand, pellets, Ammo, ReserveAmmo, akimbo, IsAiming);
        var shot = new Projectile.Shot
        {
            team = Projectile.Team.Player,
            damage = handSettings.damage,
            speed = handSettings.projectileSpeed,
            maxDistance = handSettings.range,
            lifetime = handSettings.projectileLifetime,
            radius = handSettings.projectileRadius,
            stoppingPower = handSettings.stoppingPower,
            knockback = handSettings.knockback,
            pelletGroup = group,
            pelletCount = pellets,
            pelletStopCap = handSettings.pelletStoppingPowerCap
        };
        for (int i = 0; i < pellets; i++)
        {
            // 탄은 조준선(카메라 시선)이 닿는 점으로 모인다. 총구 오프셋 때문에 탄착이 조준점에서 어긋나지 않게 한다.
            Vector3 view = SpreadDirection(aimCamera.transform, pellets > 1 ? handSettings.spreadDegrees : 0f);
            Vector3 direction = AimPoint(view, handSettings.range) - origin;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = view;
            }
            Projectile projectile = ProjectileSpawner.Launch(ProjectileSpawner.PLAYER_BULLET, shot, origin, direction, hitLayers, OwnerRoot());
            if (projectile != null)
            {
                projectile.Impacted += _impact => OnProjectileImpact(_impact, group, shotId, firedWeaponId);
            }
        }
    }

    // 카메라에서 _view 방향으로 사거리 안 첫 솔리드(자기 몸 제외)에 닿는 점. 없으면 사거리 끝 점이다.
    private Vector3 AimPoint(Vector3 _view, float _range)
    {
        Vector3 start = aimCamera.transform.position;
        float distance = _range;
        int count = Physics.RaycastNonAlloc(start, _view, AIM_BUFFER, distance, ~0, QueryTriggerInteraction.Ignore);
        Transform self = OwnerRoot();
        for (int i = 0; i < count; i++)
        {
            if (!AIM_BUFFER[i].collider.transform.IsChildOf(self) && AIM_BUFFER[i].distance < distance)
            {
                distance = AIM_BUFFER[i].distance;
            }
        }
        return start + _view * distance;
    }

    // 카메라에서 총구까지 자기 몸이 아닌 솔리드가 있는지.
    private bool MuzzleBlocked(Vector3 _muzzle)
    {
        Vector3 start = aimCamera.transform.position;
        Vector3 toMuzzle = _muzzle - start;
        float distance = toMuzzle.magnitude;
        if (distance <= Mathf.Epsilon)
        {
            return false;
        }
        int count = Physics.RaycastNonAlloc(start, toMuzzle / distance, AIM_BUFFER, distance, ~0, QueryTriggerInteraction.Ignore);
        Transform self = OwnerRoot();
        for (int i = 0; i < count; i++)
        {
            if (!AIM_BUFFER[i].collider.transform.IsChildOf(self))
            {
                return true;
            }
        }
        return false;
    }

    // 조준 방향 기준 원뿔 안의 무작위 방향.
    private static Vector3 SpreadDirection(Transform _aim, float _halfAngle)
    {
        if (_halfAngle <= 0f)
        {
            return _aim.forward;
        }
        Vector2 offset = UnityEngine.Random.insideUnitCircle * _halfAngle;
        return Quaternion.AngleAxis(offset.x, _aim.up) * Quaternion.AngleAxis(offset.y, _aim.right) * _aim.forward;
    }

    // 자기 몸 콜라이더는 탄이 무시한다. 기준은 PlayerHealth가 붙은 플레이어 루트다.
    private Transform OwnerRoot()
    {
        return health != null ? health.transform : transform.root;
    }

    // 탄이 적에게 실제로 피해를 준 경우에만 확정 적중을 올린다. 벽·지형 명중은 무시한다.
    // 산탄은 같은 발사 그룹의 머리·헬멧 명중을 누적해 알린다(마지막 산탄이 몸통이어도 헤드샷 표시 유지). 피해 계산은 산탄별 그대로다.
    private void OnProjectileImpact(Projectile.Impact _impact, int _group, int _shotId, string _weaponId)
    {
        if (!_impact.damagedEnemy)
        {
            return;
        }
        GameTelemetry.PlayerShotHit(_shotId, _weaponId, _impact.damageDealt, _impact.isHeadshot, _impact.isHelmetHit, _impact.killed, _impact.staggered);
        if (_group > 0)
        {
            if (_group != markerGroup)
            {
                markerGroup = _group;
                markerHead = false;
                markerHelmet = false;
            }
            markerHead |= _impact.isHeadshot;
            markerHelmet |= _impact.isHelmetHit;
            _impact.isHeadshot = markerHead;
            _impact.isHelmetHit = markerHelmet;
        }
        EnemyHit?.Invoke(_impact.killed);
        EnemyImpact?.Invoke(_impact);
    }
}
