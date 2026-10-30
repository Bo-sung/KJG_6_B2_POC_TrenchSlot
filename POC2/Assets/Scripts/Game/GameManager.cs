using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 한 판의 흐름을 관리한다. 웨이브(슬롯 가능) → 정비(상점) → 다음 웨이브를 반복하며, 플레이어 사망 시 종료한다.
// 소지금·퍽·무기 슬롯·파츠·스탯은 모델로 들고, 화면은 Presenter가 이벤트를 받아 갱신한다.
// 상점 구매 판정(TryBuy*/TryAttach*)은 여기서 하며 실패 사유를 문자열로 돌려준다(빈 문자열이면 성공).
public sealed partial class GameManager : MonoBehaviour
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
    // 이동속도 배율을 적용할 대상. 비워 두면 플레이어에서 찾는다.
    [SerializeField] private PlayerMovement movement;
    // 외형 id → 바닥 픽업 프리팹. 떨어뜨리기 전에 존재를 확인한다.
    [SerializeField] private WeaponPickupCatalog pickups;
    // 든 무기 외형. 사용 중 무기의 외형 id에 맞는 모델만 켠다.
    [SerializeField] private WeaponHeldModels heldModels;
    // 바닥 무기의 부모. 비워 두면 이 오브젝트 아래에 둔다. 사망 시 여기 있는 바닥 무기를 모두 치운다.
    [SerializeField] private Transform dropRoot;
    [SerializeField] private AmmoPickup ammoPickupPrefab;

    private const string AMMO_PICKUP_PATH = "Pickups/AmmoPickup";
    private readonly HashSet<AmmoPickup> ammoDrops = new HashSet<AmmoPickup>();

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
    private PartInventory parts;
    private PlayerStats stats;
    private SlotUpgradeOffers slotOffers;
    private AmmoInventory ammo;
    private AmmoShop ammoShop;
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
    // 상점 진열 기준 웨이브. 정비 중에는 다음에 시작할 웨이브, 웨이브 중에는 현재 웨이브다.
    public int ShopWave => phase == GamePhase.Wave ? Mathf.Max(1, currentWave.wave) : currentWave.wave + 1;
    public WaveSpec CurrentWave => currentWave;
    public float WaveTimeLeft => waveTimeLeft;
    public float IntermissionLeft => intermissionLeft;
    public int KillsThisWave => killsThisWave;
    public int TotalKills => totalKills;
    public PlayerWallet Wallet => wallet;
    public PerkInventory Perks => perks;
    public WeaponLoadout Loadout => loadout;
    public PartInventory Parts => parts;
    public PlayerStats Stats => stats;
    public SlotUpgradeOffers SlotOffers => slotOffers;
    public AmmoInventory Ammo => ammo;
    public bool IsWaveActive => phase == GamePhase.Wave;
    // 아킴보: 두 슬롯의 같은 권총을 양손에 든다. 전환은 정비 시간에만 무료로 한다(TrySetAkimbo).
    public bool IsAkimbo => loadout != null && loadout.Akimbo;
    // 지금 아킴보를 켤 수 있는지. 이미 켜져 있으면 false다.
    public bool CanAkimbo => !IsAkimbo && AkimboUnavailableReason == "";
    // 지금 체력 회복을 살 수 있는지(살아 있고 최대 체력 미만). 상점 칸 활성 상태와 구매 판정이 같이 쓴다.
    public bool CanHeal => playerHealth != null && playerHealth.IsAlive && ShopInfo.CanHeal(playerHealth.CurrentHealth, playerHealth.MaxHealth);
    public float CurrentHealth => playerHealth != null ? playerHealth.CurrentHealth : 0f;
    public float MaxHealth => playerHealth != null ? playerHealth.MaxHealth : 0f;

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
        loadout = new WeaponLoadout(GameConfigInfo.WeaponSlotCount(config));
        parts = new PartInventory(table.Part);
        stats = new PlayerStats(table.Shop);
        slotOffers = new SlotUpgradeOffers(table.Shop, perks, Environment.TickCount);
        ammo = new AmmoInventory(table.Ammo);
        ammoShop = new AmmoShop(table.Shop, ammo, wallet);
        if (movement == null && playerHealth != null)
        {
            movement = playerHealth.GetComponent<PlayerMovement>();
        }
        if (ammoPickupPrefab == null)
        {
            var prefab = Resources.Load<GameObject>(AMMO_PICKUP_PATH);
            ammoPickupPrefab = prefab != null ? prefab.GetComponent<AmmoPickup>() : null;
        }
        if (ammoPickupPrefab == null)
        {
            Debug.LogError("탄약 픽업 프리팹을 찾지 못했습니다.");
        }

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
            interactor.PickupPromptText = PickupPrompt;
            interactor.PickupPressed += OnPickupPressed;
        }
        if (weapon != null)
        {
            weapon.SlotSelected += OnSlotSelected;
        }
        string startId = WeaponInfo.StartWeaponId(table.Weapon);
        if (!string.IsNullOrEmpty(startId))
        {
            loadout.Put(0, NewWeapon(startId));
        }
        ApplyActiveWeapon();
    }

    private void Start()
    {
        InitializeLogging();
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
        EndLogging("unknown");
        DetachCombatLogging();
        ClearAmmoDrops();
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
        if (weapon != null)
        {
            weapon.SlotSelected -= OnSlotSelected;
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
        ObserveLogging(() => telemetry.BeginWave(CaptureLogPlan(), CaptureLogPlayer()));
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
        LogEnemySnapshot enemiesBeforeCleanup = null;
        ObserveLogging(() => enemiesBeforeCleanup = CaptureLogEnemies());
        waves.Stop();
        var config = TableManager.Instance.GameConfig;
        float healthBefore = playerHealth.CurrentHealth;
        playerHealth.Heal(GameConfigInfo.WaveClearHealAmount(config));
        WaveHealthRestored = playerHealth.CurrentHealth - healthBefore;
        WaveBonusPaid = GameConfigInfo.WaveClearBonus(config, currentWave.wave);
        wallet.Add(WaveBonusPaid);
        // 진열된 슬롯 업그레이드를 전부 샀다면 새로 추첨한다. 일부만 샀으면 유지한다.
        slotOffers.OnWaveEnded();
        ObserveLogging(() => telemetry.CompleteWave(CaptureLogPlayer(), enemiesBeforeCleanup, WaveBonusPaid, WaveHealthRestored));
        WaveEnded?.Invoke(currentWave.wave);
        intermissionLeft = GameConfigInfo.IntermissionSeconds(config);
        SetPhase(GamePhase.Shop);
        IntermissionChanged?.Invoke(intermissionLeft);
    }

    private void SetPhase(GamePhase _next)
    {
        phase = _next;
        ObserveLogging(() => telemetry.SetPhase(LogPhase(_next)));
        if (_next != GamePhase.Wave)
        {
            ClearAmmoDrops();
        }
        if (_next != GamePhase.Wave && weapon != null)
        {
            weapon.CancelReload();
        }
        PhaseChanged?.Invoke(phase);
    }

    private void OnEnemyKilled(Enemy _enemy)
    {
        SpawnAmmoDrop(_enemy);
        wallet.Add(_enemy.Reward);
        killsThisWave++;
        totalKills++;
        ObserveLogging(() => telemetry.EnemyKilled());
        KillsChanged?.Invoke(killsThisWave);
    }

    private void SpawnAmmoDrop(Enemy _enemy)
    {
        if (_enemy == null || !_enemy.IsDead || !IsWaveActive || playerHealth == null || !playerHealth.IsAlive || ammoPickupPrefab == null)
        {
            return;
        }
        var table = TableManager.Instance.Ammo;
        var entry = AmmoInfo.Find(table, _enemy.DropAmmoId);
        int amount = AmmoInfo.DropAmount(entry, table.pickup);
        if (amount <= 0 || !AmmoInfo.ShouldDrop(table.pickup, UnityEngine.Random.value))
        {
            return;
        }
        AmmoPickup drop = Instantiate(ammoPickupPrefab, _enemy.transform.position, Quaternion.identity, transform);
        drop.Initialize(this, entry, amount);
        ammoDrops.Add(drop);
    }

    public int TryCollectAmmo(AmmoPickup _pickup)
    {
        if (_pickup == null || !_pickup.isActiveAndEnabled || _pickup.IsClaimed || !ammoDrops.Contains(_pickup) ||
            !IsWaveActive || playerHealth == null || !playerHealth.IsAlive || ammo == null ||
            !ammo.Contains(_pickup.AmmoId) || ammo.GetReserve(_pickup.AmmoId) >= ammo.GetMaximum(_pickup.AmmoId))
        {
            return 0;
        }
        var settings = TableManager.Instance.Ammo.pickup;
        if (settings == null || float.IsNaN(settings.pickupDistance) || float.IsInfinity(settings.pickupDistance) || settings.pickupDistance <= 0f ||
            Vector3.Distance(_pickup.transform.position, playerHealth.transform.position) > settings.pickupDistance)
        {
            return 0;
        }
        if (!EnemySight.ClearSegment(_pickup.CollectionPoint, EnemySight.TargetCenter(playerHealth), playerHealth, _pickup.transform, 0f) ||
            !_pickup.TryClaim())
        {
            return 0;
        }
        int added = ammo.Add(_pickup.AmmoId, _pickup.Amount);
        if (added <= 0)
        {
            _pickup.ReleaseClaim();
            return 0;
        }
        ammoDrops.Remove(_pickup);
        _pickup.Remove();
        return added;
    }

    private void ClearAmmoDrops()
    {
        foreach (var drop in ammoDrops)
        {
            if (drop != null)
            {
                drop.Remove();
            }
        }
        ammoDrops.Clear();
    }

    private void OnEnemiesCleared()
    {
        if (phase != GamePhase.Wave)
        {
            return;
        }
        ObserveLogging(() => telemetry.EnemiesCleared());
        EnemiesCleared?.Invoke(waveTimeLeft);
    }

    private void OnPlayerDied()
    {
        if (phase == GamePhase.GameOver)
        {
            return;
        }
        EndLogging("death", true);
        waves.Stop();
        ClearGroundWeapons();
        SetPhase(GamePhase.GameOver);
        GameOver?.Invoke(currentWave.wave, totalKills);
    }

    // ---------- 휴대 슬롯 ----------

    // 1·2번 키 전환. 빈 슬롯이나 사용 중 슬롯은 무시하며, 진행 중인 재장전은 취소한다(장전탄은 각 무기에 남는다).
    // 아킴보 중에는 두 무기를 이미 양손에 들고 있으므로 그 슬롯 키는 무시한다. 아킴보 전환은 정비 시간 토글로만 한다.
    public bool SelectSlot(int _index)
    {
        if (phase == GamePhase.GameOver || loadout.Akimbo || loadout.Get(_index) == null || _index == loadout.ActiveIndex)
        {
            return false;
        }
        weapon.CancelReload();
        loadout.SetActive(_index);
        ApplyActiveWeapon();
        return true;
    }

    private void OnSlotSelected(int _index)
    {
        SelectSlot(_index);
    }

    // ---------- 아킴보 ----------

    public static bool IsAkimboWeapon(string _weaponId)
    {
        return WeaponInfo.CanAkimbo(WeaponInfo.Find(TableManager.Instance.Weapon, _weaponId));
    }

    // 아킴보를 켤 수 없는 사유. 빈 문자열이면 켤 수 있다(이미 켜져 있어도 빈 문자열).
    public string AkimboUnavailableReason
    {
        get
        {
            string closed = ShopClosedReason();
            if (closed != "")
            {
                return closed;
            }
            var active = loadout.Active;
            if (active == null || !IsAkimboWeapon(active.WeaponId))
            {
                return "권총류만 아킴보로 들 수 있습니다.";
            }
            if (!loadout.CanPairAkimbo)
            {
                return "같은 권총 두 자루가 필요합니다.";
            }
            return "";
        }
    }

    // 아킴보 전환(무료, 정비 시간에만). 빈 문자열이면 성공이며, 이미 그 상태여도 성공이다.
    public string TrySetAkimbo(bool _on)
    {
        string reason = _on ? AkimboUnavailableReason : ShopClosedReason();
        if (reason != "")
        {
            return reason;
        }
        if (_on == loadout.Akimbo)
        {
            return "";
        }
        weapon.CancelReload();
        loadout.SetAkimbo(_on);
        ApplyActiveWeapon();
        return "";
    }

    public string TryToggleAkimbo()
    {
        return TrySetAkimbo(!loadout.Akimbo);
    }

    // ---------- 상점 ----------

    private string ShopClosedReason()
    {
        return phase != GamePhase.Shop ? "정비 시간에만 구매할 수 있습니다." : "";
    }

    // 무기 구매. 빈 슬롯이 있으면 그 슬롯에 넣고 현재 무기를 유지한다. 슬롯이 가득 차면 _slot의 무기를 바닥에 놓고 교체하며,
    // 사용 중 슬롯을 교체하면 새 무기를 즉시 든다. 새 무기는 탄창이 가득 찬 채 지급되며 공유 예비탄에서 빼지 않는다.
    // 돈을 쓰기 전에 자리·프리팹을 모두 확인하므로, 실패하면 소지금과 슬롯이 그대로 남는다.
    public string TryBuyWeapon(string _weaponId, int _slot)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        var table = TableManager.Instance;
        var entry = WeaponInfo.Find(table.Weapon, _weaponId);
        var offer = ShopInfo.FindWeapon(table.Shop, _weaponId);
        if (entry == null || offer == null)
        {
            return "무기 데이터를 찾지 못했습니다.";
        }
        if (!ShopInfo.IsWeaponUnlocked(table.Shop, entry, ShopWave))
        {
            return "아직 판매하지 않는 시대의 무기입니다.";
        }
        if (!wallet.CanAfford(offer.price))
        {
            return "소지금이 부족합니다.";
        }

        int slot = loadout.FirstEmptySlot();
        WeaponInstance outgoing = null;
        Vector3 spot = Vector3.zero;
        if (slot < 0)
        {
            if (_slot < 0 || _slot >= loadout.SlotCount)
            {
                return "교체할 슬롯을 선택하세요.";
            }
            slot = _slot;
            outgoing = loadout.Get(slot);
            var outgoingPrefab = PickupPrefabFor(outgoing.WeaponId);
            if (outgoingPrefab == null)
            {
                return "무기 픽업 프리팹을 찾지 못했습니다.";
            }
            if (!TryFindDropSpot(out spot, null, outgoingPrefab))
            {
                return "교체한 무기를 놓을 빈 바닥이 없습니다.";
            }
        }

        if (!wallet.TrySpend(offer.price))
        {
            return "소지금이 부족합니다.";
        }
        // 아킴보 중이면 슬롯 구성이 바뀌어(빈 슬롯 채우기 포함) 아킴보가 풀리므로, 어느 슬롯을 바꿔도 손에 든 무기를 다시 맞춘다.
        bool replacingHeld = loadout.Akimbo || (outgoing != null && slot == loadout.ActiveIndex);
        if (replacingHeld)
        {
            weapon.CancelReload();
        }
        if (outgoing != null)
        {
            SpawnGroundWeapon(outgoing, spot);
        }
        loadout.Put(slot, NewWeapon(_weaponId));
        if (replacingHeld)
        {
            ApplyActiveWeapon();
        }
        return "";
    }

    // 파츠 구매. 같은 파츠를 여러 개 살 수 있으며 살 때마다 새 개체가 생긴다.
    // 같은 종류 휴대 무기 중 그 칸이 빈 무기(사용 중 → 아킴보 왼손 → 나머지 순)에 바로 붙이고,
    // 빈 칸이 없으면 이 파츠가 없는 무기의 칸을 바꿔 끼운다(원래 파츠는 보관). 붙일 곳이 없거나 장전탄이 넘치면 보관한다.
    public string TryBuyPart(string _partId)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        var table = TableManager.Instance;
        var part = PartInfo.Find(table.Part, _partId);
        if (part == null)
        {
            return "파츠 데이터를 찾지 못했습니다.";
        }
        if (!ShopInfo.IsWeaponUnlocked(table.Shop, WeaponInfo.Find(table.Weapon, part.weaponId), ShopWave))
        {
            return "아직 판매하지 않는 파츠입니다.";
        }
        if (!wallet.TrySpend(ShopInfo.PartPrice(table.Shop, part)))
        {
            return "소지금이 부족합니다.";
        }
        var bought = parts.Add(_partId);
        var target = AutoAttachTarget(part);
        if (bought != null && target != null)
        {
            AttachAndRefresh(bought, target);
        }
        return "";
    }

    // 산 파츠를 바로 붙일 휴대 무기. 없으면 null(보관).
    private WeaponInstance AutoAttachTarget(PartData.Entry _part)
    {
        var order = new List<WeaponInstance> { loadout.Active, loadout.OffHand };
        for (int i = 0; i < loadout.SlotCount; i++)
        {
            order.Add(loadout.Get(i));
        }
        order.RemoveAll(_w => _w == null || _w.WeaponId != _part.weaponId);
        foreach (var held in order)
        {
            if (held.PartIn(_part.slot) == null && OverflowFits(new[] { (held, (string)null, _part.id) }))
            {
                return held;
            }
        }
        foreach (var held in order)
        {
            if (!held.HasPart(_part.id) && OverflowFits(new[] { (held, held.PartIn(_part.slot), _part.id) }))
            {
                return held;
            }
        }
        return null;
    }

    // 보관 중이거나 휴대 무기에 붙은 파츠 개체를 휴대 슬롯 무기에 무료로 붙인다. 바닥 무기에 붙은 파츠는 그 무기를 주워야 옮길 수 있다.
    // 대상 칸의 원래 파츠는 보관함으로 간다. 두 무기에서 넘치는 장전탄이 예비탄에 다 들어가지 않으면 아무것도 바꾸지 않는다.
    public string TryAttachPart(PartInstance _part, int _slot)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        var entry = _part != null ? PartInfo.Find(TableManager.Instance.Part, _part.PartId) : null;
        var target = loadout.Get(_slot);
        if (entry == null || !parts.Owns(_part))
        {
            return "보유하지 않은 파츠입니다.";
        }
        if (target == null || target.WeaponId != entry.weaponId)
        {
            return "이 파츠를 붙일 수 없는 무기입니다.";
        }
        var current = _part.AttachedTo;
        if (current != null && !loadout.Contains(current))
        {
            return "바닥에 놓인 무기에 붙어 있습니다. 그 무기를 주워야 옮길 수 있습니다.";
        }
        // 대상에 같은 파츠가 이미 붙어 있으면 옮겨도 원래 무기만 파츠를 잃으므로 막는다.
        if (current == target || target.HasPart(_part.PartId))
        {
            return "이미 장착되어 있습니다.";
        }
        if (!OverflowFits(new[] { (current, _part.PartId, (string)null), (target, target.PartIn(entry.slot), _part.PartId) }))
        {
            return AMMO_OVERFLOW_MESSAGE;
        }
        AttachAndRefresh(_part, target);
        return "";
    }

    // 파츠 id로 장착. 대상 무기에 이미 같은 파츠가 있으면 실패하고, 보관함 개체 → 다른 휴대 무기에 붙은 개체 순으로 고른다.
    public string TryAttachPart(string _partId, int _slot)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        var target = loadout.Get(_slot);
        if (target != null && target.HasPart(_partId))
        {
            return "이미 장착되어 있습니다.";
        }
        var candidate = parts.FirstStored(_partId) ??
            parts.InstancesOf(_partId).Find(_p => _p.AttachedTo != null && loadout.Contains(_p.AttachedTo));
        if (candidate == null && parts.IsOwned(_partId))
        {
            return "바닥에 놓인 무기에 붙어 있습니다. 그 무기를 주워야 옮길 수 있습니다.";
        }
        return TryAttachPart(candidate, _slot);
    }

    private const string AMMO_OVERFLOW_MESSAGE = "탄창이 줄어 남는 장전탄을 돌려받을 예비탄 칸이 없습니다.";

    // 파츠 변경 묶음(무기, 빼는 파츠 id, 넣는 파츠 id)을 한꺼번에 적용했을 때 줄어든 탄창에서 넘치는 장전탄이
    // 탄종별 공유 예비탄에 다 들어가는지. 장전탄을 버리지 않기 위한 확인이며, 묶음 전체가 들어가야 true다.
    private bool OverflowFits(IEnumerable<(WeaponInstance weapon, string removed, string added)> _changes)
    {
        var table = TableManager.Instance;
        var excessByAmmo = new Dictionary<string, int>();
        foreach (var change in _changes)
        {
            if (change.weapon == null)
            {
                continue;
            }
            var entry = WeaponInfo.Find(table.Weapon, change.weapon.WeaponId);
            if (entry == null)
            {
                continue;
            }
            var list = parts.PartsOf(change.weapon);
            list.RemoveAll(_p => _p.id == change.removed);
            var added = PartInfo.Find(table.Part, change.added);
            if (added != null)
            {
                list.Add(added);
            }
            int excess = change.weapon.Magazine - WeaponInfo.Resolve(entry, list, stats.ReloadMultiplier).magazineSize;
            if (excess > 0)
            {
                excessByAmmo.TryGetValue(entry.ammoId, out int sum);
                excessByAmmo[entry.ammoId] = sum + excess;
            }
        }
        foreach (var pair in excessByAmmo)
        {
            if (ammo.GetMaximum(pair.Key) - ammo.GetReserve(pair.Key) < pair.Value)
            {
                return false;
            }
        }
        return true;
    }

    // 휴대 무기에 붙은 파츠 개체를 보관함으로 돌린다(무료).
    public string TryDetachPart(PartInstance _part)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        var current = _part != null && parts.Owns(_part) ? _part.AttachedTo : null;
        if (current == null || !loadout.Contains(current))
        {
            return "휴대 무기에 장착된 파츠가 아닙니다.";
        }
        if (!OverflowFits(new[] { (current, _part.PartId, (string)null) }))
        {
            return AMMO_OVERFLOW_MESSAGE;
        }
        parts.Detach(_part);
        RefreshWeaponStats(current);
        return "";
    }

    // 휴대 무기에 붙은 개수와 바닥 무기에 붙은 개수. 보관 개수는 Parts.StoredCount다.
    public int HeldPartCount(string _partId)
    {
        int count = 0;
        for (int i = 0; i < loadout.SlotCount; i++)
        {
            var held = loadout.Get(i);
            if (held != null && held.HasPart(_partId))
            {
                count++;
            }
        }
        return count;
    }

    public int GroundPartCount(string _partId)
    {
        return parts.AttachedCount(_partId) - HeldPartCount(_partId);
    }

    // _slot 휴대 무기에 붙은 _partId 개체를 분리한다.
    public string TryDetachPart(string _partId, int _slot)
    {
        var held = loadout.Get(_slot);
        foreach (var part in held != null ? held.Parts : Array.Empty<PartInstance>())
        {
            if (part.PartId == _partId)
            {
                return TryDetachPart(part);
            }
        }
        string closed = ShopClosedReason();
        return closed != "" ? closed : "그 무기에 장착된 파츠가 아닙니다.";
    }

    // 파츠 id로 분리. 사용 중 무기 → 아킴보 왼손 → 나머지 휴대 무기 순으로 붙은 개체를 고른다.
    public string TryDetachPart(string _partId)
    {
        var order = new List<WeaponInstance> { loadout.Active, loadout.OffHand };
        for (int i = 0; i < loadout.SlotCount; i++)
        {
            order.Add(loadout.Get(i));
        }
        foreach (var held in order)
        {
            if (held == null || !held.HasPart(_partId))
            {
                continue;
            }
            foreach (var part in held.Parts)
            {
                if (part.PartId == _partId)
                {
                    return TryDetachPart(part);
                }
            }
        }
        string closed = ShopClosedReason();
        return closed != "" ? closed : "휴대 무기에 장착된 파츠가 아닙니다.";
    }

    public string TryBuyStat(string _statId)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        var shop = TableManager.Instance.Shop;
        var stat = ShopInfo.FindStat(shop, _statId);
        if (stat == null)
        {
            return "스탯 데이터를 찾지 못했습니다.";
        }
        if (!wallet.TrySpend(ShopInfo.StatPrice(shop, stat, stats.CountOf(_statId))))
        {
            return "소지금이 부족합니다.";
        }
        stats.Buy(_statId);
        ApplyStats(_statId);
        return "";
    }

    public string TryBuyAmmo(string _ammoId)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        if (!ShopInfo.IsAmmoUnlocked(TableManager.Instance.Shop, TableManager.Instance.Weapon, _ammoId, ShopWave))
        {
            return "아직 판매하지 않는 탄약입니다.";
        }
        return ammoShop.TryBuy(_ammoId);
    }

    public string TryBuyHeal()
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        if (!CanHeal)
        {
            return "체력이 가득 차 있습니다.";
        }
        if (!wallet.TrySpend(ShopInfo.HealPrice(TableManager.Instance.Shop)))
        {
            return "소지금이 부족합니다.";
        }
        playerHealth.Heal(GameConfigInfo.HealAmount(TableManager.Instance.GameConfig));
        return "";
    }

    // 슬롯 업그레이드 진열 칸 구매. 산 칸은 다음 갱신까지 빈 칸으로 남는다.
    public string TryBuySlotUpgrade(int _offerIndex)
    {
        string closed = ShopClosedReason();
        if (closed != "")
        {
            return closed;
        }
        if (!slotOffers.CanBuy(_offerIndex))
        {
            return slotOffers.IsSoldOut(_offerIndex) ? "품절입니다." : "이미 구매했습니다.";
        }
        string perkId = slotOffers.Get(_offerIndex);
        var upgrade = ShopInfo.FindSlotUpgrade(TableManager.Instance.Shop, perkId);
        if (upgrade == null)
        {
            return "업그레이드 데이터를 찾지 못했습니다.";
        }
        if (!wallet.TrySpend(upgrade.price))
        {
            return "소지금이 부족합니다.";
        }
        perks.Buy(perkId);
        slotOffers.MarkBought(_offerIndex);
        return "";
    }

    // ---------- 바닥 무기 ----------

    // 바닥 픽업 줍기. 빈 슬롯이 있으면 그 슬롯에 넣고 현재 무기를 유지한다. 가득 찼으면 사용 중 무기와 교환해 바닥에 놓는다.
    // 주운 무기는 놓였을 때의 장전탄·파츠를 그대로 이어받는다(재장전 이득 없음). 조준 대상 픽업은 줍는 순간 한 번만 소비된다.
    // 직접 호출이나 지난 프레임 조준으로 들어와도, 지금 카메라 시선·거리·생존·단계·소비 여부를 다시 확인한 뒤에만 바꾼다.
    public string TryPickup(WeaponPickup _pickup)
    {
        if (!CanPickupTarget(_pickup) || interactor == null || !interactor.IsPickupInView(_pickup))
        {
            return "주울 수 없는 무기입니다.";
        }
        int empty = loadout.FirstEmptySlot();
        WeaponInstance outgoing = empty < 0 ? loadout.Active : null;
        Vector3 spot = Vector3.zero;
        if (outgoing != null && !TryFindDropSpot(out spot, _pickup, PickupPrefabFor(outgoing.WeaponId)))
        {
            return "들고 있는 무기를 놓을 빈 바닥이 없습니다.";
        }

        WeaponInstance picked = _pickup.Weapon ?? NewWeapon(_pickup.WeaponId);
        // 소비를 먼저 확정한다. 같은 프레임에 다시 들어온 요청은 여기서 막혀 무기가 복제되지 않는다.
        if (!_pickup.Consume())
        {
            return "주울 수 없는 무기입니다.";
        }
        if (outgoing != null)
        {
            weapon.CancelReload();
            SpawnGroundWeapon(outgoing, spot);
            loadout.Put(loadout.ActiveIndex, picked);
            ApplyActiveWeapon();
        }
        else
        {
            // 빈 슬롯을 채워도 슬롯 구성이 바뀌어 아킴보가 풀린다. 그때는 손에 든 무기를 다시 맞춘다.
            bool wasAkimbo = loadout.Akimbo;
            if (wasAkimbo)
            {
                weapon.CancelReload();
            }
            loadout.Put(empty, picked);
            if (wasAkimbo)
            {
                ApplyActiveWeapon();
            }
        }
        return "";
    }

    // 픽업 프롬프트와 입력이 같이 쓰는 판정. 웨이브 중이 아니거나, 놓을 자리가 필요한데 없으면 false다.
    private bool CanPickupTarget(WeaponPickup _pickup)
    {
        if (_pickup == null || !_pickup.isActiveAndEnabled || phase != GamePhase.Wave || _pickup.IsConsumed)
        {
            return false;
        }
        if (WeaponInfo.Find(TableManager.Instance.Weapon, _pickup.WeaponId) == null)
        {
            return false;
        }
        float reachLimit = GameConfigInfo.InteractDistance(TableManager.Instance.GameConfig);
        bool inRange = playerHealth != null && playerHealth.IsAlive &&
            Vector3.Distance(_pickup.transform.position, playerHealth.transform.position) <= reachLimit + 0.5f;
        if (!inRange)
        {
            return false;
        }
        if (loadout.FirstEmptySlot() >= 0)
        {
            return true;
        }
        var outgoingPrefab = loadout.Active != null ? PickupPrefabFor(loadout.Active.WeaponId) : null;
        return outgoingPrefab != null && TryFindDropSpot(out _, _pickup, outgoingPrefab);
    }

    private string PickupPrompt(WeaponPickup _pickup)
    {
        string label = string.IsNullOrEmpty(_pickup.DisplayName) ? _pickup.WeaponId : _pickup.DisplayName;
        int empty = loadout.FirstEmptySlot();
        return empty >= 0
            ? $"[F / X] {label} 줍기 · {empty + 1}번 슬롯에 넣음"
            : $"[F / X] {label} 줍기 · 현재 무기는 바닥에 놓임";
    }

    private void OnPickupPressed(WeaponPickup _pickup)
    {
        string failure = TryPickup(_pickup);
        if (failure != "")
        {
            Debug.LogWarning(failure);
        }
    }

    // ---------- 무기·스탯 적용 ----------

    private WeaponInstance NewWeapon(string _weaponId)
    {
        var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weaponId);
        return new WeaponInstance(_weaponId, entry != null ? entry.magazineSize : 0);
    }

    // 개별 무기의 최종 스탯(무기 × 파츠 × 장전 스탯).
    public PlayerWeapon.WeaponSettings ResolveSettings(WeaponInstance _weapon)
    {
        var entry = _weapon != null ? WeaponInfo.Find(TableManager.Instance.Weapon, _weapon.WeaponId) : null;
        return WeaponInfo.Resolve(entry, parts.PartsOf(_weapon), stats.ReloadMultiplier);
    }

    private void AttachAndRefresh(PartInstance _part, WeaponInstance _target)
    {
        var previous = _part.AttachedTo;
        parts.Attach(_part, _target);
        if (previous != null && previous != _target)
        {
            RefreshWeaponStats(previous);
        }
        RefreshWeaponStats(_target);
    }

    // 파츠 변화 후 장전탄을 새 탄창 크기에 맞춘다. 넘치는 장전탄은 공유 예비탄으로 돌려준다.
    private void RefreshWeaponStats(WeaponInstance _weapon)
    {
        if (_weapon == null)
        {
            return;
        }
        var settings = ResolveSettings(_weapon);
        if (_weapon.Magazine > settings.magazineSize)
        {
            var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weapon.WeaponId);
            if (entry != null)
            {
                ammo.Add(entry.ammoId, _weapon.Magazine - settings.magazineSize);
            }
            _weapon.Magazine = settings.magazineSize;
        }
        if (weapon != null)
        {
            weapon.UpdateSettings(_weapon, settings);
        }
        loadout.NotifyChanged();
        if (heldModels != null) heldModels.ApplyParts(loadout.Active, loadout.OffHand);
    }

    // 사용 중인 무기를 손에 든다. 아킴보면 보조 슬롯 무기를 왼손에 함께 든다. 외형·이동속도도 함께 맞춘다.
    private void ApplyActiveWeapon()
    {
        var table = TableManager.Instance.Weapon;
        var active = loadout.Active;
        var entry = active != null ? WeaponInfo.Find(table, active.WeaponId) : null;
        if (entry == null || weapon == null)
        {
            Debug.LogError($"무기 {active?.WeaponId}를 적용하지 못했습니다.");
            return;
        }
        string fallbackModel = WeaponInfo.Drop(table)?.fallbackModelId;
        var offHand = loadout.OffHand;
        weapon.SetHeldModels(heldModels);
        if (heldModels != null) heldModels.ApplyParts(active, offHand);
        if (offHand != null && WeaponInfo.CanAkimbo(entry))
        {
            weapon.ConfigureAkimbo(active, ResolveSettings(active), offHand, ResolveSettings(offHand), ammo, entry.ammoId, WeaponInfo.AkimboReloadMultiplier(table));
            if (heldModels != null)
            {
                heldModels.ShowAkimbo(entry.modelId, fallbackModel);
            }
        }
        else
        {
            // 아킴보로 들 수 없는 무기면 기록도 풀어 손에 든 상태와 맞춘다.
            if (loadout.Akimbo)
            {
                loadout.SetAkimbo(false);
            }
            weapon.Configure(active, ResolveSettings(active), ammo, entry.ammoId);
            if (heldModels != null)
            {
                heldModels.Show(entry.modelId, fallbackModel);
            }
        }
        ApplyMoveSpeed();
        ObserveLogging(SynchronizeCombatEquipment);
    }

    private void ApplyMoveSpeed()
    {
        if (movement == null)
        {
            return;
        }
        var active = loadout.Active;
        var entry = active != null ? WeaponInfo.Find(TableManager.Instance.Weapon, active.WeaponId) : null;
        movement.SpeedMultiplier = stats.MoveSpeedMultiplier * WeaponInfo.MoveSpeedMultiplier(entry);
    }

    private void ApplyStats(string _statId)
    {
        switch (_statId)
        {
            case PlayerStats.MAX_HEALTH:
                if (playerHealth != null)
                {
                    playerHealth.SetMaxHealth(GameConfigInfo.MaxHealth(TableManager.Instance.GameConfig) + stats.MaxHealthBonus);
                }
                break;
            case PlayerStats.ARMOR:
                if (playerHealth != null)
                {
                    playerHealth.SetDamageTakenMultiplier(stats.DamageTakenMultiplier);
                }
                break;
            case PlayerStats.MOVE_SPEED:
                ApplyMoveSpeed();
                break;
            case PlayerStats.RELOAD_SPEED:
                for (int i = 0; i < loadout.SlotCount; i++)
                {
                    RefreshWeaponStats(loadout.Get(i));
                }
                break;
            case PlayerStats.AMMO_CAPACITY:
                var table = TableManager.Instance.Ammo;
                foreach (var entry in table.ammoTypes)
                {
                    if (entry != null)
                    {
                        ammo.SetMaximum(entry.id, entry.maxReserve + stats.AmmoCapacityBonus, false);
                    }
                }
                break;
        }
    }

    private WeaponPickup PickupPrefabFor(string _weaponId)
    {
        var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weaponId);
        if (entry == null || pickups == null)
        {
            return null;
        }
        return pickups.Find(entry.modelId, WeaponInfo.Drop(TableManager.Instance.Weapon)?.fallbackModelId);
    }

    // 바닥 무기는 픽업 프리팹을 복제해 둔다. 게임플레이 스폰(드롭)이라 런타임 생성을 허용한다.
    // 무기 객체를 그대로 넘기므로 장전탄과 파츠가 바닥에서도 유지된다.
    private void SpawnGroundWeapon(WeaponInstance _weapon, Vector3 _position)
    {
        WeaponPickup drop = Instantiate(PickupPrefabFor(_weapon.WeaponId), _position, Quaternion.identity, GroundRoot());
        drop.Initialize(_weapon, WeaponInfo.DisplayName(TableManager.Instance.Weapon, _weapon.WeaponId));
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
