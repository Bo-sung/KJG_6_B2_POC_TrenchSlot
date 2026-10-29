using System;
using System.Collections;
using UnityEngine;

// 한 판의 흐름을 관리한다. 웨이브(슬롯 가능) → 정비(상점) → 다음 웨이브를 반복하며, 플레이어 사망 시 종료한다.
// 소지금·퍽·무기 보유는 모델(PlayerWallet·PerkInventory·WeaponLoadout)로 들고, 화면은 Presenter가 이벤트를 받아 갱신한다.
public sealed class GameManager : MonoBehaviour
{
    public enum GamePhase
    {
        Wave,
        Shop,
        GameOver
    }

    [SerializeField] private WaveController waves;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerWeapon weapon;
    [SerializeField] private PlayerInteractor interactor;
    // 무기 id → 바닥 픽업 프리팹. 떨어뜨리기 전에 존재를 확인한다.
    [SerializeField] private WeaponPickupCatalog pickups;
    // 든 무기 외형. 장착 id에 맞는 모델만 켠다.
    [SerializeField] private WeaponHeldModels heldModels;
    // 바닥 무기의 부모. 비워 두면 이 오브젝트 아래에 둔다. 사망 시 여기 있는 바닥 무기를 모두 치운다.
    [SerializeField] private Transform dropRoot;

    // 자리 확보 검사용 물리 질의 버퍼(한 번에 겹친 콜라이더를 받는다).
    private static readonly Collider[] OVERLAP_BUFFER = new Collider[16];
    private static readonly float[] DROP_ANGLES = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
    // 바닥 탐색: 플레이어 발밑 기준 위 1m에서 아래로 쏜다. 바닥 법선이 이보다 기울면 걷기 어려운 면으로 본다.
    private const float FLOOR_PROBE_HEIGHT = 1f;
    private const float FLOOR_PROBE_DEPTH = 2f;
    private const float FLOOR_MIN_NORMAL_Y = 0.7f;
    private const float FLOOR_MAX_STEP = 0.3f;
    // 놓을 무기 상자를 바닥에서 띄우는 간격과, 플레이어→놓을 자리 시선을 쏘는 플레이어 발밑 기준 높이.
    private const float DROP_BOX_SKIN = 0.03f;
    private const float DROP_SIGHT_HEIGHT = 0.9f;
    private static readonly RaycastHit[] SIGHT_BUFFER = new RaycastHit[16];

    private PlayerWallet wallet;
    private PerkInventory perks;
    private WeaponLoadout loadout;
    private GamePhase phase;
    private WaveSpec currentWave;
    private float waveTimeLeft;
    private float intermissionLeft;
    private int killsThisWave;
    private int totalKills;
    private int lastCompletedWave;

    // 완료 결과 모델. Presenter는 설정값 대신 실제 적용된 회복·지급을 보여 준다.
    public float WaveHealthRestored { get; private set; }
    public int WaveBonusPaid { get; private set; }

    public GamePhase Phase => phase;
    public int WaveNumber => currentWave.wave;
    public WaveSpec CurrentWave => currentWave;
    public float WaveTimeLeft => waveTimeLeft;
    public float IntermissionLeft => intermissionLeft;
    public int KillsThisWave => killsThisWave;
    public int TotalKills => totalKills;
    public PlayerWallet Wallet => wallet;
    public PerkInventory Perks => perks;
    public WeaponLoadout Loadout => loadout;
    public bool IsWaveActive => phase == GamePhase.Wave;
    // 지금 체력 회복을 살 수 있는지(살아 있고 최대 체력 미만). 상점 칸 활성 상태와 구매 판정이 같이 쓴다.
    public bool CanHeal => playerHealth != null && playerHealth.IsAlive && ShopInfo.CanHeal(playerHealth.CurrentHealth, playerHealth.MaxHealth);

