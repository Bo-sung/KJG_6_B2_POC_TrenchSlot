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

        var loadout = new WeaponLoadout("revolver");
        Check(loadout.IsOwned("revolver") && loadout.IsEquipped("revolver"), "시작 무기는 획득·장착 상태다");
        Check(!loadout.Equip("bolt"), "미획득 무기는 장착할 수 없다(바이패스 차단)");
        loadout.Own("bolt");
        Check(loadout.IsOwned("bolt") && !loadout.IsEquipped("bolt"), "구매 기록만으로는 장착되지 않는다");
        Check(loadout.Equip("bolt") && loadout.IsEquipped("bolt"), "획득한 무기는 들고 있는 무기로 바꿀 수 있다");
        Check(!loadout.Equip("bolt"), "이미 든 무기 장착은 무시된다");
        Check(loadout.Equip("revolver") && loadout.IsEquipped("revolver") && loadout.IsOwned("bolt"), "이전 무기로 되돌아가도 획득 기록은 유지된다");

        var root = new GameObject("WeaponAmmoProbe");
        var weapon = root.AddComponent<PlayerWeapon>();
        var settings = new PlayerWeapon.WeaponSettings { magazineSize = 6, shotsPerSecond = 2.5f, range = 60f, damage = 25f, reloadSeconds = 1.8f, projectileSpeed = 120f, projectileLifetime = 1f, projectileRadius = 0.05f };
        weapon.Configure(settings);
        Check(weapon.Ammo == 6, "새 구매 무기는 탄창을 가득 채운다");
        weapon.Configure(settings, 2);
        Check(weapon.Ammo == 2, "주운 무기는 저장된 탄약을 그대로 이어받는다");
        weapon.Configure(settings, 99);
        Check(weapon.Ammo == 6, "저장 탄약이 탄창을 넘으면 탄창으로 제한한다");
        weapon.Configure(settings, 3);
        weapon.StartReload();
        Check(weapon.IsReloading, "재장전 시작");
        weapon.CancelReload();
        Check(!weapon.IsReloading && weapon.Ammo == 3, "재장전 취소 후 탄약은 채워지지 않는다");

        // X 컨텍스트 주움 우선순위는 이번 프레임의 픽업 키 입력 + 이번 프레임 조준으로 정한 예약(PickupClaimThisFrame)만 본다.
        // 조준 가능한 유효 픽업이 있어도 이번 프레임에 픽업 키가 눌리지 않았으면 재장전을 막지 않는다(R·대상 없는 X는 재장전).
        // 실제 입력·양쪽 Update 순서 검사는 PocPickupPriorityVerification이 맡는다.
        Check(typeof(PlayerInteractor).GetProperty("PickupClaimThisFrame") != null, "PlayerInteractor가 프레임 단위 줍기 예약을 노출한다");
        PlayerInteractor ctxInteractor = new GameObject("CtxInteractorProbe").AddComponent<PlayerInteractor>();
        WeaponPickup contextPickup = new GameObject("CtxPickupProbe").AddComponent<WeaponPickup>();
        contextPickup.Initialize("bolt", "", 5);
        ctxInteractor.CanPickup = _candidate => _candidate != null && _candidate.WeaponId == "bolt";
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
        EditorApplication.Exit(Finish());
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
        foreach (string id in new[] { "revolver", "bolt", "smg" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(PocWeaponDropAuthoring.PICKUP_PREFAB_FORMAT, id));
            var pickup = prefab != null ? prefab.GetComponent<WeaponPickup>() : null;
            Check(pickup != null && pickup.WeaponId == id, $"픽업 프리팹 {id}의 무기 id 일치");
            Check(prefab != null && prefab.layer == PICKUP_LAYER && prefab.GetComponentInChildren<MeshRenderer>() != null, $"픽업 프리팹 {id}는 상호작용 레이어에 보이는 모델을 가진다");
            Check(catalogComponent != null && catalogComponent.Find(id) != null, $"카탈로그가 {id} 픽업을 가진다");
        }
        var held = AssetDatabase.LoadAssetAtPath<GameObject>(PocWeaponDropAuthoring.HELD_PREFAB_PATH);
        var heldComponent = held != null ? held.GetComponent<WeaponHeldModels>() : null;
        Check(heldComponent != null && held.transform.childCount == 3, "들고 있는 무기 프리팹은 세 모델을 가진다");
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
                EditorApplication.delayCall += () => EditorApplication.Exit(Finish());
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

        gm.Wallet.Add(1000);
        var items = TableManager.Instance.Shop.items;
        var bolt = items.First(_i => _i.id == "buy_bolt");
        var smg = items.First(_i => _i.id == "buy_smg");
        int moneyStart = gm.Wallet.Balance;

        // 탄약 스냅샷: 리볼버를 2발 상태로 만들어 놓고 구매 낙하가 그 탄약을 들고 가는지 본다.
        SetAmmo(weapon, 2);
        Check(gm.TryBuy(bolt) == "", "볼트액션 구매가 성공한다");
        Check(gm.Loadout.IsEquipped("bolt") && gm.Loadout.IsOwned("revolver"), "구매 후 볼트액션을 들고 리볼버는 보유 기록으로 남는다");
        Check(gm.Wallet.Balance == moneyStart - bolt.price, "구매 가격만큼만 소지금이 줄어든다");
        Check(weapon.Ammo == 5, "새로 산 무기는 탄창을 가득 채운다");
        var ground = GroundWeapons();
        Check(ground.Count == 1 && ground[0].WeaponId == "revolver" && ground[0].Ammo == 2, "이전 무기가 탄약 그대로 바닥에 떨어진다");

        // 실패 구매: 이미 획득한 무기는 다시 사거나 무료 장착할 수 없다. 돈·현재 무기·바닥 개수가 그대로여야 한다.
        int groundBefore = GroundWeapons().Count;
        Check(gm.TryBuy(bolt) != "" && gm.Loadout.IsEquipped("bolt"), "보유 무기 재구매는 거부되고 현재 무기는 그대로다");
        Check(gm.Wallet.Balance == moneyStart - bolt.price && GroundWeapons().Count == groundBefore, "거부된 구매는 소지금과 바닥 무기를 바꾸지 않는다");

        Check(gm.TryBuy(smg) == "", "기관단총 구매가 성공한다");
        ground = GroundWeapons();
        Check(ground.Count == 2 && ground.Any(_p => _p.WeaponId == "revolver") && ground.Any(_p => _p.WeaponId == "bolt" && _p.Ammo == 5), "두 번째 구매 후 바닥에 리볼버·볼트액션이 한 자루씩만 있다");
        Check(weapon.Ammo == 30, "기관단총은 탄창을 가득 채워 든다");

        // 픽업 게이트는 웨이브 중에만 열린다. 상점에서는 판정이 막혀 있어야 한다.
        gm.StartNextWave();
        frames = 0;
        while (gm.Phase != GameManager.GamePhase.Wave && frames++ < WAIT_LIMIT_FRAMES)
        {
            yield return null;
        }
        Check(gm.Phase == GameManager.GamePhase.Wave, "다음 웨이브가 시작된다");

        var revolverPickup = GroundWeapons().First(_p => _p.WeaponId == "revolver");
        Check(interactor.CanPickup(revolverPickup), "웨이브 중 바닥 리볼버를 주울 수 있다");
        // TryPickup은 지금 카메라 시선·거리를 다시 확인한다. 조준하지 않은 직접 호출은 거부되고 아무것도 바뀌지 않는다.
        Aim(interactor, GroundWeapons().First(_p => _p.WeaponId == "bolt"));
        Check(gm.TryPickup(revolverPickup) != "" && !revolverPickup.IsConsumed && gm.Loadout.IsEquipped("smg") && GroundWeapons().Count == 2,
            "조준하지 않은 바닥 리볼버 직접 줍기는 거부되고 무기·바닥이 그대로다");
        Check(interactor.CanPickup(revolverPickup) && !interactor.CanPickup(null), "픽업 게이트는 빈 대상을 거부한다");

        // 재장전 중 줍기: 재장전은 취소되고 탄약은 이득 없이 그대로 간다.
        SetAmmo(weapon, 1);
        weapon.StartReload();
        Check(weapon.IsReloading, "재장전을 시작한다");
        Aim(interactor, revolverPickup);
        Check(interactor.IsPickupInView(revolverPickup), "카메라를 바닥 리볼버에 조준한다");
        Check(gm.TryPickup(revolverPickup) == "", "바닥 리볼버를 줍는다");
        Check(gm.Loadout.IsEquipped("revolver") && weapon.Ammo == 2, "주운 리볼버는 놓였을 때 탄약 2발을 이어받는다");
        Check(!weapon.IsReloading, "줍기 시 재장전은 취소된다");
        // Destroy는 프레임 끝에 적용되므로 소비 결과는 한 프레임 뒤에 본다.
        yield return null;
        Check(revolverPickup == null, "주운 픽업은 한 번만 소비되어 사라진다");
        Check(gm.TryPickup(revolverPickup) != "", "소비된 픽업은 다시 줍을 수 없다");
        Check(!interactor.IsPickupInView(revolverPickup), "소비된 픽업은 시선 판정에서도 빠진다");
        ground = GroundWeapons();
        Check(ground.Count == 2 && ground.Any(_p => _p.WeaponId == "smg" && _p.Ammo == 1) && ground.Any(_p => _p.WeaponId == "bolt" && _p.Ammo == 5), "줍기 후 기관단총은 떨어진 순간의 탄약 1발 그대로 떨어지고 볼트액션은 그대로 있다");

        // 바닥의 볼트액션을 주워 교환해도 탄약을 채워 주지 않는다(볼트 5발 그대로).
        var boltPickup = GroundWeapons().First(_p => _p.WeaponId == "bolt");
        Aim(interactor, boltPickup);
        Check(gm.TryPickup(boltPickup) == "" && gm.Loadout.IsEquipped("bolt") && weapon.Ammo == 5, "볼트액션 줍기는 저장 탄약 5발을 그대로 잇는다");
        Check(GroundWeapons().Count(_p => _p.WeaponId == "revolver") == 1 && GroundWeapons().Count(_p => _p.WeaponId == "smg") == 1, "교환 뒤에도 무기마다 바닥 한 자루씩만 있다");

        // 바닥 정리: 사망 경로(ClearGroundWeapons)가 바닥 무기를 모두 치운다.
        InvokePrivate(gm, "ClearGroundWeapons");
        yield return null;
        Check(GroundWeapons().Count == 0, "사망 정리 후 바닥 무기가 남지 않는다");

        Check(weapon != null && gm.Loadout.IsEquipped("bolt"), "사망 정리는 현재 든 무기를 바꾸지 않는다");
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
