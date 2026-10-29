using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerWeapon : MonoBehaviour
{
    [Serializable]
    public struct WeaponSettings
    {
        [Min(0)] public int magazineSize;
        [Min(0f)] public float damage;
        [Min(0f)] public float range;
        [Min(0f)] public float shotsPerSecond;
        [Min(0f)] public float reloadSeconds;
        public bool automatic;
        // 발사체 탄도. 최대 비행 거리는 range를 쓴다.
        [Min(0f)] public float projectileSpeed;
        [Min(0f)] public float projectileLifetime;
        [Min(0f)] public float projectileRadius;
    }

    [SerializeField] private InputActionReference fireAction;
    [SerializeField] private InputActionReference reloadAction;
    [SerializeField] private InputActionReference pickupAction;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private PlayerHealth health;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private LayerMask hitLayers;
    [SerializeField] private WeaponSettings settings;

    // 카메라 기준 총구 위치(근사). PocSceneBuilder가 만드는 리볼버 모델 위치(0.25, -0.22, 0.6)보다 조금 앞이다.
    private static readonly Vector3 MUZZLE_OFFSET = new Vector3(0.25f, -0.22f, 0.9f);

    private float nextShotTime;
    private float reloadEndTime;
    private bool isReloading;

    public int Ammo { get; private set; }
    public int MagazineSize => settings.magazineSize;
    public bool IsReloading => isReloading;
    // 재장전 진행률 0~1. 재장전 중이 아니면 0이다. 즉시 재장전(reloadSeconds 0)은 항상 0이다.
    public float ReloadProgress
    {
        get
        {
            if (!isReloading || settings.reloadSeconds <= 0f)
            {
                return 0f;
            }
            return Mathf.Clamp01(1f - (reloadEndTime - Time.time) / settings.reloadSeconds);
        }
    }
    // 발사체가 실제로 살아 있던 적에게 피해를 준 순간에만 나간다. bool은 그 피해로 처치됐는지다. 벽·지형 명중에는 나가지 않는다.
    public event Action<bool> EnemyHit;
    public event Action<int, int> AmmoChanged;
    public event Action Fired;

    private void Awake()
    {
        Ammo = Mathf.Max(0, settings.magazineSize);
    }

    private void OnEnable()
    {
        fireAction?.action?.Enable();
        reloadAction?.action?.Enable();
        pickupAction?.action?.Enable();
    }

    private void OnDisable()
    {
        fireAction?.action?.Disable();
        reloadAction?.action?.Disable();
        pickupAction?.action?.Disable();
    }

    private void Update()
    {
        if (health != null && !health.IsAlive)
        {
            isReloading = false;
            return;
        }

        // 정비 상점·게임 오버 중에는 사격·재장전을 막는다. 슬롯 회전 중에는 막지 않는다.
        if (!InputGate.PlayerEnabled)
        {
            return;
        }

        if (isReloading && Time.time >= reloadEndTime)
        {
            RefillImmediately();
        }

        bool wantsReload = reloadAction != null && reloadAction.action != null && reloadAction.action.WasPressedThisFrame();
        // 이번 프레임에 픽업 키로 줍기가 예약됐으면 재장전보다 줍기가 앞선다(게임패드 X는 두 동작에 같이 묶여 있다).
        // 예약은 PlayerInteractor가 프레임 단위로 한 번 정하므로 두 컴포넌트의 Update 순서와 상관없이 어느 총도 재장전하지 않는다.
        // 줍기 예약이 없으면 R이든 X든 그대로 재장전한다.
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

    // 새로 구매한 무기는 탄창을 채운 채 든다.
    public void Configure(WeaponSettings _settings)
    {
        Configure(_settings, _settings.magazineSize);
    }

    // 바닥 픽업을 주운 경우 놓을 때의 탄약을 그대로 이어받는다. 재장전은 취소하고 탄약은 채우지 않는다.
    public void Configure(WeaponSettings _settings, int _ammo)
    {
        settings = _settings;
        isReloading = false;
        nextShotTime = 0f;
        Ammo = Mathf.Clamp(_ammo, 0, Mathf.Max(0, settings.magazineSize));
        AmmoChanged?.Invoke(Ammo, settings.magazineSize);
    }

    // 이번 프레임 줍기 예약 여부. 이전 프레임 조준 캐시가 아니라 이번 프레임의 픽업 키 입력과 조준으로 정해진다.
    private bool ReloadBlockedByPickupInView()
    {
        return interactor != null && interactor.PickupClaimThisFrame != null;
    }

    // 무기를 내려놓을 때 재장전을 끊는다. 진행 중이던 재장전은 완료되지 않으며 탄약 이득도 없다.
    public void CancelReload()
    {
        isReloading = false;
    }

    public void StartReload()
    {
        if ((health != null && !health.IsAlive) || isReloading || settings.magazineSize <= 0 || Ammo >= settings.magazineSize)
        {
            return;
        }

        isReloading = true;
        reloadEndTime = Time.time + Mathf.Max(0f, settings.reloadSeconds);
        if (settings.reloadSeconds <= 0f)
        {
            RefillImmediately();
        }
    }

    public void RefillImmediately()
    {
        isReloading = false;
        Ammo = Mathf.Max(0, settings.magazineSize);
        AmmoChanged?.Invoke(Ammo, settings.magazineSize);
    }

    private void TryFire()
    {
        if (isReloading || Ammo <= 0 || Time.time < nextShotTime || settings.shotsPerSecond <= 0f || settings.range <= 0f || aimCamera == null)
        {
            return;
        }

        Ammo--;
        nextShotTime = Time.time + 1f / settings.shotsPerSecond;
        AmmoChanged?.Invoke(Ammo, settings.magazineSize);
        Fired?.Invoke();
        SoundPlayer.Play(SfxType.Shot);
        Vector3 origin = aimCamera.transform.TransformPoint(MUZZLE_OFFSET);
        EffectSpawner.Spawn(EffectSpawner.MUZZLE_FLASH, origin, aimCamera.transform.rotation);

        // 즉발 판정 대신 발사체를 보낸다. 피해는 탄이 실제로 닿는 순간 Projectile이 준다.
        var shot = new Projectile.Shot
        {
            team = Projectile.Team.Player,
            damage = settings.damage,
            speed = settings.projectileSpeed,
            maxDistance = settings.range,
            lifetime = settings.projectileLifetime,
            radius = settings.projectileRadius
        };
        Projectile projectile = ProjectileSpawner.Launch(ProjectileSpawner.PLAYER_BULLET, shot, origin, aimCamera.transform.forward, hitLayers, OwnerRoot());
        if (projectile != null)
        {
            projectile.Impacted += OnProjectileImpact;
        }
    }

    // 자기 몸 콜라이더는 탄이 무시한다. 기준은 PlayerHealth가 붙은 플레이어 루트다.
    private Transform OwnerRoot()
    {
        return health != null ? health.transform : transform.root;
    }

    // 탄이 적에게 실제로 피해를 준 경우에만 확정 적중을 올린다. 벽·지형 명중은 무시한다.
    private void OnProjectileImpact(Projectile.Impact _impact)
    {
        if (_impact.damagedEnemy)
        {
            EnemyHit?.Invoke(_impact.killed);
        }
    }
}
