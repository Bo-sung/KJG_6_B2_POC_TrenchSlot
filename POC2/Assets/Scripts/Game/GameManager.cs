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
        }
        ApplyWeapon(loadout.EquippedId);
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

    private string BuyWeapon(ShopData.Item _item)
    {
        if (loadout.IsOwned(_item.target))
        {
            loadout.Equip(_item.target);
            ApplyWeapon(_item.target);
            return "";
        }
        if (!wallet.TrySpend(_item.price))
        {
            return "소지금이 부족합니다.";
        }
        loadout.Own(_item.target);
        loadout.Equip(_item.target);
        ApplyWeapon(_item.target);
        return "";
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

    private void ApplyWeapon(string _weaponId)
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
        });
    }
}
