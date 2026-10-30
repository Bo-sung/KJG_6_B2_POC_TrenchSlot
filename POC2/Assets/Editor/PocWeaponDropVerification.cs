using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

// CLI(격리 사본에서만 실행):
//   편집 모드 단위 검사: -batchmode -nographics -projectPath <copy> -executeMethod PocWeaponDropVerification.RunUnitChecks
//   플레이 모드 검사:   -batchmode -nographics -projectPath <copy> -executeMethod PocWeaponDropVerification.RunPlayChecks  (no -quit)
// 구매→낙하→주움 사이클, 실패 구매, 중복 없음, 탄약 보존, 재장전 취소, 픽업 게이트, 사망 정리를 실제 PlayerLoop에서 확인한다.
[InitializeOnLoad]
public static class PocWeaponDropVerification
{
    private const string RUNNING = "PocWeaponDropVerification.running";
    private const string TAG = "[PocWeaponDropVerification]";
    private const int PICKUP_LAYER = 9;
    private const float WAIT_LIMIT_FRAMES = 600;

    private static int passes;
    private static int failures;
    private static bool playStarted;

    static PocWeaponDropVerification()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private static void Check(bool _ok, string _label)
    {
        if (_ok)
        {
            passes++;
            Debug.Log($"{TAG} PASS {_label}");
        }
        else
        {
            failures++;
            Debug.LogError($"{TAG} FAIL {_label}");
        }
    }

    private static int Finish()
    {
        Debug.Log($"{TAG} 결과 pass={passes} fail={failures}");
        return failures == 0 ? 0 : 1;
    }

