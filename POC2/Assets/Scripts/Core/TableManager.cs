using UnityEngine;

// 실행 초기에 한 번 읽어 유지하는 참조 테이블 보관소.
// 씬·프리팹 연결이나 스크립터블오브젝트 없이 인스턴스가 테이블을 들고 있고, 게임 코드는 조회 로직 클래스(XInfo)로 읽는다.
//
// 규칙: 파일(Resources/X.json) → 순수 직렬화 자료(XData) → TableManager가 1회 로드·보유.
//       테이블을 읽는 로직은 XInfo 정적 클래스에 두고, 데이터 클래스에는 필드만 남긴다.
//       프로퍼티명은 클래스명에서 Data를 뺀 이름(SlotMachineData → SlotMachine)을 쓴다.
//       신규 테이블 등록: Resources/X.json 작성 → XData·XInfo 작성 → 여기 프로퍼티·Load·Validate 1줄씩 추가.
public sealed class TableManager
{
    private static TableManager instance;

    /// <summary>초기화된 보관소. 초기화 전에 읽어도 한 번 채우고 되돌린다.</summary>
    public static TableManager Instance => instance ?? Initialize();

    /// <summary>슬롯머신 당첨·가격 테이블. 로드 실패 시 빈 표이며 원인은 이미 로그로 남았다.</summary>
    public SlotMachineData SlotMachine { get; private set; }
    public WeaponData Weapon { get; private set; }
    public PartData Part { get; private set; }
    public AmmoData Ammo { get; private set; }
    public EnemyData Enemy { get; private set; }
    public EnemyWeaponData EnemyWeapon { get; private set; }
    public EnemyAiData EnemyAi { get; private set; }
    public WaveData Wave { get; private set; }
    public ShopData Shop { get; private set; }
    public PerkData Perk { get; private set; }
    public GameConfigData GameConfig { get; private set; }

    private TableManager()
    {
        SlotMachine = Load<SlotMachineData>(nameof(SlotMachineData));
        SlotMachineInfo.Validate(SlotMachine);

        Ammo = Load<AmmoData>(nameof(AmmoData));
        AmmoInfo.Validate(Ammo);

        Weapon = Load<WeaponData>(nameof(WeaponData));
        WeaponInfo.Validate(Weapon);
        WeaponInfo.ValidateAmmo(Weapon, Ammo);

        Part = Load<PartData>(nameof(PartData));
        PartInfo.Validate(Part);
        PartInfo.ValidateWeapons(Part, Weapon);

        Enemy = Load<EnemyData>(nameof(EnemyData));
        EnemyInfo.Validate(Enemy);

        EnemyWeapon = Load<EnemyWeaponData>(nameof(EnemyWeaponData));
        EnemyWeaponInfo.Validate(EnemyWeapon);
        EnemyWeaponInfo.ValidateAmmo(EnemyWeapon, Ammo);
        EnemyWeaponInfo.ValidatePlayerWeapons(EnemyWeapon, Weapon);

        EnemyAi = Load<EnemyAiData>(nameof(EnemyAiData));
        EnemyAiInfo.Validate(EnemyAi);

        Wave = Load<WaveData>(nameof(WaveData));
        WaveInfo.Validate(Wave);
        WaveInfo.ValidateEnemyWeapons(Wave, EnemyWeapon);

        Perk = Load<PerkData>(nameof(PerkData));
        PerkInfo.Validate(Perk);

        Shop = Load<ShopData>(nameof(ShopData));
        ShopInfo.Validate(Shop);
        ShopInfo.ValidateReferences(Shop, Weapon, Ammo, Perk);

        GameConfig = Load<GameConfigData>(nameof(GameConfigData));
        GameConfigInfo.Validate(GameConfig);
    }

    /// <summary>반복 호출해도 테이블을 다시 읽지 않는다.</summary>
    public static TableManager Initialize()
    {
        if (instance == null)
        {
            instance = new TableManager();
        }
        return instance;
    }

#if UNITY_EDITOR
    public static void ReloadAfterTableEdit()
    {
        instance = null;
        Initialize();
    }
#endif

    // 테이블 파일을 한 번 읽는다. 실패하면 빈 객체를 주고 이유를 한 번만 남긴다.
    private static T Load<T>(string _resourcePath) where T : class, new()
    {
        TextAsset asset = Resources.Load<TextAsset>(_resourcePath);
        if (asset == null)
        {
            Debug.LogError($"테이블 {_resourcePath}를 Resources에서 찾지 못했습니다. 빈 테이블로 동작합니다.");
            return new T();
        }

        T table;
        try
        {
            table = JsonUtility.FromJson<T>(asset.text);
        }
        catch (System.ArgumentException exception)
        {
            Debug.LogError($"테이블 {_resourcePath}의 JSON을 읽지 못했습니다: {exception.Message}");
            return new T();
        }

        if (table == null)
        {
            Debug.LogError($"테이블 {_resourcePath}의 형식을 읽지 못했습니다. 빈 테이블로 동작합니다.");
            return new T();
        }
        return table;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }
}
