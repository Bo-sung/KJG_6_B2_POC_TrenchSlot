using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// CLI(격리 사본에서만 실행):
//   -batchmode -nographics -projectPath <copy> -executeMethod PocPickupPriorityVerification.Run  (no -quit)
// 빈 씬에 GameManager·플레이어·바닥 픽업을 직접 꾸미고, 실제 입력 자산(Player/Reload·Player/Pickup)과 가상 키보드·게임패드로
// X(게임패드 buttonWest) 줍기 우선순위, R 재장전, 대상 없는 X 재장전, 같은 프레임 중복 줍기, 거리·벽 거부, 벽 너머 낙하 거부를 확인한다.
// PlayerWeapon·PlayerInteractor의 Update 순서는 두 가지로 강제해 본다(컴포넌트를 끄고 입력 갱신 직후 원하는 순서로 직접 호출).
[InitializeOnLoad]
public static class PocPickupPriorityVerification
{
    private const string RUNNING = "PocPickupPriorityVerification.running";
    private const string TAG = "[PocPickupPriorityVerification]";
    private const int WAIT_LIMIT_FRAMES = 600;
    private static readonly Vector3 EYE = new Vector3(0f, 1.6f, 0f);

    private enum Order
    {
        Natural,
        WeaponFirst,
        InteractorFirst,
        Twice
    }

    private static int passes;
    private static int failures;
    private static bool playStarted;

    private static Order order;
    private static InputActionAsset input;
    private static Keyboard keyboard;
    private static Gamepad pad;
    private static InputSettings.BackgroundBehavior previousBackground;
    private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;

    private static GameManager gm;
    private static PlayerWeapon weapon;
    private static PlayerInteractor interactor;
    private static Camera view;
    private static WeaponPickupCatalog catalog;
    private static Transform dropRoot;
    private static readonly List<GameObject> walls = new List<GameObject>();
    private static MethodInfo weaponUpdate;
    private static MethodInfo interactorUpdate;

