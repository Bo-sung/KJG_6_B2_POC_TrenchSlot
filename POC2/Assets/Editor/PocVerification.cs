using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;

// 검증 진입점(Unity CLI -executeMethod). 두 가지를 본다.
//  - RunUnitChecks: 테이블·판정·확률(RTP)·퍽·웨이브 스케일링·소지금·무기 보유 로직
//  - RunFeedbackChecks: HUD 프리팹·씬 직렬화 참조, 빈 탄창·재장전 문구(에디터, 플레이 없음)
//  - RunPlayModeSmoke:Game 씬을 플레이 모드로 돌려 회전·처치·정비 전환·재시작 흐름을 확인
public static class PocVerification
{
    private const string GAME_SCENE = "Assets/Scenes/Game.unity";
    private const double RTP_TOLERANCE = 0.005;
    private const int MONTE_CARLO_SPINS = 2000000;
    private const string TAG = "[PocVerification]";
    private const string MODE_KEY = "PocVerification.Mode";
    private const string PRELOAD_SCENE = "Assets/Scenes/Preload.unity";

    private static int failures;
    private static int resolvedSpins;
    private static int lastPayout;
    private static bool clearedSeen;
    private static bool gameOverSeen;
    private static int smokeStep;
    private static double playStart = -1d;
    private static double stepStart;
    private static GameManager gm;
    private static SlotMachinePresenter slot;
    private static WaveController waves;
    private static PlayerHealth player;
    private static int spinStartBalance;
    private static PlayerWeapon weapon;

    public static void RunInputChecks()
    {
        failures = 0;
        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var movement = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var gun = UnityEngine.Object.FindFirstObjectByType<PlayerWeapon>();
        var interactor = UnityEngine.Object.FindFirstObjectByType<PlayerInteractor>();
        Check(movement != null && movement.GetComponent<CharacterController>() != null, "scene player uses CharacterController");
        Check(gun != null && interactor != null, "scene weapon and interactor exist");
        Check(UnityEngine.Object.FindFirstObjectByType<InputSystemUIInputModule>() != null, "game scene uses Input System UI module");
        foreach (var pair in new (Component component, string field)[] { (movement, "moveAction"), (movement, "lookAction"), (gun, "fireAction"), (gun, "reloadAction"), (interactor, "interactAction") })
        {
            var reference = pair.component != null ? new SerializedObject(pair.component).FindProperty(pair.field).objectReferenceValue as InputActionReference : null;
            Check(reference != null && reference.action != null, $"scene {pair.field} action reference wired");
        }
        EditorSceneManager.OpenScene("Assets/Scenes/Title.unity", OpenSceneMode.Single);
        Check(UnityEngine.Object.FindFirstObjectByType<InputSystemUIInputModule>() != null, "title scene uses Input System UI module");
        SessionState.SetInt("PocVerification.InputFailures", failures);
        SessionState.SetString(MODE_KEY, "input");
        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        RegisterTick(InputTick);
        EditorApplication.isPlaying = true;
    }