    public event Action<GamePhase> PhaseChanged;
    public event Action<WaveSpec> WaveStarted;
    public event Action<float, float> WaveTimeChanged;
    // 적 전멸 시 남은 웨이브 시간(초). 이 시간 동안 슬롯을 돌릴 수 있다.
    public event Action<float> EnemiesCleared;
    public event Action<int> WaveEnded;
    public event Action<float> IntermissionChanged;
    public event Action<int> KillsChanged;
    public event Action<int, int> GameOver;

    private void Awake()
    {
        var table = TableManager.Instance;
        var config = table.GameConfig;
        InputGate.PlayerEnabled = true;
        wallet = new PlayerWallet(GameConfigInfo.StartMoney(config));
        perks = new PerkInventory(table.Perk);
        loadout = new WeaponLoadout(WeaponInfo.StartWeaponId(table.Weapon));

        if (playerHealth != null)
        {
            playerHealth.Configure(GameConfigInfo.MaxHealth(config));
            playerHealth.Died += OnPlayerDied;
        }
        if (waves != null)
        {
            waves.EnemyKilled += OnEnemyKilled;
            waves.Cleared += OnEnemiesCleared;
        }
        if (interactor != null)
        {
            interactor.Configure(GameConfigInfo.InteractDistance(config));
            interactor.CanPickup = CanPickupTarget;
            interactor.PickupPressed += OnPickupPressed;
        }
        ApplyWeapon(loadout.EquippedId, FullMagazine(loadout.EquippedId));
    }

    private void Start()
    {
        StartCoroutine(StartFirstWaveNextFrame());
    }