    static PocPickupPriorityVerification()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
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
            bool more;
            try
            {
                more = _script.MoveNext();
            }
            catch (Exception exception)
            {
                failures++;
                Debug.LogException(exception);
                more = false;
            }
            if (!more)
            {
                EditorApplication.update -= tick;
                Teardown();
                SessionState.EraseBool(RUNNING);
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () =>
                {
                    Debug.Log($"{TAG} 결과 pass={passes} fail={failures}");
                    EditorApplication.Exit(failures == 0 ? 0 : 1);
                };
            }
        };
        return tick;
    }

    private static IEnumerator PlayScript()
    {
        BuildRig();
        int frames = 0;
        while (gm.Phase != GameManager.GamePhase.Wave && frames++ < WAIT_LIMIT_FRAMES)
        {
            yield return null;
        }
        Check(gm.Phase == GameManager.GamePhase.Wave, "합성 씬에서 첫 웨이브가 시작된다");
        Check(input.FindAction("Player/Reload").bindings.Any(_b => _b.path == "<Gamepad>/buttonWest") &&
              input.FindAction("Player/Pickup").bindings.Any(_b => _b.path == "<Gamepad>/buttonWest"), "게임패드 X는 Reload·Pickup에 같이 묶여 있다");

        // 1) 둘러싸인 자리: 낙하 자리가 모두 벽 너머이면 구매·줍기가 돈·무기·픽업을 바꾸지 않고 거부된다.
        foreach (var step in ResetCase("revolver", 2)) yield return step;
        AddWall(new Vector3(0f, 1.5f, 0.7f), new Vector3(1.6f, 3f, 0.1f));
        AddWall(new Vector3(0f, 1.5f, -0.7f), new Vector3(1.6f, 3f, 0.1f));
        AddWall(new Vector3(0.7f, 1.5f, 0f), new Vector3(0.1f, 3f, 1.6f));
        AddWall(new Vector3(-0.7f, 1.5f, 0f), new Vector3(0.1f, 3f, 1.6f));
        Physics.SyncTransforms();
        SetPrivate(gm, "phase", GameManager.GamePhase.Shop);
        gm.Wallet.Add(1000);
        int money = gm.Wallet.Balance;
        var buyBolt = TableManager.Instance.Shop.items.First(_i => _i.id == "buy_bolt");
        Check(gm.TryBuy(buyBolt) != "" && gm.Wallet.Balance == money && gm.Loadout.IsEquipped("revolver") && !gm.Loadout.IsOwned("bolt") && Ground().Count == 0,
            "벽에 둘러싸여 놓을 자리가 없으면 구매가 거부되고 소지금·무기·바닥이 그대로다");
        SetPrivate(gm, "phase", GameManager.GamePhase.Wave);
        WeaponPickup boxed = Spawn("smg", new Vector3(0f, 0f, 0.35f), 7);
        Aim(boxed);
        Check(gm.TryPickup(boxed) != "" && !boxed.IsConsumed && gm.Loadout.IsEquipped("revolver") && Ground().Count == 1,
            "벽 너머로만 놓을 수 있으면 줍기가 거부되고 픽업은 소비되지 않는다");

        // 2) 벽 한 면: 바로 앞 후보(z=1.2)는 벽 너머라 거르고, 벽 안쪽 자리만 고른다.
        foreach (var step in ResetCase("revolver", 2)) yield return step;
        AddWall(new Vector3(0f, 1.5f, 0.95f), new Vector3(6f, 3f, 0.1f));
        Physics.SyncTransforms();
        object[] args = { Vector3.zero, null, catalog.Find("revolver") };
        bool found = (bool)typeof(GameManager).GetMethod("TryFindDropSpot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gm, args);
        Vector3 spot = (Vector3)args[0];
        Check(found && spot.z < 0.9f && !Physics.Linecast(new Vector3(0f, 0.9f, 0f), spot + Vector3.up * 0.15f),
            $"낙하 자리는 벽을 넘지 않는다(자리 {spot})");

        // 3) X + 유효 픽업: Update 순서 세 가지 모두 한 번만 바꾸고 어느 총도 재장전하지 않는다.
        foreach (Order mode in new[] { Order.WeaponFirst, Order.InteractorFirst, Order.Natural })
        {
            foreach (var step in ResetCase("revolver", 2)) yield return step;
            WeaponPickup bolt = Spawn("bolt", new Vector3(0f, 0f, 2.2f), 3);
            Aim(bolt);
            // 조준 판정은 프레임 단위 캐시라 카메라를 돌린 다음 프레임에 확인한다.
            foreach (var step in WaitFrames(1)) yield return step;
            Check(interactor.AimedPickup == bolt, $"[{mode}] 조준점의 볼트액션이 유효 픽업이다");
            SetOrder(mode);
            foreach (var step in Press(PadX())) yield return step;
            foreach (var step in WaitFrames(3)) yield return step;
            Check(gm.Loadout.IsEquipped("bolt") && weapon.Ammo == 3 && !weapon.IsReloading, $"[{mode}] X 줍기는 탄약 3발을 잇고 재장전하지 않는다");
            var ground = Ground();
            Check(ground.Count == 1 && ground[0].WeaponId == "revolver" && ground[0].Ammo == 2 && bolt == null, $"[{mode}] 이전 리볼버만 탄약 2발로 놓이고 픽업은 소비된다");
        }

        // 4) R은 유효 픽업을 보고 있어도 재장전하고 줍지 않는다.
        foreach (Order mode in new[] { Order.WeaponFirst, Order.InteractorFirst })
        {
            foreach (var step in ResetCase("revolver", 2)) yield return step;
            WeaponPickup bolt = Spawn("bolt", new Vector3(0f, 0f, 2.2f), 3);
            Aim(bolt);
            SetOrder(mode);
            foreach (var step in Press(new KeyboardState(Key.R))) yield return step;
            Check(weapon.IsReloading && gm.Loadout.IsEquipped("revolver") && bolt != null && !bolt.IsConsumed, $"[{mode}] R은 픽업 조준 중에도 재장전하고 줍지 않는다");
        }

        // 5) 대상 없는 X는 재장전한다.
        foreach (Order mode in new[] { Order.WeaponFirst, Order.InteractorFirst })
        {
            foreach (var step in ResetCase("revolver", 2)) yield return step;
            view.transform.rotation = Quaternion.LookRotation(Vector3.up);
            SetOrder(mode);
            foreach (var step in Press(PadX())) yield return step;
            Check(weapon.IsReloading && gm.Loadout.IsEquipped("revolver"), $"[{mode}] 줍을 대상이 없으면 X는 재장전한다");
        }

        // 6) 한 프레임에 Update가 두 번씩 돌아도 줍기는 한 번이다.
        foreach (var step in ResetCase("revolver", 2)) yield return step;
        WeaponPickup once = Spawn("bolt", new Vector3(0f, 0f, 2.2f), 3);
        Aim(once);
        SetOrder(Order.Twice);
        foreach (var step in Press(PadX())) yield return step;
        Check(gm.Loadout.IsEquipped("bolt") && !weapon.IsReloading && Ground().Count == 1 && Ground()[0].WeaponId == "revolver", "같은 프레임 중복 호출에도 교환은 한 번이다");

        // 7) 같은 프레임 직접 호출 A/B/A: 세 번째는 거부되고 무기가 복제되지 않는다.
        foreach (var step in ResetCase("revolver", 2)) yield return step;
        SetOrder(Order.WeaponFirst);
        WeaponPickup a = Spawn("bolt", new Vector3(0f, 0f, 2.2f), 3);
        WeaponPickup b = Spawn("smg", new Vector3(1.6f, 0f, 1.6f), 7);
        int frame = Time.frameCount;
        Aim(a);
        string first = gm.TryPickup(a);
        Aim(b);
        string second = gm.TryPickup(b);
        Aim(a);
        string third = gm.TryPickup(a);
        Check(Time.frameCount == frame && first == "" && second == "" && third != "", "같은 프레임 A/B/A에서 세 번째 A는 거부된다");
        Check(gm.Loadout.IsEquipped("smg") && weapon.Ammo == 7, "A/B 뒤 기관단총을 저장 탄약 그대로 든다");
        yield return null;
        var after = Ground();
        Check(after.Count == 2 && after.Count(_p => _p.WeaponId == "revolver" && _p.Ammo == 2) == 1 && after.Count(_p => _p.WeaponId == "bolt" && _p.Ammo == 3) == 1,
            "A/B/A 뒤 바닥에는 리볼버·볼트액션이 한 자루씩만 있다");

        // 8) 사거리 밖 픽업은 프롬프트·직접 호출 모두 거부된다.
        foreach (var step in ResetCase("revolver", 2)) yield return step;
        WeaponPickup far = Spawn("bolt", new Vector3(0f, 0f, 5f), 3);
        Aim(far);
        foreach (var step in WaitFrames(1)) yield return step;
        Check(interactor.AimedPickup == null && gm.TryPickup(far) != "" && !far.IsConsumed && gm.Loadout.IsEquipped("revolver"), "사거리 밖 픽업은 거부된다");

        // 9) 벽 너머 픽업: 상호작용 레이어 질의엔 걸려도 시선이 막혀 거부되고, X는 재장전으로 돌아간다.
        foreach (var step in ResetCase("revolver", 2)) yield return step;
        WeaponPickup hidden = Spawn("bolt", new Vector3(0f, 0f, 2.2f), 3);
        AddWall(new Vector3(0f, 1.5f, 1.5f), new Vector3(3f, 3f, 0.1f));
        Physics.SyncTransforms();
        Aim(hidden);
        foreach (var step in WaitFrames(1)) yield return step;
        Check(interactor.AimedPickup == null && gm.TryPickup(hidden) != "" && !hidden.IsConsumed, "벽 너머 픽업은 직접 호출도 거부된다");
        SetOrder(Order.Natural);
        foreach (var step in Press(PadX())) yield return step;
        Check(weapon.IsReloading && gm.Loadout.IsEquipped("revolver") && !hidden.IsConsumed, "벽 너머 픽업을 보고 누른 X는 재장전한다");

        // 10) 지난 조준 캐시로 들어온 직접 호출: 조준이 유효했던 픽업이라도 지금 시선 밖이면 거부된다.
        foreach (var step in ResetCase("revolver", 2)) yield return step;
        WeaponPickup stale = Spawn("bolt", new Vector3(0f, 0f, 2.2f), 3);
        Aim(stale);
        foreach (var step in WaitFrames(1)) yield return step;
        bool wasAimed = interactor.AimedPickup == stale;
        view.transform.rotation = Quaternion.LookRotation(Vector3.up);
        Check(wasAimed && gm.TryPickup(stale) != "" && !stale.IsConsumed && gm.Loadout.IsEquipped("revolver"), "조준이 벗어난 뒤의 직접 줍기는 거부된다");

        Debug.Log($"{TAG} 플레이 검사 완료");
    }

    private static void BuildRig()
    {
        previousBackground = InputSystem.settings.backgroundBehavior;
        previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        // 헤드리스 CLI에는 포커스된 Game 뷰가 없으므로 가상 장치 입력을 플레이 모드로 보낸다.
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        input = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
        keyboard = InputSystem.AddDevice<Keyboard>();
        pad = InputSystem.AddDevice<Gamepad>();

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(40f, 1f, 40f);

        GameObject player = new GameObject("Player");
        player.SetActive(false);
        PlayerHealth health = player.AddComponent<PlayerHealth>();
        GameObject camera = new GameObject("View");
        camera.transform.SetParent(player.transform, false);
        camera.transform.localPosition = EYE;
        view = camera.AddComponent<Camera>();
        weapon = player.AddComponent<PlayerWeapon>();
        interactor = player.AddComponent<PlayerInteractor>();
        var reloadRef = InputActionReference.Create(input.FindAction("Player/Reload", true));
        var pickupRef = InputActionReference.Create(input.FindAction("Player/Pickup", true));
        SetPrivate(weapon, "reloadAction", reloadRef);
        SetPrivate(weapon, "pickupAction", pickupRef);
        SetPrivate(weapon, "aimCamera", view);
        SetPrivate(weapon, "health", health);
        SetPrivate(weapon, "interactor", interactor);
        SetPrivate(weapon, "hitLayers", (LayerMask)1);
        SetPrivate(interactor, "pickupAction", pickupRef);
        SetPrivate(interactor, "viewCamera", view);
        SetPrivate(interactor, "health", health);
        SetPrivate(interactor, "interactLayers", (LayerMask)(1 << 9));

        GameObject manager = new GameObject("GameManager");
        manager.SetActive(false);
        gm = manager.AddComponent<GameManager>();
        WaveController waves = manager.AddComponent<WaveController>();
        waves.enabled = false;
        catalog = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PocWeaponDropAuthoring.CATALOG_PREFAB_PATH)).GetComponent<WeaponPickupCatalog>();
        dropRoot = new GameObject("WeaponDrops").transform;
        SetPrivate(gm, "waves", waves);
        SetPrivate(gm, "playerHealth", health);
        SetPrivate(gm, "weapon", weapon);
        SetPrivate(gm, "interactor", interactor);
        SetPrivate(gm, "pickups", catalog);
        SetPrivate(gm, "dropRoot", dropRoot);

        player.SetActive(true);
        manager.SetActive(true);

        weaponUpdate = typeof(PlayerWeapon).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        interactorUpdate = typeof(PlayerInteractor).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        InputSystem.onAfterUpdate += DriveOrdered;
    }

    // 강제 순서 모드에서는 두 컴포넌트를 끄고, 입력 갱신 직후(같은 프레임, Update 이전)에 정한 순서로 Update를 직접 부른다.
    private static void DriveOrdered()
    {
        if (order == Order.Natural || InputState.currentUpdateType != InputUpdateType.Dynamic || weapon == null)
        {
            return;
        }
        switch (order)
        {
            case Order.WeaponFirst:
                weaponUpdate.Invoke(weapon, null);
                interactorUpdate.Invoke(interactor, null);
                break;
            case Order.InteractorFirst:
                interactorUpdate.Invoke(interactor, null);
                weaponUpdate.Invoke(weapon, null);
                break;
            case Order.Twice:
                weaponUpdate.Invoke(weapon, null);
                interactorUpdate.Invoke(interactor, null);
                interactorUpdate.Invoke(interactor, null);
                weaponUpdate.Invoke(weapon, null);
                break;
        }
    }

    private static void SetOrder(Order _order)
    {
        order = _order;
        bool natural = _order == Order.Natural;
        weapon.enabled = natural;
        interactor.enabled = natural;
        // 끈 컴포넌트의 OnDisable이 입력을 끄므로 직접 다시 켠다.
        input.FindAction("Player/Reload", true).Enable();
        input.FindAction("Player/Pickup", true).Enable();
    }

    private static IEnumerable ResetCase(string _equip, int _ammo)
    {
        foreach (var wall in walls)
        {
            UnityEngine.Object.Destroy(wall);
        }
        walls.Clear();
        InvokePrivate(gm, "ClearGroundWeapons");
        gm.Loadout.Own(_equip);
        gm.Loadout.Equip(_equip);
        InvokePrivate(gm, "ApplyWeapon", _equip, _ammo);
        weapon.CancelReload();
        SetPrivate(gm, "waveTimeLeft", 9999f);
        SetOrder(Order.Natural);
        view.transform.localRotation = Quaternion.identity;
        yield return null;
        yield return null;
        Physics.SyncTransforms();
    }

    private static IEnumerable Press<T>(T _state) where T : struct, IInputStateTypeInfo
    {
        InputDevice device = typeof(T) == typeof(KeyboardState) ? (InputDevice)keyboard : pad;
        InputSystem.QueueStateEvent(device, _state);
        foreach (var step in WaitFrames(2)) yield return step;
        if (device == keyboard)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }
        else
        {
            InputSystem.QueueStateEvent(pad, new GamepadState());
        }
        foreach (var step in WaitFrames(2)) yield return step;
    }

    private static GamepadState PadX() => new GamepadState().WithButton(GamepadButton.West);

    private static IEnumerable WaitFrames(int _count)
    {
        int target = Time.frameCount + _count;
        int guard = 0;
        while (Time.frameCount < target && guard++ < WAIT_LIMIT_FRAMES)
        {
            yield return null;
        }
    }

    private static WeaponPickup Spawn(string _id, Vector3 _position, int _ammo)
    {
        WeaponPickup pickup = UnityEngine.Object.Instantiate(catalog.Find(_id), _position, Quaternion.identity, dropRoot);
        pickup.Initialize(_id, "", _ammo);
        Physics.SyncTransforms();
        return pickup;
    }

    private static void AddWall(Vector3 _center, Vector3 _size)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "TestWall";
        wall.transform.position = _center;
        wall.transform.localScale = _size;
        walls.Add(wall);
    }

    private static void Aim(WeaponPickup _pickup)
    {
        view.transform.LookAt(_pickup.transform.position + Vector3.up * 0.12f);
    }

    private static List<WeaponPickup> Ground()
    {
        return UnityEngine.Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None).Where(_p => !_p.IsConsumed).ToList();
    }

    private static void Teardown()
    {
        InputSystem.onAfterUpdate -= DriveOrdered;
        if (keyboard != null)
        {
            InputSystem.RemoveDevice(keyboard);
        }
        if (pad != null)
        {
            InputSystem.RemoveDevice(pad);
        }
        InputSystem.settings.backgroundBehavior = previousBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
    }

    private static void SetPrivate(object _target, string _field, object _value)
    {
        _target.GetType().GetField(_field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_target, _value);
    }

    private static void InvokePrivate(object _target, string _method, params object[] _args)
    {
        _target.GetType().GetMethod(_method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_target, _args);
    }
}