    private static Gamepad testPad;
    private static int inputStep;
    private static bool inputChecked;
    private static void InputTick()
    {
        if (!EditorApplication.isPlaying) return;
        if (inputStep++ == 0)
        {
            failures = SessionState.GetInt("PocVerification.InputFailures", 0);
            testPad = InputSystem.AddDevice<Gamepad>();
            return;
        }
        if (inputStep == 2)
        {
            var state = new GamepadState { leftStick = new Vector2(0f, 1f), rightStick = new Vector2(1f, 0f), rightTrigger = 1f };
            state.WithButton(GamepadButton.West);
            state.WithButton(GamepadButton.North);
            state.WithButton(GamepadButton.South);
            Debug.Log($"{TAG} queued sticks {state.leftStick}/{state.rightStick}");
            InputSystem.onAfterUpdate += CheckInputState;
            InputSystem.QueueStateEvent(testPad, state);
            return;
        }
        if (!inputChecked) return;
        InputSystem.onAfterUpdate -= CheckInputState;
        InputSystem.RemoveDevice(testPad);
        Debug.Log($"{TAG} input checks finished: failures={failures}");
        SessionState.SetString(MODE_KEY, "");
        EditorApplication.update -= InputTick;
        EditorApplication.isPlaying = false;
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    private static void CheckInputState()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || inputChecked) return;
        inputChecked = true;
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var move = asset.FindAction("Player/Move", true);
        var look = asset.FindAction("Player/Look", true);
        var fire = asset.FindAction("Player/Attack", true);
        var reload = asset.FindAction("Player/Reload", true);
        var interact = asset.FindAction("Player/Interact", true);
        var ui = UnityEngine.Object.FindFirstObjectByType<InputSystemUIInputModule>();
        var navigate = ui.move.action;
        var submit = ui.submit.action;
        Debug.Log($"{TAG} input values pad={testPad.leftStick.ReadValue()}/{testPad.rightStick.ReadValue()} raw={testPad.leftStick.ReadUnprocessedValue()}/{testPad.rightStick.ReadUnprocessedValue()} move={move.ReadValue<Vector2>()} look={look.ReadValue<Vector2>()} nav={navigate.ReadValue<Vector2>()} submit={submit.IsPressed()} enabled={move.enabled}/{look.enabled}/{navigate.enabled}/{submit.enabled}");
        Check(move.ReadValue<Vector2>().y > 0.9f && look.ReadValue<Vector2>().x > 0.9f, "virtual gamepad move and look bindings");
        Check(fire.IsPressed() && reload.IsPressed() && interact.IsPressed(), "virtual gamepad fire reload interact bindings");
        Check(navigate.ReadValue<Vector2>().y > 0f && submit.IsPressed(), "virtual gamepad UI navigate and submit bindings");
    }

    private static void Check(bool _ok, string _label)
    {
        if (!_ok)
        {
            failures++;
        }
        Debug.Log($"{TAG} {(_ok ? "PASS" : "FAIL")} {_label}");
    }

    public static void RunUnitChecks()
    {
        failures = 0;
        var table = TableManager.Initialize();
        var slotTable = table.SlotMachine;

        Check(slotTable.symbols.Length == 6, "slot symbol count is 6");
        Check(SlotMachineInfo.SpinCost(slotTable) == 10, "spin cost base 10");
        int total = SlotMachineInfo.TotalWeight(slotTable, 1);
        Check(total == 2396, $"six weights sum to 2396 (got {total})");
        Check(SlotMachineInfo.PickFace(slotTable, 0, 1) == 0, "roll 0 maps to rifle (reel 0)");
        Check(SlotMachineInfo.PickFace(slotTable, 207, 1) == 0, "roll 207 still rifle");
        Check(SlotMachineInfo.PickFace(slotTable, 208, 1) == 1, "roll 208 maps to helmet (reel 1)");
        Check(SlotMachineInfo.PickFace(slotTable, 2395, 1) == 5, "last roll maps to seven (reel 5)");
        Check(SlotMachineInfo.TotalWeight(slotTable, 3) == 2396 + 104 * 2, "ammo weight scale x3 raises ammo weight only");

        var triple = SlotMachineInfo.Evaluate(slotTable, new[] { 0, 0, 0 });
        Check(triple.tripleFace == 0 && triple.multiplier == 40, "rifle x3 pays x40");
        var pair = SlotMachineInfo.Evaluate(slotTable, new[] { 2, 2, 5 });
        Check(pair.pairFace == 2 && pair.multiplier == 1 && pair.tripleFace < 0, "shell x2 pays x1");
        var near = SlotMachineInfo.Evaluate(slotTable, new[] { 0, 0, 1 });
        Check(near.pairFace == 0 && near.multiplier == 0 && near.isNearMiss, "rifle pair without pay is near miss");
        var ammo = SlotMachineInfo.Evaluate(slotTable, new[] { 4, 1, 3 });
        Check(ammo.hasAmmo && ammo.multiplier == 0, "ammo on one reel refills, no pay");
        var none = SlotMachineInfo.Evaluate(slotTable, new[] { 1, 2, 3 });
        Check(none.multiplier == 0 && !none.hasAmmo && none.pairFace < 0 && !none.isNearMiss, "all different is a plain miss");
        var seven = SlotMachineInfo.Evaluate(slotTable, new[] { 5, 5, 5 });
        Check(seven.multiplier == 50, "seven x3 pays x50");

        double analytic = AnalyticRtp(slotTable);
        Check(Math.Abs(analytic - 0.6941) < 0.001, $"analytic RTP ~0.694 with independent reels (got {analytic:0.0000})");
        double simulated = SimulateRtp(slotTable, MONTE_CARLO_SPINS);
        Check(Math.Abs(simulated - analytic) < RTP_TOLERANCE, $"simulated RTP {simulated:0.0000} matches analytic {analytic:0.0000}");

        var perks = new PerkInventory(table.Perk);
        Check(perks.ApplySpinCost(10) == 10, "no discount keeps cost 10");
        // 슬롯 업그레이드는 런당 각 1회(상점-세부설계 §6).
        perks.Buy(PerkInventory.DISCOUNT_LEVER);
        Check(perks.ApplySpinCost(10) == 8 && !perks.CanBuy(PerkInventory.DISCOUNT_LEVER), "discount 1 stack -> 8, once per run");
        perks.Buy(PerkInventory.DISCOUNT_LEVER);
        Check(perks.ApplySpinCost(10) == 8, "extra discount purchase is ignored");
        perks.Buy(PerkInventory.FAST_SPINDLE);
        Check(Math.Abs(perks.ApplySpinSeconds(2.4f) - 1.68f) < 0.001f, "fast spindle -30% of 2.4s = 1.68s");
        perks.Buy(PerkInventory.KILL_STACK);
        perks.Buy(PerkInventory.KILL_STACK);
        Check(Math.Abs(perks.KillBonusPerKill - 0.02f) < 0.0001f, "kill stack once per run -> +2% per kill");
        perks.Buy(PerkInventory.AMMO_SYMBOL);
        Check(perks.AmmoWeightScale == 3, "ammo symbol perk weight x3");

        var w1 = WaveInfo.Resolve(table.Wave, 1);
        Check(w1.rushers == 3 && w1.riflemen == 2 && w1.heavies == 0 && w1.era == "MUSKET", "wave 1 table row");
        Check(w1.enemyWeapon == "musket", "wave 1 ranged enemies carry muskets");
        var w3 = WaveInfo.Resolve(table.Wave, 3);
        Check(w3.enemyWeapon == "musket" && w3.era == "MUSKET", "wave 3 still muskets");
        // 시대 구간: 1~5 MUSKET(1887) · 6~10 WWI · 11~15 WWII, 이후 WWII 무한(상점-세부설계 §2).
        var w5 = WaveInfo.Resolve(table.Wave, 5);
        Check(w5.enemyWeapon == "musket" && w5.era == "MUSKET", "wave 5 still muskets");
        var w4 = WaveInfo.Resolve(table.Wave, 6);
        Check(w4.enemyWeapon == "wwi_rifle" && w4.era == "WWI", "wave 6 switches to WWI rifles");
        var w9 = WaveInfo.Resolve(table.Wave, 9);
        Check(w9.enemyWeapon == "wwi_rifle" && w9.era == "WWI", "wave 9 still WWI rifles");
        Check(WaveInfo.Resolve(table.Wave, 10).heavies == 1 && WaveInfo.Resolve(table.Wave, 10).era == "WWI", "wave 10 introduces heavy in WWI");
        var w10 = WaveInfo.Resolve(table.Wave, 11);
        Check(w10.era == "WWII" && w10.enemyWeapon == "wwii_rifle", "wave 11 switches to WWII rifles");
        var w13 = WaveInfo.Resolve(table.Wave, 16);
        Check(w13.rushers == 11 && w13.riflemen == 10 && w13.heavies == 4 && w13.era == "WWII" && Math.Abs(w13.hpMultiplier - 1.1f) < 0.0001f, "wave 16 scales beyond table as WWII infinite");
        Check(w13.enemyWeapon == "wwii_rifle", "scaled wave 13 keeps WWII rifles");

        var enemyWeapons = table.EnemyWeapon;
        Check(enemyWeapons.weapons.Length == 3 && EnemyWeaponInfo.Find(enemyWeapons, "musket") != null && EnemyWeaponInfo.Find(enemyWeapons, "wwi_rifle") != null && EnemyWeaponInfo.Find(enemyWeapons, "wwii_rifle") != null, "three enemy weapons defined");
        var rusherStats = EnemyInfo.Find(table.Enemy, EnemyInfo.RUSHER_ID);
        var riflemanStats = EnemyInfo.Find(table.Enemy, EnemyInfo.RIFLEMAN_ID);
        var heavyStats = EnemyInfo.Find(table.Enemy, EnemyInfo.HEAVY_ID);
        Check(EnemyWeaponInfo.ForEnemy(rusherStats, enemyWeapons, w10.enemyWeapon) == null, "rusher never gets a gun, even in wave 11");
        Check(EnemyWeaponInfo.ForEnemy(rusherStats, enemyWeapons, w1.enemyWeapon) == null, "rusher never gets a gun, even in wave 1");
        var riflemanWeapon = EnemyWeaponInfo.ForEnemy(riflemanStats, enemyWeapons, w1.enemyWeapon);
        Check(riflemanWeapon != null && riflemanWeapon.id == "musket", "wave 1 rifleman resolves to musket");
        var heavyWeapon = EnemyWeaponInfo.ForEnemy(heavyStats, enemyWeapons, w10.enemyWeapon);
        Check(heavyWeapon != null && heavyWeapon.id == "wwii_rifle", "wave 11 heavy resolves to WWII rifle");
        Check(EnemyWeaponInfo.ForEnemy(riflemanStats, enemyWeapons, "") == null, "ranged enemy without a wave weapon stays unarmed");

        var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PocAssetBuilder.GAMEPLAY_PREFAB_DIR}/Enemy.prefab");
        var mount = enemyPrefab != null ? enemyPrefab.transform.Find("WeaponMount") : null;
        Check(mount != null && mount.childCount == 3 && mount.Find("musket") != null && mount.Find("wwi_rifle") != null && mount.Find("wwii_rifle") != null, "enemy prefab has one gun model per enemy weapon");

        // 실제 프리팹을 편집 모드에서 세우고 Configure만 호출한다. 플레이 모드 없이 무기 값·모델 전환 경로를 확인한다.
        var armedRifle = UnityEngine.Object.Instantiate(enemyPrefab).GetComponent<Enemy>();
        armedRifle.Configure(riflemanStats, heavyWeapon, 1f, Color.gray, null);
        Check(armedRifle.WeaponId == "wwii_rifle" && ActiveGunModel(armedRifle.transform.Find("WeaponMount")) == "wwii_rifle", "armed enemy shows only its wave weapon model");
        var meleeRusher = UnityEngine.Object.Instantiate(enemyPrefab).GetComponent<Enemy>();
        meleeRusher.Configure(rusherStats, heavyWeapon, 1f, Color.gray, null);
        Check(meleeRusher.WeaponId == "" && ActiveGunModel(meleeRusher.transform.Find("WeaponMount")) == "", "melee rusher ignores a weapon even when one is passed");

        armedRifle.TakeDamage(1f);
        Check(armedRifle.IsFlashing && !armedRifle.IsDead, "non-lethal hit starts the hit flash");
        meleeRusher.TakeDamage(100000f);
        Check(meleeRusher.IsDead && meleeRusher.IsFlashing, "lethal hit kills and flashes the body");
        UnityEngine.Object.DestroyImmediate(armedRifle.gameObject);
        UnityEngine.Object.DestroyImmediate(meleeRusher.gameObject);

        Check(EffectSpawner.Exists(EffectSpawner.MUZZLE_FLASH) && EffectSpawner.Exists(EffectSpawner.HIT_SPARK), "muzzle flash and hit spark prefabs load from Resources");
        Check(table.Weapon.status == "provisional" && table.EnemyWeapon.status == "provisional", "weapon tables are marked provisional");

        var wallet = new PlayerWallet(50);
        Check(wallet.TrySpend(10) && wallet.Balance == 40, "spend 10 from 50");
        Check(!wallet.TrySpend(100) && wallet.Balance == 40, "cannot overspend");

        Check(table.GameConfig.maxHealth == 100 && table.GameConfig.startMoney == 50, "game config loaded");
        var startRevolver = WeaponInfo.Find(table.Weapon, WeaponInfo.StartWeaponId(table.Weapon));
        Check(startRevolver != null && startRevolver.id == "m1873" && startRevolver.magazineSize == 6, "player starts with six-round Colt SAA M1873 (table)");
        // 상점·무기 슬롯·파츠 모델 검사는 에디터를 종료하지 않는 PocShopVerification에 있다.
        failures += PocShopVerification.RunModelChecks();

        Debug.Log($"{TAG} unit checks finished: failures={failures}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    // 켜진 총 모델 이름. 정확히 하나만 켜져 있을 때만 그 이름을 주고, 아니면 빈 문자열.
    private static string ActiveGunModel(Transform _mount)
    {
        if (_mount == null)
        {
            return "";
        }
        string active = "";
        int count = 0;
        foreach (Transform model in _mount)
        {
            if (model.gameObject.activeSelf)
            {
                active = model.name;
                count++;
            }
        }
        return count == 1 ? active : "";
    }

    // 릴 독립 추첨 기준 기대값: 3줄 p^3 x 배수, 2줄(정확히 두 개 일치) 3p^2(1-p) x 2줄 배수.
    private static double AnalyticRtp(SlotMachineData _table)
    {
        int total = SlotMachineInfo.TotalWeight(_table, 1);
        double rtp = 0d;
        foreach (var symbol in _table.symbols)
        {
            double p = symbol.weight / (double)total;
            rtp += p * p * p * symbol.multiplier;
            rtp += 3d * p * p * (1d - p) * symbol.pairMultiplier;
        }
        return rtp;
    }

    private static double SimulateRtp(SlotMachineData _table, int _spins)
    {
        var random = new System.Random(20261029);
        int total = SlotMachineInfo.TotalWeight(_table, 1);
        double paid = 0d;
        for (int spin = 0; spin < _spins; spin++)
        {
            int[] faces = new int[3];
            for (int reel = 0; reel < 3; reel++)
            {
                int face = SlotMachineInfo.PickFace(_table, random.Next(total), 1);
                faces[reel] = face;
            }
            var outcome = SlotMachineInfo.Evaluate(_table, faces);
            paid += outcome.multiplier;
        }
        return paid / _spins;
    }

    // ---------- Feedback wiring (edit mode, no play) ----------
    // HUD 피드백 연결을 본다: HUD 프리팹·Game 씬의 직렬화 참조, 이벤트 선언, 빈 탄창·재장전 문구.
    // 확정 적중(실제 적 피해·처치 플래그)은 플레이 중에만 일어나므로 여기서 보지 않는다(플레이 검증 별도 기록).
    private static readonly string[] HUD_REFS = { "healthText", "healthFill", "ammoText", "hitMarkerGroup", "hitMarkerText", "damageFlash", "bannerGroup", "resultGroup" };
    private static readonly string[] PRESENTER_REFS = { "view", "gameManager", "playerHealth", "weapon" };

    public static void RunFeedbackChecks()
    {
        failures = 0;
        string hudPath = PocAssetBuilder.UI_PREFAB_DIR + "/HUD.prefab";
        var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath);
        Check(hudPrefab != null, "HUD prefab loads");
        var prefabView = hudPrefab != null ? hudPrefab.GetComponent<HudView>() : null;
        Check(prefabView != null, "HUD prefab root has HudView");
        if (prefabView != null)
        {
            CheckSerializedRefs(new SerializedObject(prefabView), "HUD prefab", HUD_REFS);
        }

        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var hud = UnityEngine.Object.FindFirstObjectByType<HudView>();
        var presenter = UnityEngine.Object.FindFirstObjectByType<HudPresenter>();
        Check(hud != null && presenter != null, "game scene has HudView and HudPresenter");
        if (presenter != null)
        {
            CheckSerializedRefs(new SerializedObject(presenter), "game scene HudPresenter", PRESENTER_REFS);
        }
        if (hud != null)
        {
            CheckSerializedRefs(new SerializedObject(hud), "game scene HudView", HUD_REFS);
            var ammoLabel = new SerializedObject(hud).FindProperty("ammoText").objectReferenceValue as TMPro.TMP_Text;
            hud.SetAmmo(0, 6, false, 0f);
            Check(ammoLabel != null && ammoLabel.text == "<size=60%>빈 탄창 · R 재장전</size>\n0 / 6", "empty magazine shows count and reload prompt above");
            hud.SetAmmo(3, 6, false, 0f);
            Check(ammoLabel != null && ammoLabel.text == "<size=60%> </size>\n3 / 6", "loaded magazine reserves status row above count");
            hud.SetAmmo(0, 6, true, 0.5f);
            Check(ammoLabel != null && ammoLabel.text == "<size=60%>재장전 50%</size>\n0 / 6", "reload progress is above count");
            hud.SetAmmo(0, 0, false, 0f);
            Check(ammoLabel != null && ammoLabel.text == "<size=60%>예비탄 없음</size>\n0 / 0", "no reserve does not offer reload");
        }
        Check(typeof(PlayerWeapon).GetEvent("EnemyHit") != null, "PlayerWeapon exposes EnemyHit confirmed-hit event");
        Check(typeof(PlayerWeapon).GetProperty("ReloadProgress") != null, "PlayerWeapon exposes ReloadProgress");
        Check(ShopInfo.CanHeal(20f, 100f) && !ShopInfo.CanHeal(100f, 100f) && !ShopInfo.CanHeal(0f, 0f), "heal item buyable only below max health");

        Debug.Log($"{TAG} feedback finished: failures={failures}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    private static void CheckSerializedRefs(SerializedObject _so, string _owner, string[] _fields)
    {
        foreach (string field in _fields)
        {
            SerializedProperty property = _so.FindProperty(field);
            Check(property != null && property.objectReferenceValue != null, $"{_owner} serialized {field} wired");
        }
    }

    // ---------- Play mode smoke ----------

    public static void RunPlayModeSmoke()
    {
        failures = 0;
        SessionState.SetString(MODE_KEY, "smoke");
        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        RegisterTick(SmokeTick);
        EditorApplication.isPlaying = true;
    }

    // Preload → Title → Game 전환을 실제 시작 버튼 클릭으로 확인한다.
    public static void RunFlowSmoke()
    {
        failures = 0;
        SessionState.SetString(MODE_KEY, "flow");
        EditorSceneManager.OpenScene(PRELOAD_SCENE, OpenSceneMode.Single);
        RegisterTick(FlowTick);
        EditorApplication.isPlaying = true;
    }

    // 플레이 모드 진입 시 도메인 리로드로 콜백이 사라지므로, 리로드 뒤에도 SessionState 플래그를 보고 다시 등록한다.
    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        string mode = SessionState.GetString(MODE_KEY, "");
        if (mode == "smoke")
        {
            RegisterTick(SmokeTick);
        }
        else if (mode == "flow")
        {
            RegisterTick(FlowTick);
        }
        else if (mode == "shots")
        {
            RegisterTick(ShotTick);
        }
        else if (mode == "input")
        {
            RegisterTick(InputTick);
        }
    }

    private static void RegisterTick(EditorApplication.CallbackFunction _tick)
    {
        EditorApplication.update -= _tick;
        EditorApplication.update += _tick;
    }

    private static void SmokeTick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }
        if (playStart < 0d)
        {
            playStart = EditorApplication.timeSinceStartup;
            stepStart = playStart;
            return;
        }
        if (gm == null)
        {
            gm = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            slot = UnityEngine.Object.FindFirstObjectByType<SlotMachinePresenter>();
            waves = UnityEngine.Object.FindFirstObjectByType<WaveController>();
            player = UnityEngine.Object.FindFirstObjectByType<PlayerHealth>();
            weapon = UnityEngine.Object.FindFirstObjectByType<PlayerWeapon>();
            if (gm == null || slot == null || waves == null || player == null || weapon == null)
            {
                Check(false, "scene objects present in Game scene");
                FinishSmoke();
                return;
            }
            gm.GameOver += (_w, _k) => gameOverSeen = true;
            gm.EnemiesCleared += _s => clearedSeen = true;
            slot.SpinResolved += (_o, _p, _t) =>
            {
                resolvedSpins++;
                lastPayout = _p;
                Debug.Log($"{TAG} spin resolved: payout={_p} text='{_t}'");
            };
        }

        double now = EditorApplication.timeSinceStartup;
        double elapsed = now - stepStart;
        player.Heal(10000f);

        switch (smokeStep)
        {
            case 0:
                if (elapsed < 1.5d)
                {
                    return;
                }
                Check(gm.Phase == GameManager.GamePhase.Wave && gm.WaveNumber == 1, "wave 1 starts automatically");
                Check(waves.AliveCount > 0 || !waves.AllSpawned, "enemies spawn during wave 1");
                Check(weapon.MagazineSize == 6 && weapon.Ammo == 6, $"player starts with six-round revolver in scene (ammo {weapon.Ammo}/{weapon.MagazineSize})");
                Check(weapon.AmmoId == "colt45" && weapon.ReserveAmmo == 36,
                    "starting revolver uses .45 Colt reserve separately from its loaded magazine");
                foreach (var ammoEntry in TableManager.Instance.Ammo.ammoTypes)
                {
                    Check(gm.Ammo.GetReserve(ammoEntry.id) == ammoEntry.startingReserve &&
                          gm.Ammo.GetMaximum(ammoEntry.id) == ammoEntry.maxReserve,
                        $"new Game scene initializes {ammoEntry.id} reserve and capacity from AmmoData");
                }
                int rusherGuns = 0;
                int riflemanWrongGun = 0;
                foreach (var enemy in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                {
                    if (enemy.KindId == EnemyInfo.RUSHER_ID && enemy.WeaponId != "")
                    {
                        rusherGuns++;
                    }
                    if (enemy.KindId == EnemyInfo.RIFLEMAN_ID && enemy.WeaponId != "musket")
                    {
                        riflemanWrongGun++;
                    }
                }
                Check(rusherGuns == 0, "wave 1 rushers carry no guns");
                Check(riflemanWrongGun == 0, "wave 1 riflemen spawn with muskets");
                spinStartBalance = gm.Wallet.Balance;
                Check(slot.CanInteract(), "slot is usable during wave 1");
                slot.Interact();
                Check(slot.IsSpinning, "spin started");
                Check(gm.Wallet.Balance == spinStartBalance - slot.CurrentCost, "spin cost deducted once");
                Check(InputGate.PlayerEnabled, "player input stays enabled during spin (no input lock)");
                int ammoBefore = weapon.Ammo;
                TryFireOnce();
                Check(weapon.Ammo == ammoBefore - 1, $"firing still works while slot spins (ammo {ammoBefore} -> {weapon.Ammo})");
                Next();
                return;

            case 1:
                if (elapsed < 4d)
                {
                    return;
                }
                if (!waves.AllSpawned)
                {
                    return;
                }
                Check(!slot.IsSpinning && resolvedSpins == 1, "spin resolved after reel animation");
                KillAllEnemies();
                Next();
                return;

            case 2:
                if (elapsed < 0.5d)
                {
                    return;
                }
                Check(waves.AliveCount == 0 && clearedSeen, "enemy wipe raises EnemiesCleared");
                Check(gm.KillsThisWave > 0 && gm.Wallet.Balance > 0, "kills credit rewards to wallet");
                Check(gm.Phase == GameManager.GamePhase.Wave && slot.CanInteract() == (gm.Wallet.Balance >= slot.CurrentCost), "slot still usable with time left after wipe");
                Next();
                return;

            case 3:
                if (gm.Phase != GameManager.GamePhase.Shop && elapsed < 75d)
                {
                    return;
                }
                Check(gm.Phase == GameManager.GamePhase.Shop, "wave timer runs out and opens shop");
                Check(!slot.CanInteract(), "slot locked during shop");
                Check(!waves.IsRunning, "leftover enemies cleared at wave end");
                Next();
                return;

            case 4:
                gm.Wallet.Add(500);
                int before = gm.Wallet.Balance;
                string failure = gm.TryBuyWeapon("win1886", -1);
                Check(failure == "", $"shop purchase succeeds with funds (message='{failure}')");
                Check(gm.Loadout.Get(1)?.WeaponId == "win1886" && gm.Loadout.ActiveWeaponId == "m1873", "purchase fills empty slot 2 and keeps current weapon");
                Check(gm.Wallet.Balance == before - ShopInfo.WeaponPrice(TableManager.Instance.Shop, "win1886"), "win1886 costs table price");
                string locked = gm.TryBuyWeapon("mp18", 0);
                Check(locked != "", $"WW1 weapon is locked in 1887 era (message='{locked}')");
                Next();
                return;

            case 5:
                if (elapsed < 31d)
                {
                    return;
                }
                Check(gm.Phase == GameManager.GamePhase.Wave && gm.WaveNumber == 2, "intermission auto-starts wave 2");
                Next();
                return;

            case 6:
                player.ApplyDamage(100000f);
                Check(gameOverSeen && gm.Phase == GameManager.GamePhase.GameOver, "player death triggers game over");
                Next();
                return;

            case 7:
                FinishSmoke();
                return;
        }
    }

    private static int flowStep;

    private static void FlowTick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        if (playStart < 0d)
        {
            playStart = now;
            stepStart = now;
            return;
        }
        if (now - stepStart < 2d)
        {
            return;
        }

        if (flowStep == 0)
        {
            Check(SceneManager.GetActiveScene().name == SceneNames.TITLE, "Preload hands off to Title");
            ClickButton("StartButton");
            flowStep++;
            stepStart = now;
            return;
        }

        Check(SceneManager.GetActiveScene().name == SceneNames.GAME, "Title start button loads Game");
        Check(UnityEngine.Object.FindFirstObjectByType<GameManager>() != null, "Game scene has GameManager");
        Check(UnityEngine.Object.FindFirstObjectByType<HudView>() != null, "Game scene has HUD view");
        Debug.Log($"{TAG} flow finished: failures={failures}");
        SessionState.SetString(MODE_KEY, "");
        EditorApplication.update -= FlowTick;
        EditorApplication.isPlaying = false;
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    private static void ClickButton(string _name)
    {
        var go = GameObject.Find(_name);
        var button = go != null ? go.GetComponent<UnityEngine.UI.Button>() : null;
        Check(button != null, $"button {_name} exists");
        if (button != null)
        {
            button.onClick.Invoke();
        }
    }

    // ---------- Visual capture (편집기 창 실행 시에만) ----------

    private static int shotStep;

    public static void RunScreenshots()
    {
        failures = 0;
        shotStep = 0;
        SessionState.SetString(MODE_KEY, "shots");
        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        RegisterTick(ShotTick);
        EditorApplication.isPlaying = true;
    }

    private static void ShotTick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        if (playStart < 0d)
        {
            playStart = now;
            stepStart = now;
            return;
        }
        if (now - stepStart < 2.5d)
        {
            return;
        }

        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poc2_shots");
        System.IO.Directory.CreateDirectory(folder);
        var gameManager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        switch (shotStep)
        {
            case 0:
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "hud.png"));
                break;
            case 1:
                // 완료는 타이머 만료만 허용한다. 실제 Update가 정비 전환을 처리하게 한다.
                typeof(GameManager).GetField("waveTimeLeft", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(gameManager, 0f);
                break;
            case 2:
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "shop.png"));
                break;
            case 3:
                UnityEngine.Object.FindFirstObjectByType<PlayerHealth>().ApplyDamage(100000f);
                break;
            case 4:
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "gameover.png"));
                break;
            default:
                Debug.Log($"{TAG} screenshots written to {folder}");
                SessionState.SetString(MODE_KEY, "");
                EditorApplication.update -= ShotTick;
                EditorApplication.isPlaying = false;
                PocEditorExit.Exit(0);
                return;
        }
        shotStep++;
        stepStart = now;
    }

    // 사격 입력은 자동화할 수 없으므로 발사 함수를 직접 호출해 탄약 소모만 확인한다(입력 매핑은 에디터 수동 확인 필요).
    private static void TryFireOnce()
    {
        var method = typeof(PlayerWeapon).GetMethod("TryFire", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Invoke(weapon, null);
    }

    private static void Next()
    {
        smokeStep++;
        stepStart = EditorApplication.timeSinceStartup;
    }

    private static void KillAllEnemies()
    {
        var enemies = UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        foreach (var enemy in enemies)
        {
            enemy.TakeDamage(100000f);
        }
        Debug.Log($"{TAG} killed {enemies.Length} enemies");
    }

    private static void FinishSmoke()
    {
        Debug.Log($"{TAG} smoke finished: failures={failures}");
        SessionState.SetString(MODE_KEY, "");
        EditorApplication.update -= SmokeTick;
        EditorApplication.update -= FlowTick;
        EditorApplication.isPlaying = false;
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }
}