    // 프레젠터가 이벤트를 구독할 시간을 한 프레임 준다.
    private IEnumerator StartFirstWaveNextFrame()
    {
        yield return null;
        StartWave(1);
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -= OnPlayerDied;
        }
        if (waves != null)
        {
            waves.EnemyKilled -= OnEnemyKilled;
            waves.Cleared -= OnEnemiesCleared;
        }
        if (interactor != null)
        {
            interactor.PickupPressed -= OnPickupPressed;
        }
    }

    private void Update()
    {
        if (phase == GamePhase.Wave)
        {
            waveTimeLeft = Mathf.Max(0f, waveTimeLeft - Time.deltaTime);
            WaveTimeChanged?.Invoke(waveTimeLeft, currentWave.durationSeconds);
            if (waveTimeLeft <= 0f)
            {
                EndWave();
            }
            return;
        }

        if (phase == GamePhase.Shop && GameConfigInfo.IntermissionSeconds(TableManager.Instance.GameConfig) > 0f)
        {
            intermissionLeft = Mathf.Max(0f, intermissionLeft - Time.deltaTime);
            IntermissionChanged?.Invoke(intermissionLeft);
            if (intermissionLeft <= 0f)
            {
                StartNextWave();
            }
        }
    }

    public void StartNextWave()
    {
        if (phase != GamePhase.Shop)
        {
            return;
        }
        StartWave(currentWave.wave + 1);
    }

    private void StartWave(int _wave)
    {
        WaveHealthRestored = 0f;
        WaveBonusPaid = 0;
        currentWave = WaveInfo.Resolve(TableManager.Instance.Wave, _wave);
        waveTimeLeft = currentWave.durationSeconds;
        killsThisWave = 0;
        SetPhase(GamePhase.Wave);
        waves.Begin(currentWave, TableManager.Instance.Enemy, TableManager.Instance.EnemyWeapon, playerHealth);
        WaveStarted?.Invoke(currentWave);
        WaveTimeChanged?.Invoke(waveTimeLeft, currentWave.durationSeconds);
        KillsChanged?.Invoke(killsThisWave);
    }

    // 시간 종료: 남은 적은 치우고 정비 상점을 연다. 진행 중이던 슬롯 회전은 끝까지 지급된다.
    private void EndWave()
    {
        // 전멸은 슬롯 시간을 남긴다. 초기화/조기 호출/사망/중복 호출은 완료가 아니다.
        if (phase != GamePhase.Wave || currentWave.wave <= lastCompletedWave || waveTimeLeft > 0f ||
            playerHealth == null || !playerHealth.IsAlive)
        {
            return;
        }
        // Heal/Wallet 이벤트를 통한 재진입보다 먼저 기록한다(웨이브당 한 번).
        lastCompletedWave = currentWave.wave;
        waves.Stop();
        var config = TableManager.Instance.GameConfig;
        float healthBefore = playerHealth.CurrentHealth;
        playerHealth.Heal(GameConfigInfo.WaveClearHealAmount(config));
        WaveHealthRestored = playerHealth.CurrentHealth - healthBefore;
        WaveBonusPaid = GameConfigInfo.WaveClearBonus(config, currentWave.wave);
        wallet.Add(WaveBonusPaid);
        WaveEnded?.Invoke(currentWave.wave);
        intermissionLeft = GameConfigInfo.IntermissionSeconds(config);
        SetPhase(GamePhase.Shop);
        IntermissionChanged?.Invoke(intermissionLeft);
    }

    private void SetPhase(GamePhase _next)
    {
        phase = _next;
        PhaseChanged?.Invoke(phase);
    }

    private void OnEnemyKilled(Enemy _enemy)
    {
        wallet.Add(_enemy.Reward);
        killsThisWave++;
        totalKills++;
        KillsChanged?.Invoke(killsThisWave);
    }

    private void OnEnemiesCleared()
    {
        if (phase != GamePhase.Wave)
        {
            return;
        }
        EnemiesCleared?.Invoke(waveTimeLeft);
    }

    private void OnPlayerDied()
    {
        if (phase == GamePhase.GameOver)
        {
            return;
        }
        waves.Stop();
        ClearGroundWeapons();
        SetPhase(GamePhase.GameOver);
        GameOver?.Invoke(currentWave.wave, totalKills);
    }

    // 정비 상점 구매. 실패 사유를 문자열로 돌려준다(빈 문자열이면 성공).
    public string TryBuy(ShopData.Item _item)
    {
        if (_item == null)
        {
            return "항목이 없습니다.";
        }
        if (phase != GamePhase.Shop)
        {
            return "정비 시간에만 구매할 수 있습니다.";
        }

        switch (_item.type)
        {
            case ShopInfo.TYPE_WEAPON:
                return BuyWeapon(_item);
            case ShopInfo.TYPE_HEAL:
                return BuyHeal(_item);
            case ShopInfo.TYPE_PERK:
                return BuyPerk(_item);
            default:
                return "알 수 없는 항목입니다.";
        }
    }

    // 구매는 한 번만 한다. 새 무기는 탄창을 채워 들고, 들고 있던 무기는 발밑 근처 빈 바닥에 탄약 그대로 떨어뜨린다.
    // 돈을 쓰기 전에 자리·프리팹을 모두 확인하므로, 실패하면 소지금과 현재 무기가 그대로 남는다.
    private string BuyWeapon(ShopData.Item _item)
    {
        string target = _item.target;
        if (loadout.IsOwned(target))
        {
            return "이미 획득한 무기입니다. 바닥에 놓인 무기를 주워 장착하세요.";
        }
        if (WeaponInfo.Find(TableManager.Instance.Weapon, target) == null || !pickups.Has(target))
        {
            return "무기 데이터를 찾지 못했습니다.";
        }
        if (!wallet.CanAfford(_item.price))
        {
            return "소지금이 부족합니다.";
        }

        string outgoing = loadout.EquippedId;
        Vector3 spot = Vector3.zero;
        if (!string.IsNullOrEmpty(outgoing))
        {
            if (!pickups.Has(outgoing))
            {
                return "무기 픽업 프리팹을 찾지 못했습니다.";
            }
            if (!TryFindDropSpot(out spot, null, pickups.Find(outgoing)))
            {
                return "들고 있는 무기를 놓을 빈 바닥이 없습니다.";
            }
        }

        if (!wallet.TrySpend(_item.price))
        {
            return "소지금이 부족합니다.";
        }
        if (!string.IsNullOrEmpty(outgoing))
        {
            int outgoingAmmo = weapon.Ammo;
            weapon.CancelReload();
            SpawnGroundWeapon(outgoing, outgoingAmmo, spot);
        }
        loadout.Own(target);
        loadout.Equip(target);
        ApplyWeapon(target, FullMagazine(target));
        return "";
    }

    // 바닥 픽업 줍기. 들고 있던 무기를 대신 놓고, 주운 무기는 놓였을 때의 탄약을 그대로 이어받는다(재장전 이득 없음).
    // 조준 대상 픽업은 줍는 순간 한 번만 소비된다. 자리를 찾지 못하면 아무것도 바뀌지 않는다.
    // 직접 호출이나 지난 프레임 조준으로 들어와도, 지금 카메라 시선·거리·생존·단계·소비 여부를 다시 확인한 뒤에만 바꾼다.
    public string TryPickup(WeaponPickup _pickup)
    {
        if (!CanPickupTarget(_pickup) || interactor == null || !interactor.IsPickupInView(_pickup))
        {
            return "주울 수 없는 무기입니다.";
        }
        string outgoing = loadout.EquippedId;
        Vector3 spot = Vector3.zero;
        if (!string.IsNullOrEmpty(outgoing) && !TryFindDropSpot(out spot, _pickup, pickups.Find(outgoing)))
        {
            return "들고 있는 무기를 놓을 빈 바닥이 없습니다.";
        }

        string picked = _pickup.WeaponId;
        int pickedAmmo = _pickup.Ammo;
        // 소비를 먼저 확정한다. 같은 프레임에 다시 들어온 요청은 여기서 막혀 무기가 복제되지 않는다.
        if (!_pickup.Consume())
        {
            return "주울 수 없는 무기입니다.";
        }
        if (!string.IsNullOrEmpty(outgoing))
        {
            int outgoingAmmo = weapon.Ammo;
            weapon.CancelReload();
            SpawnGroundWeapon(outgoing, outgoingAmmo, spot);
        }
        loadout.Own(picked);
        loadout.Equip(picked);
        ApplyWeapon(picked, pickedAmmo);
        return "";
    }

    // 픽업 프롬프트와 입력이 같이 쓰는 판정. 정비 시간이 아니거나, 이미 든 무기이거나, 놓을 자리가 없으면 false다.
    private bool CanPickupTarget(WeaponPickup _pickup)
    {
        if (_pickup == null || !_pickup.isActiveAndEnabled || phase != GamePhase.Wave || _pickup.WeaponId == loadout.EquippedId)
        {
            return false;
        }
        if (WeaponInfo.Find(TableManager.Instance.Weapon, _pickup.WeaponId) == null)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(loadout.EquippedId) && !pickups.Has(loadout.EquippedId))
        {
            return false;
        }
        // 줍기가 한 번만 소비되는지, 플레이어와 픽업 간 실제 도달 가능한 거리인지 재검증한다.
        if (_pickup.IsConsumed)
        {
            return false;
        }

        float reachLimit = GameConfigInfo.InteractDistance(TableManager.Instance.GameConfig);
        bool inRange = playerHealth != null && playerHealth.IsAlive &&
            Vector3.Distance(_pickup.transform.position, playerHealth.transform.position) <= reachLimit + 0.5f;

        return inRange && (string.IsNullOrEmpty(loadout.EquippedId) || TryFindDropSpot(out _, _pickup, pickups.Find(loadout.EquippedId)));
    }

    private void OnPickupPressed(WeaponPickup _pickup)
    {
        string failure = TryPickup(_pickup);
        if (failure != "")
        {
            Debug.LogWarning(failure);
        }
    }

    private string BuyHeal(ShopData.Item _item)
    {
        if (!CanHeal)
        {
            return "체력이 가득 차 있습니다.";
        }
        if (!wallet.TrySpend(_item.price))
        {
            return "소지금이 부족합니다.";
        }
        playerHealth.Heal(GameConfigInfo.HealAmount(TableManager.Instance.GameConfig));
        return "";
    }

    private string BuyPerk(ShopData.Item _item)
    {
        if (!perks.CanBuy(_item.target))
        {
            return "이미 최대 스택입니다.";
        }
        if (!wallet.TrySpend(_item.price))
        {
            return "소지금이 부족합니다.";
        }
        perks.Buy(_item.target);
        return "";
    }

    // 무기 적용. 주운 무기는 저장된 탄약을, 새로 산 무기는 탄창을 가득 채워 준다.
    private void ApplyWeapon(string _weaponId, int _ammo)
    {
        var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weaponId);
        if (entry == null || weapon == null)
        {
            Debug.LogError($"무기 {_weaponId}를 적용하지 못했습니다.");
            return;
        }
        weapon.Configure(new PlayerWeapon.WeaponSettings
        {
            magazineSize = entry.magazineSize,
            damage = entry.damage,
            range = entry.range,
            shotsPerSecond = entry.shotsPerSecond,
            reloadSeconds = entry.reloadSeconds,
            automatic = entry.automatic,
            projectileSpeed = entry.projectileSpeed,
            projectileLifetime = entry.projectileLifetime,
            projectileRadius = entry.projectileRadius
        }, _ammo);
        if (heldModels != null)
        {
            heldModels.Show(_weaponId);
        }
    }

    private int FullMagazine(string _weaponId)
    {
        var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weaponId);
        return entry != null ? entry.magazineSize : 0;
    }

    // 바닥 무기는 픽업 프리팹을 복제해 둔다. 게임플레이 스폰(드롭)이라 런타임 생성을 허용한다.
    private void SpawnGroundWeapon(string _weaponId, int _ammo, Vector3 _position)
    {
        var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weaponId);
        WeaponPickup drop = Instantiate(pickups.Find(_weaponId), _position, Quaternion.identity, GroundRoot());
        drop.Initialize(_weaponId, entry.name, _ammo);
        // 같은 프레임의 다음 자리 판정이 방금 놓은 무기를 보도록 물리 상태를 맞춘다.
        Physics.SyncTransforms();
    }

    // 사망 시 바닥 무기를 모두 치운다. 다음 판은 씬 재시작으로 시작하며, 그때 소지 무기도 시작 무기로 돌아간다.
    private void ClearGroundWeapons()
    {
        Transform root = GroundRoot();
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            if (root.GetChild(i).GetComponent<WeaponPickup>() != null)
            {
                Destroy(root.GetChild(i).gameObject);
            }
        }
    }

    private Transform GroundRoot() => dropRoot != null ? dropRoot : transform;

    // 플레이어 발밑 주변에서 놓을 자리를 찾는다. _ignore는 줍는 대상 픽업이라 자리 판정에서 뺀다.
    // 자리 조건: 발밑과 같은 높이의 걷기 쉬운 바닥이고, 자리 확보 구 안과 바로 위에 플레이어가 아닌 솔리드·다른 픽업이 없어야 한다.
    // _dropPrefab이 있으면 그 프리팹의 실제 콜라이더 상자가 들어갈 공간과, 플레이어에서 그 자리까지 가리는 것이 없는지도 본다.
    private bool TryFindDropSpot(out Vector3 _spot, WeaponPickup _ignore, WeaponPickup _dropPrefab)
    {
        _spot = Vector3.zero;
        var drop = WeaponInfo.Drop(TableManager.Instance.Weapon);
        if (drop == null || playerHealth == null)
        {
            return false;
        }
        Transform player = playerHealth.transform;
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        if (forward == Vector3.zero)
        {
            forward = Vector3.forward;
        }
        float[] distances = { drop.minDistance, drop.maxDistance };
        foreach (float distance in distances)
        {
            foreach (float angle in DROP_ANGLES)
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
                Vector3 candidate = player.position + direction * distance;
                if (TryFloorAt(player, candidate, drop, _ignore, _dropPrefab, out Vector3 floor))
                {
                    _spot = floor;
                    return true;
                }
            }
        }
        return false;
    }

    private bool TryFloorAt(Transform _player, Vector3 _horizontal, WeaponData.DropSettings _drop, WeaponPickup _ignore, WeaponPickup _dropPrefab, out Vector3 _floor)
    {
        _floor = Vector3.zero;
        Vector3 top = new Vector3(_horizontal.x, _player.position.y + FLOOR_PROBE_HEIGHT, _horizontal.z);
        if (!Physics.Raycast(top, Vector3.down, out RaycastHit floorHit, FLOOR_PROBE_HEIGHT + FLOOR_PROBE_DEPTH, ~0, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        if (floorHit.normal.y < FLOOR_MIN_NORMAL_Y || Mathf.Abs(floorHit.point.y - _player.position.y) > FLOOR_MAX_STEP)
        {
            return false;
        }
        // 픽업 위는 바닥이 아니다. 줍는 대상이라도 그 윗면에는 놓지 않는다.
        if (floorHit.collider.GetComponentInParent<WeaponPickup>() != null)
        {
            return false;
        }
        if (IsBlockedBySolid(_player, floorHit.point, _drop, _ignore))
        {
            return false;
        }
        if (_dropPrefab != null && IsDropBodyBlocked(_player, floorHit.point, _dropPrefab, _ignore))
        {
            return false;
        }
        _floor = floorHit.point;
        return true;
    }

    // 자리 확보 구와 그 위 공간에 플레이어가 아닌 솔리드나 다른 픽업이 있으면 막힌 것이다.
    private bool IsBlockedBySolid(Transform _player, Vector3 _floor, WeaponData.DropSettings _drop, WeaponPickup _ignore)
    {
        Vector3 center = _floor + Vector3.up * (_drop.clearanceHeight * 0.5f);
        int count = Physics.OverlapSphereNonAlloc(center, _drop.clearanceRadius, OVERLAP_BUFFER, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsPlayerOrIgnored(OVERLAP_BUFFER[i], _player, _ignore))
            {
                return true;
            }
        }
        if (Physics.Raycast(_floor + Vector3.up * 0.05f, Vector3.up, out RaycastHit ceiling, _drop.clearanceHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
        {
            return !IsPlayerOrIgnored(ceiling.collider, _player, _ignore);
        }
        return false;
    }

    // 놓을 프리팹 루트 BoxCollider(생성 회전은 identity) 크기의 상자가 비어 있고, 플레이어 몸에서 상자 중심까지 시선이 트여 있어야 한다.
    // 벽 너머 바닥이나 긴 총몸이 벽을 관통하는 자리를 거른다.
    private bool IsDropBodyBlocked(Transform _player, Vector3 _floor, WeaponPickup _dropPrefab, WeaponPickup _ignore)
    {
        BoxCollider box = _dropPrefab.GetComponent<BoxCollider>();
        Vector3 scale = _dropPrefab.transform.localScale;
        Vector3 localCenter = box != null ? box.center : Vector3.zero;
        Vector3 halfExtents = box != null ? Vector3.Scale(box.size, scale) * 0.5f : Vector3.one * 0.1f;
        halfExtents = new Vector3(Mathf.Abs(halfExtents.x), Mathf.Abs(halfExtents.y), Mathf.Abs(halfExtents.z));
        Vector3 center = _floor + Vector3.Scale(localCenter, scale) + Vector3.up * DROP_BOX_SKIN;
        center.y = Mathf.Max(center.y, _floor.y + halfExtents.y + DROP_BOX_SKIN);

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, OVERLAP_BUFFER, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsPlayerOrIgnored(OVERLAP_BUFFER[i], _player, _ignore))
            {
                return true;
            }
        }

        Vector3 eye = _player.position + Vector3.up * DROP_SIGHT_HEIGHT;
        Vector3 toDrop = center - eye;
        float distance = toDrop.magnitude;
        if (distance <= Mathf.Epsilon)
        {
            return false;
        }
        int hits = Physics.RaycastNonAlloc(eye, toDrop / distance, SIGHT_BUFFER, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            if (!IsPlayerOrIgnored(SIGHT_BUFFER[i].collider, _player, _ignore))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPlayerOrIgnored(Collider _collider, Transform _player, WeaponPickup _ignore)
    {
        if (_collider.transform.IsChildOf(_player))
        {
            return true;
        }
        WeaponPickup pickup = _collider.GetComponentInParent<WeaponPickup>();
        return pickup != null && pickup == _ignore;
    }
}