    // 편집 모드: 획득 의미 체계, 입력 바인딩, 프리팹 구성, 탄약 오버로드.
    public static void RunUnitChecks()
    {
        var table = JsonUtility.FromJson<WeaponData>(Resources.Load<TextAsset>("WeaponData").text);
        Check(table.drop != null && table.drop.minDistance > 0f && table.drop.maxDistance >= table.drop.minDistance, "WeaponData.drop 자리 규칙이 로드된다");
        var weaponLog = 0;
        Application.LogCallback capture = (_message, _stack, _type) =>
        {
            if (_type == LogType.Error && _message.Contains("drop"))
            {
                weaponLog++;
            }
        };
        Application.logMessageReceived += capture;
        WeaponInfo.Validate(table);
        Application.logMessageReceived -= capture;
        Check(weaponLog == 0, "WeaponInfo.Validate가 drop 규칙을 오류 없이 통과한다");

        var loadout = new WeaponLoadout(2);
        var first = new WeaponInstance("m1873", 6);
        var second = new WeaponInstance("m1873", 3);
        Check(loadout.Put(0, first) == null && loadout.Active == first, "시작 무기는 1번 슬롯 사용 중이다");
        Check(!loadout.SetActive(1), "빈 슬롯으로는 전환할 수 없다");
        Check(loadout.Put(1, second) == null && loadout.FirstEmptySlot() < 0, "같은 종류 무기를 두 자루 가질 수 있다");
        Check(loadout.Put(1, first) == null && loadout.Get(1) == second, "같은 개별 무기를 두 슬롯에 넣지 않는다");
        Check(loadout.SetActive(1) && loadout.Active == second && first.Magazine == 6 && second.Magazine == 3, "전환 후에도 장전탄은 무기마다 따로 남는다");

        var root = new GameObject("WeaponAmmoProbe");
        var weapon = root.AddComponent<PlayerWeapon>();
        var settings = new PlayerWeapon.WeaponSettings { magazineSize = 6, shotsPerSecond = 2.5f, range = 60f, damage = 25f, reloadSeconds = 1.8f, projectileSpeed = 120f, projectileLifetime = 1f, projectileRadius = 0.05f };
        weapon.Configure(new WeaponInstance("m1873", 6), settings, null, "");
        Check(weapon.Ammo == 6, "새 구매 무기는 탄창을 가득 채운다");
        weapon.Configure(new WeaponInstance("m1873", 2), settings, null, "");
        Check(weapon.Ammo == 2, "주운 무기는 저장된 탄약을 그대로 이어받는다");
        weapon.Configure(new WeaponInstance("m1873", 99), settings, null, "");
        Check(weapon.Ammo == 6, "저장 탄약이 탄창을 넘으면 탄창으로 제한한다");
        var ammoTable = JsonUtility.FromJson<AmmoData>(Resources.Load<TextAsset>("AmmoData").text);
        var inventory = new AmmoInventory(ammoTable);
        bool playerEnabled = InputGate.PlayerEnabled;
        InputGate.PlayerEnabled = true;
        try
        {
            int reserve = inventory.GetReserve("colt45");
            weapon.Configure(new WeaponInstance("m1873", 3), settings, inventory, "colt45");
            weapon.StartReload();
            Check(weapon.IsReloading && weapon.ReserveAmmo == reserve, "예비탄이 있으면 소비 없이 재장전을 시작한다");
            weapon.CancelReload();
            Check(!weapon.IsReloading && weapon.Ammo == 3 && weapon.ReserveAmmo == reserve,
                "재장전 취소 후 장전탄과 예비탄은 유지된다");

            weapon.StartReload();
            weapon.GrantSlotAmmo();
            // 탄창이 다 찼으니 남은 재장전으로 조작을 묶어 두지 않는다(예비탄 소비 없음).
            Check(weapon.Ammo == 6 && weapon.ReserveAmmo == reserve && !weapon.IsReloading,
                "슬롯은 탄창만 무료로 채우고, 할 일이 없어진 진행 중 재장전은 예비탄 소비 없이 끝낸다");

            settings.reloadSeconds = 0f;
            weapon.Configure(new WeaponInstance("m1873", 2), settings, inventory, "colt45");
            weapon.StartReload();
            Check(!weapon.IsReloading && weapon.Ammo == 6 && weapon.ReserveAmmo == reserve - 4,
                "재장전 완료는 부족한 장전탄만큼 예비탄을 소비한다");
            inventory.Consume("colt45", int.MaxValue);
            inventory.Add("colt45", 2);
            weapon.Configure(new WeaponInstance("m1873", 1), settings, inventory, "colt45");
            weapon.StartReload();
            Check(weapon.Ammo == 3 && weapon.ReserveAmmo == 0, "부족한 예비탄만큼만 장전한다");
            weapon.StartReload();
            Check(!weapon.IsReloading && weapon.Ammo == 3 && weapon.ReserveAmmo == 0,
                "예비탄이 없으면 재장전과 탄약 생성이 발생하지 않는다");
        }
        finally
        {
            InputGate.PlayerEnabled = playerEnabled;
        }

        // X 컨텍스트 주움 우선순위는 이번 프레임의 픽업 키 입력 + 이번 프레임 조준으로 정한 예약(PickupClaimThisFrame)만 본다.
        // 조준 가능한 유효 픽업이 있어도 이번 프레임에 픽업 키가 눌리지 않았으면 재장전을 막지 않는다(R·대상 없는 X는 재장전).
        // 실제 입력·양쪽 Update 순서 검사는 PocPickupPriorityVerification이 맡는다.
        Check(typeof(PlayerInteractor).GetProperty("PickupClaimThisFrame") != null, "PlayerInteractor가 프레임 단위 줍기 예약을 노출한다");
        PlayerInteractor ctxInteractor = new GameObject("CtxInteractorProbe").AddComponent<PlayerInteractor>();
        WeaponPickup contextPickup = new GameObject("CtxPickupProbe").AddComponent<WeaponPickup>();
        contextPickup.Initialize(new WeaponInstance("win1886", 5), "");
        ctxInteractor.CanPickup = _candidate => _candidate != null && _candidate.WeaponId == "win1886";
        PlayerWeapon ctxProbe = new GameObject("CtxProbeHolder").AddComponent<PlayerWeapon>();
        SetPrivate(ctxProbe, "interactor", ctxInteractor);
        MethodInfo blocked = typeof(PlayerWeapon).GetMethod("ReloadBlockedByPickupInView", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(blocked != null && !(bool)blocked.Invoke(ctxProbe, null) && ctxInteractor.PickupClaimThisFrame == null,
            "픽업 키 입력이 없으면 유효 픽업이 있어도 줍기 예약이 없고 재장전은 막히지 않는다");
        UnityEngine.Object.DestroyImmediate(ctxInteractor.gameObject);
        UnityEngine.Object.DestroyImmediate(contextPickup.gameObject);
        UnityEngine.Object.DestroyImmediate(ctxProbe.gameObject);

        UnityEngine.Object.DestroyImmediate(root);

        CheckBindings();
        CheckPrefabs();

        Debug.Log($"{TAG} 편집 모드 검사 완료");
        PocEditorExit.Exit(Finish());
    }

    private static void CheckBindings()
    {
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        Check(asset != null, "입력 자산을 불러온다");
        if (asset == null)
        {
            return;
        }
        var pickup = asset.FindAction("Player/Pickup");
        Check(pickup != null, "Player/Pickup 액션이 있다");
        if (pickup != null)
        {
            var paths = pickup.bindings.Select(_b => _b.effectivePath).ToList();
            Check(paths.Contains("<Keyboard>/f"), "Pickup 키보드 F 바인딩");
            Check(paths.Contains("<Gamepad>/buttonWest"), "Pickup 게임패드 X(buttonWest) 바인딩");
        }
        AssertBinding(asset, "Player/Interact", "852140f2-7766-474d-8707-702459ba45f3", "<Keyboard>/e", "<Gamepad>/buttonNorth");
        AssertBinding(asset, "Player/Crouch", "27c5f898-bc57-4ee1-8800-db469aca5fe3", "<Keyboard>/c", "<Gamepad>/buttonEast");
        var crouchPaths = asset.FindAction("Player/Crouch").bindings.Select(_b => _b.effectivePath).ToList();
        Check(crouchPaths.Contains("<Keyboard>/leftCtrl"), "Crouch 키보드 LeftCtrl 바인딩 추가(C 유지)");
        AssertBinding(asset, "Player/Reload", "cc78bb08-a901-4163-8d06-029f08a6d27b", "<Keyboard>/r", "<Gamepad>/buttonWest");
    }

    private static void AssertBinding(InputActionAsset _asset, string _path, string _id, string _key, string _pad)
    {
        var action = _asset.FindAction(_path);
        Check(action != null && action.id.ToString() == _id, $"{_path} 기존 ID 유지");
        if (action != null)
        {
            var paths = action.bindings.Select(_b => _b.effectivePath).ToList();
            Check(paths.Contains(_key) && paths.Contains(_pad), $"{_path} 기존 바인딩 유지({_key}, {_pad})");
        }
    }

    private static void CheckPrefabs()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<GameObject>(PocWeaponDropAuthoring.CATALOG_PREFAB_PATH);
        var catalogComponent = catalog != null ? catalog.GetComponent<WeaponPickupCatalog>() : null;
        Check(catalogComponent != null, "카탈로그 프리팹에 WeaponPickupCatalog가 있다");
        foreach (string id in PocWeaponDropAuthoring.MODEL_IDS)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(PocWeaponDropAuthoring.PICKUP_PREFAB_FORMAT, id));
            var pickup = prefab != null ? prefab.GetComponent<WeaponPickup>() : null;
            Check(pickup != null, $"픽업 프리팹 {id}가 있다");
            Check(prefab != null && prefab.layer == PICKUP_LAYER && prefab.GetComponentInChildren<MeshRenderer>() != null, $"픽업 프리팹 {id}는 상호작용 레이어에 보이는 모델을 가진다");
            Check(catalogComponent != null && catalogComponent.Find(id) != null, $"카탈로그가 {id} 픽업을 가진다");
        }
        var held = AssetDatabase.LoadAssetAtPath<GameObject>(PocWeaponDropAuthoring.HELD_PREFAB_PATH);
        var heldComponent = held != null ? held.GetComponent<WeaponHeldModels>() : null;
        bool everyModel = heldComponent != null;
        foreach (string id in PocWeaponDropAuthoring.MODEL_IDS)
        {
            everyModel &= heldComponent != null && heldComponent.Has(id);
        }
        Check(everyModel, "들고 있는 무기 프리팹은 외형마다 모델을 가진다");
        Check(heldComponent != null && heldComponent.HasOffHand("revolver"), "아킴보 권총 외형은 왼손 모델도 가진다");
        Check(heldComponent != null && heldComponent.HasOffHand("colt_saa") && !heldComponent.HasOffHand("colt_1878_shotgun"),
            "M1873은 왼손 모델이 있고 Colt 1878 산탄총은 없다");
        var weapons = TableManager.Instance.Weapon;
        Check(WeaponInfo.Find(weapons, "m1873")?.modelId == "colt_saa" &&
              WeaponInfo.Find(weapons, "colt1878")?.modelId == "colt_1878_shotgun" &&
              WeaponInfo.Find(weapons, "win1886")?.modelId == "winchester_1886",
            "1887 무기 3종이 각각 올바른 무기 종류 외형을 참조한다");
        Check(AssetDatabase.LoadAssetAtPath<InputActionReference>(PocWeaponDropAuthoring.INPUT_REF_PATH) != null, "Pickup 입력 참조 에셋이 있다");
    }

    // 플레이 모드: 실제 Game 씬의 GameManager로 구매·낙하·줍기를 돌린다. 씬에는 WireSavedGame이 먼저 적용돼 있어야 한다.
    public static void RunPlayChecks()
    {
        EditorSceneManager.OpenScene(PocWeaponDropAuthoring.GAME_SCENE, OpenSceneMode.Single);
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
    }

    private static void OnEditorUpdate()
    {
        if (!SessionState.GetBool(RUNNING, false) || !EditorApplication.isPlaying || playStarted)
        {
            return;
        }
        playStarted = true;
        EditorApplication.update += Step(PlayScript());
    }

    private static EditorApplication.CallbackFunction Step(IEnumerator _script)
    {
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= tick;
                return;
            }
            if (!_script.MoveNext())
            {
                EditorApplication.update -= tick;
                SessionState.EraseBool(RUNNING);
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => PocEditorExit.Exit(Finish());
            }
        };
        return tick;
    }

    private static IEnumerator PlayScript()
    {
        var gm = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        var weapon = UnityEngine.Object.FindFirstObjectByType<PlayerWeapon>();
        var interactor = UnityEngine.Object.FindFirstObjectByType<PlayerInteractor>();
        if (gm == null || weapon == null || interactor == null)
        {
            Check(false, "Game 씬에 GameManager·PlayerWeapon·PlayerInteractor가 있다");
            yield break;
        }

        // 멱등성: WireSavedGame을 두 번 돌려도 바닥 루트·들고 있는 무기 모델·카탈로그는 한 벌씩만 있어야 한다.
        Check(GameObject.Find("WeaponDrops") != null && UnityEngine.Object.FindObjectsByType<WeaponHeldModels>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1, "Game 씬의 바닥 무기 연결은 한 벌만 있다");
        Check(UnityEngine.Object.FindObjectsByType<WeaponPickupCatalog>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 1, "무기 카탈로그 인스턴스는 한 개다");

        int frames = 0;
        while (!(gm.Phase == GameManager.GamePhase.Wave && gm.Loadout != null) && frames++ < WAIT_LIMIT_FRAMES)
        {
            yield return null;
        }
        Check(gm.Phase == GameManager.GamePhase.Wave, "첫 웨이브가 시작된다");

        // 정비 상점 열기: 웨이브 시간을 0으로 두면 다음 Update에서 정상 종료 경로(EndWave)를 탄다.
        SetPrivate(gm, "waveTimeLeft", 0f);
        frames = 0;
        while (gm.Phase != GameManager.GamePhase.Shop && frames++ < WAIT_LIMIT_FRAMES)
        {
            yield return null;
        }
        Check(gm.Phase == GameManager.GamePhase.Shop, "정비 상점이 열린다");
        Check(!InputGate.PlayerEnabled, "상점 중 입력 게이트가 닫힌다");
        Check(!interactor.CanPickup(null), "상점 중 픽업 판정은 막힌다");
        Check(GetPrivate<string>(interactor, "lastPickupPrompt") == "", "상점 중 픽업 프롬프트가 비어 있다");

        gm.Wallet.Add(5000);
        int moneyStart = gm.Wallet.Balance;
        var shop = TableManager.Instance.Shop;

        // 파츠는 사용 중인 같은 종류 무기에 즉시 붙고, 그 무기를 따라 바닥까지 간다.
        Check(gm.TryBuyPart("m1873_heavy_barrel") == "" && gm.Parts.AttachedTo("m1873_heavy_barrel") == gm.Loadout.Active, "파츠 구매 시 사용 중인 M1873에 즉시 장착된다");
        Check(Mathf.Approximately(weapon.Settings.damage, 36f), "무거운 총열이 사용 중 무기 피해에 반영된다");
        Check(gm.TryBuyPart("m1873_heavy_barrel") == "" && gm.Parts.OwnedCount("m1873_heavy_barrel") == 2 && gm.Parts.StoredCount("m1873_heavy_barrel") == 1,
            "같은 파츠를 다시 사면 새 개체가 생기고, 붙일 빈 칸이 없으면 보관된다(중복 보유)");
        moneyStart = gm.Wallet.Balance;

        // 빈 슬롯이 있으면 구매 무기는 빈 칸에 들어가고 사용 중 무기는 그대로다.
        SetAmmo(weapon, 2);
        Check(gm.TryBuyWeapon("win1886", 0) == "", "Winchester 1886 구매가 성공한다");
        Check(gm.Loadout.Get(1)?.WeaponId == "win1886" && gm.Loadout.ActiveWeaponId == "m1873" && weapon.Ammo == 2, "빈 2번 슬롯에 들어가고 M1873을 계속 든다");
        Check(gm.Loadout.Get(1).Magazine == 8 && GroundWeapons().Count == 0, "새 무기는 탄창이 가득 차 있고 바닥에 놓인 무기는 없다");
        Check(gm.Wallet.Balance == moneyStart - ShopInfo.WeaponPrice(shop, "win1886"), "구매 가격만큼만 소지금이 줄어든다");
        Check(gm.TryBuyWeapon("mp18", 0) != "", "다음 시대 무기는 아직 살 수 없다");

        // 슬롯이 가득 차면 교체할 슬롯이 필요하다. 사용 중 슬롯을 교체하면 새 무기를 즉시 든다.
        int before = gm.Wallet.Balance;
        Check(gm.TryBuyWeapon("colt1878", -1) != "" && gm.Wallet.Balance == before, "슬롯이 가득 차면 교체 슬롯 없이 살 수 없다");
        Check(gm.TryBuyWeapon("colt1878", 0) == "" && gm.Loadout.ActiveWeaponId == "colt1878" && weapon.Ammo == 2, "사용 중 슬롯 교체 시 새 무기를 즉시 든다");
        var ground = GroundWeapons();
        Check(ground.Count == 1 && ground[0].WeaponId == "m1873" && ground[0].Ammo == 2 && gm.Parts.AttachedTo("m1873_heavy_barrel") == ground[0].Weapon,
            "교체된 M1873은 장전탄 2발과 파츠를 그대로 지닌 채 바닥에 놓인다");
        Check(gm.TryBuyWeapon("colt1878", 1) == "" && gm.Loadout.Get(0).WeaponId == "colt1878" && gm.Loadout.Get(1).WeaponId == "colt1878" && gm.Loadout.ActiveIndex == 0,
            "같은 무기를 두 자루 보유할 수 있고, 사용 중이 아닌 슬롯 교체는 현재 무기를 유지한다");
        Check(GroundWeapons().Count == 2 && GroundWeapons().Any(_p => _p.WeaponId == "win1886" && _p.Ammo == 8), "교체된 Winchester 1886도 탄창 그대로 바닥에 놓인다");

        gm.StartNextWave();
        frames = 0;
        while (gm.Phase != GameManager.GamePhase.Wave && frames++ < WAIT_LIMIT_FRAMES)
        {
            yield return null;
        }
        Check(gm.Phase == GameManager.GamePhase.Wave, "다음 웨이브가 시작된다");
        Check(GroundWeapons().Count == 2, "바닥 무기는 웨이브가 지나도 남는다");

        // 전환: 진행 중 재장전은 취소되고 장전탄은 무기마다 남는다.
        SetAmmo(weapon, 1);
        gm.Ammo.Add("ga12", 2);
        weapon.StartReload();
        Check(weapon.IsReloading, "재장전을 시작한다");
        Check(gm.SelectSlot(1) && !weapon.IsReloading && gm.Loadout.Get(0).Magazine == 1 && weapon.Ammo == 2, "슬롯 전환은 재장전을 취소하고 각 무기 장전탄을 유지한다");
        Check(gm.SelectSlot(0) && weapon.Ammo == 1, "다시 전환하면 이전 무기 장전탄 그대로다");

        // 가득 찬 슬롯에서 줍기: 사용 중 무기와 교환하고, 주운 무기는 장전탄·파츠를 유지한다.
        var revolverPickup = GroundWeapons().First(_p => _p.WeaponId == "m1873");
        Aim(interactor, GroundWeapons().First(_p => _p.WeaponId == "win1886"));
        Check(gm.TryPickup(revolverPickup) != "" && !revolverPickup.IsConsumed, "조준하지 않은 바닥 무기 직접 줍기는 거부된다");
        Aim(interactor, revolverPickup);
        Check(interactor.IsPickupInView(revolverPickup) && gm.TryPickup(revolverPickup) == "", "바닥 M1873을 줍는다");
        Check(gm.Loadout.ActiveWeaponId == "m1873" && weapon.Ammo == 2 && gm.Parts.AttachedTo("m1873_heavy_barrel") == gm.Loadout.Active, "주운 M1873은 장전탄 2발과 파츠를 유지한다");
        Check(Mathf.Approximately(weapon.Settings.damage, 36f), "주운 무기의 파츠 효과가 다시 적용된다");
        yield return null;
        Check(revolverPickup == null && gm.TryPickup(revolverPickup) != "", "주운 픽업은 한 번만 소비된다");
        ground = GroundWeapons();
        Check(ground.Count == 2 && ground.Any(_p => _p.WeaponId == "colt1878" && _p.Ammo == 1) && ground.Any(_p => _p.WeaponId == "win1886"), "교환된 Colt 1878이 장전탄 1발로 바닥에 놓인다");

        // 바닥 정리: 사망 경로(ClearGroundWeapons)가 바닥 무기를 모두 치운다.
        InvokePrivate(gm, "ClearGroundWeapons");
        yield return null;
        Check(GroundWeapons().Count == 0, "사망 정리 후 바닥 무기가 남지 않는다");

        Check(weapon != null && gm.Loadout.ActiveWeaponId == "m1873", "사망 정리는 현재 든 무기를 바꾸지 않는다");
        Debug.Log($"{TAG} 플레이 검사 완료");
    }

    private static List<WeaponPickup> GroundWeapons()
    {
        return UnityEngine.Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).Where(_p => !_p.IsConsumed).ToList();
    }

    // 실제 조준 카메라(PlayerInteractor.viewCamera)를 픽업 콜라이더 중심으로 돌린다. 같은 틱의 TryPickup 재검증이 이 시선을 쓴다.
    private static void Aim(PlayerInteractor _interactor, WeaponPickup _pickup)
    {
        Camera view = GetPrivate<Camera>(_interactor, "viewCamera");
        BoxCollider box = _pickup.GetComponent<BoxCollider>();
        view.transform.LookAt(box != null ? box.bounds.center : _pickup.transform.position);
    }

    private static void SetAmmo(PlayerWeapon _weapon, int _ammo)
    {
        _weapon.GetType().GetProperty("Ammo").GetSetMethod(true).Invoke(_weapon, new object[] { _ammo });
    }

    private static void SetPrivate(object _target, string _field, object _value)
    {
        _target.GetType().GetField(_field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_target, _value);
    }

    private static T GetPrivate<T>(object _target, string _field)
    {
        return (T)_target.GetType().GetField(_field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_target);
    }

    private static void InvokePrivate(object _target, string _method)
    {
        _target.GetType().GetMethod(_method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_target, null);
    }
}
