using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// CLI: -batchmode -nographics -executeMethod PocWaveRecoveryVerification.RunChecks (no -quit).
// 저장된 Game을 실제 PlayerLoop에서 실행한다. 제품 씬·테이블은 저장하지 않는다.
[InitializeOnLoad]
public static class PocWaveRecoveryVerification
{
    private const string RUNNING = "PocWaveRecoveryVerification.running";
    private const string TAG = "[PocWaveRecoveryVerification]";
    private static readonly Stack<IEnumerator> STEPS = new Stack<IEnumerator>();
    private static int passes;
    private static int failures;
    private static int lastFrame = -1;
    private static int completionEvents;
    private static int spinEvents;
    private static int spinPayout;
    private static bool initialized;
    private static float deadline;
    private static GameManager game;
    private static PlayerHealth player;
    private static WaveController waves;

    static PocWaveRecoveryVerification()
    {
        EditorApplication.update += Tick;
    }

    public static void RunChecks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
    }

    public static void RunConfigChecks()
    {
        var config = TableManager.Instance.GameConfig;
        int errors = 0;
        Application.LogCallback capture = (_message, _stack, _type) =>
        {
            if (_type == LogType.Error && _message.StartsWith("waveClearHealAmount", StringComparison.Ordinal))
            {
                errors++;
            }
        };
        Application.logMessageReceived += capture;
        GameConfigInfo.Validate(config);
        Check(errors == 0, "loaded recovery/bonus settings validate");
        foreach (string field in new[] { "waveClearHealAmount", "waveClearBonusBase", "waveClearBonusPerWave" })
        {
            var invalid = JsonUtility.FromJson<GameConfigData>(JsonUtility.ToJson(config));
            typeof(GameConfigData).GetField(field).SetValue(invalid, -1);
            int before = errors;
            GameConfigInfo.Validate(invalid);
            Check(errors == before + 1, $"validation rejects negative {field} (expected diagnostic)");
        }
        Application.logMessageReceived -= capture;
        var zero = new GameConfigData { maxHealth = 100 };
        Check(GameConfigInfo.WaveClearHealAmount(zero) == 0 && GameConfigInfo.WaveClearBonus(zero, 13) == 0,
            "zero tuning disables both completion rewards");
        var extreme = new GameConfigData { waveClearBonusBase = int.MaxValue, waveClearBonusPerWave = int.MaxValue };
        Check(GameConfigInfo.WaveClearBonus(extreme, int.MaxValue) == int.MaxValue,
            "infinite-wave bonus calculation cannot wrap negative");
        Debug.Log($"{TAG} config checks finished: passes={passes}, failures={failures}, expectedValidationErrors={errors}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(RUNNING, false) || !EditorApplication.isPlaying || lastFrame == Time.frameCount)
        {
            return;
        }
        lastFrame = Time.frameCount;
        try
        {
            if (!initialized)
            {
                initialized = true;
                Time.captureDeltaTime = 1f / 60f;
                deadline = Time.realtimeSinceStartup + 150f;
                Application.logMessageReceived += OnLog;
                STEPS.Push(Scenarios());
            }
            if (Time.realtimeSinceStartup > deadline)
            {
                throw new TimeoutException("Wave recovery checks exceeded 150 seconds.");
            }
            while (STEPS.Count > 0)
            {
                IEnumerator step = STEPS.Peek();
                if (!step.MoveNext())
                {
                    STEPS.Pop();
                    continue;
                }
                if (step.Current is IEnumerator nested)
                {
                    STEPS.Push(nested);
                    continue;
                }
                return;
            }
            Finish();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogException(exception);
            Finish();
        }
    }

    private static IEnumerator Scenarios()
    {
        var table = TableManager.Instance;
        var config = table.GameConfig;
        Check(GameConfigInfo.WaveClearHealAmount(config) == 20, "TableManager loads JSON recovery 20");
        foreach (var test in new[] { (wave: 1, bonus: 100), (wave: 2, bonus: 110), (wave: 12, bonus: 210), (wave: 13, bonus: 220) })
        {
            Check(GameConfigInfo.WaveClearBonus(config, test.wave) == test.bonus, $"table exact wave {test.wave} bonus {test.bonus}");
        }
        Check(GameConfigInfo.WaveClearBonus(null, 1) == 0 && GameConfigInfo.WaveClearBonus(config, 0) == 0,
            "missing config and unstarted wave award zero");
        var custom = new GameConfigData { waveClearHealAmount = 7, waveClearBonusBase = 31, waveClearBonusPerWave = 4 };
        Check(GameConfigInfo.WaveClearHealAmount(custom) == 7 && GameConfigInfo.WaveClearBonus(custom, 13) == 79,
            "custom table values change recovery and bonus without code tuning");

        game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        player = UnityEngine.Object.FindFirstObjectByType<PlayerHealth>();
        waves = UnityEngine.Object.FindFirstObjectByType<WaveController>();
        Check(game != null && player != null && waves != null, "saved Game scene gameplay references present");
        yield return Wait(() => game.WaveNumber == 1, 3f, "real Start starts wave one");
        game.WaveEnded += _wave => completionEvents++;
        Check(game.Wallet.Balance == 50 && game.WaveBonusPaid == 0 && game.WaveHealthRestored == 0f,
            "scene initialization awards no completion money or recovery");
        Call(game, "EndWave");
        Check(game.Phase == GameManager.GamePhase.Wave && completionEvents == 0 && game.Wallet.Balance == 50,
            "manual early EndWave does not transition or pay");

        yield return Wait(() => waves.AllSpawned, 12f, "production wave spawns all enemies");
        int beforeKills = game.Wallet.Balance;
        int killReward = 0;
        int killed = 0;
        foreach (var enemy in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (!enemy.IsDead)
            {
                killReward += enemy.Reward;
                killed++;
                enemy.TakeDamage(100000f);
                enemy.TakeDamage(100000f);
            }
        }
        yield return Frames(3);
        Check(game.Wallet.Balance == beforeKills + killReward && game.KillsThisWave == killed,
            "real enemy kills pay exactly once (repeated lethal damage does not duplicate)");
        Check(waves.AliveCount == 0 && game.Phase == GameManager.GamePhase.Wave && game.WaveTimeLeft > 0f &&
            completionEvents == 0 && game.WaveBonusPaid == 0, "early wipe leaves slot time and grants no completion bonus");

        // Isolate first-shop affordability from kill rewards or slot luck.
        game.Wallet.TrySpend(game.Wallet.Balance);
        game.Wallet.Add(GameConfigInfo.StartMoney(config));
        player.Configure(100f);
        player.ApplyDamage(75f);
        Check(player.CurrentHealth == 25f, "low-health fixture starts at HP 25");
        int completionWalletEvents = 0;
        Action<int> reenterCompletion = _balance =>
        {
            completionWalletEvents++;
            Call(game, "EndWave");
        };
        game.Wallet.BalanceChanged += reenterCompletion;
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 2f, "timer Update actually transitions to Shop");
        game.Wallet.BalanceChanged -= reenterCompletion;
        Check(player.CurrentHealth == 45f && game.WaveHealthRestored == 20f, "wave one low health restores partial 20 (25 to 45)");
        Check(game.Wallet.Balance == 150 && game.WaveBonusPaid == 100 && completionEvents == 1,
            "wave one pays 100 once without kill or slot rewards");
        Check(completionWalletEvents == 1, "wallet-event reentrant completion cannot duplicate rewards");
        Check(!InputGate.PlayerEnabled && !waves.IsRunning && Projectile.ActiveCount == 0,
            "shop transition preserves input gate and combat/projectile cleanup");
        Check(WeaponBuyButton("win1886").interactable && WeaponBuyButton("colt1878").interactable,
            "both 1887 shop weapons become affordable on actual first-shop UI");
        Check(!WeaponRowShown("mp18"), "WW1 weapon is not listed in the first shop");
        for (int i = 0; i < game.SlotOffers.Count; i++)
        {
            Check(game.SlotOffers.IsSoldOut(i) || UpgradeButton(i).interactable, $"slot upgrade offer {i} affordable after auto recovery");
        }
        Check(!HasHealSupply(), "shop no longer sells health recovery");
        Check(ShopMessage().Contains("HP +20") && ShopMessage().Contains("+$100"),
            "shop MVP summary shows actual restored health and paid bonus");
        var hud = UnityEngine.Object.FindFirstObjectByType<HudView>();
        Check(TextField(hud, "bannerText").text.Contains("HP +20") && TextField(hud, "bannerText").text.Contains("+$100"),
            "HUD MVP summary shows actual completion values");
        Call(game, "EndWave");
        Call(game, "EndWave");
        Check(game.Wallet.Balance == 150 && player.CurrentHealth == 45f && completionEvents == 1,
            "repeated completion grants neither health nor money nor event");

        waves.enabled = false;
        game.StartNextWave();
        Check(game.WaveNumber == 2 && game.WaveBonusPaid == 0 && game.WaveHealthRestored == 0f,
            "next-wave transition resets completion display model without paying");
        player.Heal(10000f);
        int before = game.Wallet.Balance;
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 2f, "wave two timer reaches shop");
        Check(player.CurrentHealth == 100f && game.WaveHealthRestored == 0f && game.Wallet.Balance == before + 110,
            "wave two full health has zero overheal and exact 110 bonus");
        Check(!HasHealSupply() && ShopMessage().Contains("HP +0"),
            "full-health recovery summary is zero and shop has no heal item");

        game.StartNextWave();
        player.ApplyDamage(5f);
        before = game.Wallet.Balance;
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 2f, "near-max-health timer reaches shop");
        Check(player.CurrentHealth == 100f && game.WaveHealthRestored == 5f && game.Wallet.Balance == before + 120,
            "near-max health restores only missing 5 (95 to 100), not configured 20");
        Check(ShopMessage().Contains("HP +5") && !HasHealSupply(),
            "clipped recovery refreshes shop to full-health state with actual +5 summary");

        Call(game, "StartWave", 12);
        player.Configure(175f);
        player.ApplyDamage(7.5f);
        before = game.Wallet.Balance;
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 2f, "wave twelve timer reaches shop");
        Check(player.MaxHealth == 175f && player.CurrentHealth == 175f && game.WaveHealthRestored == 7.5f,
            "non-default max health caps recovery to actual fractional missing 7.5");
        Check(game.Wallet.Balance == before + 210 && game.WaveBonusPaid == 210 && ShopMessage().Contains("HP +7.5"),
            "wave twelve pays exact 210 and UI reports actual fractional recovery");

        Call(game, "StartWave", 13);
        var slot = UnityEngine.Object.FindFirstObjectByType<SlotMachinePresenter>();
        slot.SpinResolved += (_outcome, _paid, _text) => { spinEvents++; spinPayout = _paid; };
        int healEvents = 0;
        float lastHeal = 0f;
        slot.SpinHealed += _amount => { healEvents++; lastHeal = _amount; };
        player.ApplyDamage(20f);
        float damagedHealth = player.CurrentHealth;
        int savedBalance = game.Wallet.Balance;
        game.Wallet.TrySpend(savedBalance);
        slot.Interact();
        Check(!slot.IsSpinning && player.CurrentHealth == damagedHealth && healEvents == 0, "insufficient funds do not start a spin or heal");
        game.Wallet.Add(savedBalance);
        // Use real reels but deterministic winning symbols, so shop refresh after late payout is exercised.
        int seed = WinningSeed(slot);
        UnityEngine.Random.InitState(seed);
        int spinBalance = game.Wallet.Balance;
        int cost = slot.CurrentCost;
        Action<int> reenterSpin = _balance => slot.Interact();
        game.Wallet.BalanceChanged += reenterSpin;
        slot.Interact();
        game.Wallet.BalanceChanged -= reenterSpin;
        Check(slot.IsSpinning && game.Wallet.Balance == spinBalance - cost && InputGate.PlayerEnabled,
            "real in-flight slot starts before timer expiry without input lock");
        Check(player.CurrentHealth == damagedHealth + SlotMachineInfo.SpinHealAmount(TableManager.Instance.SlotMachine) && healEvents == 1 && lastHeal > 0f,
            "successful spin heals once immediately and emits actual recovery");
        slot.Interact();
        Check(healEvents == 1 && game.Wallet.Balance == spinBalance - cost,
            "repeat interaction during spin cannot heal or charge twice");
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 2f, "in-flight slot survives real shop transition");
        Check(game.Wallet.Balance == spinBalance - cost + 220 && game.WaveBonusPaid == 220 && slot.IsSpinning,
            "wave thirteen pays exact 220 before independent in-flight slot payout");
        // Make weapons initially unaffordable while preserving the pending real payout.
        game.Wallet.TrySpend(game.Wallet.Balance - 85);
        Check(!WeaponBuyButton("colt1878").interactable, "shop weapon initially disabled before late slot payout");
        yield return Wait(() => !slot.IsSpinning, 8f, "real reel animation completes in shop");
        Check(spinEvents == 1 && spinPayout > 0 && game.Wallet.Balance == 85 + spinPayout,
            "in-flight winning slot pays exactly once in shop (no completion duplication)");
        Check(healEvents == 1, "reel completion does not heal a second time");
        Check(spinPayout < 5 || WeaponBuyButton("colt1878").interactable, "late wallet payout refreshes shop affordability");
        Check(ShopMessage().Contains("+$220"), "slot feedback does not erase shop completion summary");

        game.StartNextWave();
        // Keep a real alive enemy at expiry to verify despawn gives no kill money.
        waves.enabled = true;
        yield return Wait(() => waves.AliveCount > 0, 3f, "next wave actually spawns enemy before expiry");
        before = game.Wallet.Balance;
        int killsBefore = game.TotalKills;
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 2f, "expiry despawns remaining enemies and opens shop");
        Check(game.Wallet.Balance == before + 230 && game.TotalKills == killsBefore && waves.AliveCount == 0,
            "forced expiry despawn pays completion only, not kill rewards");

        waves.enabled = false;
        game.StartNextWave();
        before = game.Wallet.Balance;
        int eventsBefore = completionEvents;
        player.ApplyDamage(100000f);
        Set(game, "waveTimeLeft", 0f);
        Call(game, "EndWave");
        Call(game, "EndWave");
        player.Heal(100000f);
        yield return Frames(3);
        Check(game.Phase == GameManager.GamePhase.GameOver && !player.IsAlive && player.CurrentHealth == 0f,
            "death at expiry stays GameOver and never resurrects");
        Check(game.Wallet.Balance == before && completionEvents == eventsBefore && game.WaveBonusPaid == 0,
            "death and repeated completion after GameOver pay nothing");
    }

    private static int WinningSeed(SlotMachinePresenter _slot)
    {
        for (int seed = 0; seed < 10000; seed++)
        {
            UnityEngine.Random.InitState(seed);
            int[] faces = (int[])Call(_slot, "RollFaces");
            if (SlotMachineInfo.Evaluate(TableManager.Instance.SlotMachine, faces).multiplier > 0)
            {
                return seed;
            }
        }
        throw new InvalidOperationException("Could not seed winning spin.");
    }

    // 무기를 실제 상점 화면에서 선택한 뒤, 빈 슬롯(없으면 1번) 구매 버튼을 돌려준다.
    private static Button WeaponBuyButton(string _weaponId)
    {
        var presenter = UnityEngine.Object.FindFirstObjectByType<ShopPresenter>();
        var game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        var shop = TableManager.Instance.Shop;
        var weapons = TableManager.Instance.Weapon;
        // 무기 목록 줄은 해금된 무기만 판매 순서대로 채운다.
        var visible = shop.weapons.Where(_o => ShopInfo.IsWeaponUnlocked(shop, WeaponInfo.Find(weapons, _o.weaponId), game.ShopWave)).ToArray();
        Call(presenter, "OnWeaponSelected", Array.FindIndex(visible, _o => _o.weaponId == _weaponId));
        int slot = Math.Max(0, game.Loadout.FirstEmptySlot());
        var buttons = (ShopRowView[])Get(UnityEngine.Object.FindFirstObjectByType<ShopView>(), "weaponBuyButtons");
        return buttons[slot].Button;
    }

    // 해금 전 무기는 목록 줄이 꺼져 있어야 한다. 켜진 줄의 이름으로 찾는다.
    private static bool WeaponRowShown(string _weaponId)
    {
        string name = WeaponInfo.Find(TableManager.Instance.Weapon, _weaponId).name;
        var rows = (ShopRowView[])Get(UnityEngine.Object.FindFirstObjectByType<ShopView>(), "weaponRows");
        return rows.Any(_r => _r.gameObject.activeSelf && _r.LogLabel != null && _r.LogLabel.Contains(name));
    }

    private static Button UpgradeButton(int _index)
    {
        var rows = (ShopRowView[])Get(UnityEngine.Object.FindFirstObjectByType<ShopView>(), "upgradeRows");
        return rows[_index].Button;
    }

    private static bool HasHealSupply()
    {
        var rows = (ShopRowView[])Get(UnityEngine.Object.FindFirstObjectByType<ShopView>(), "supplyRows");
        foreach (var row in rows)
        {
            if (row == null || !row.gameObject.activeSelf) continue;
            var label = (TMP_Text)Get(row, "label");
            if (label != null && (label.text.Contains("체력 회복") || label.text.Contains("응급 처치"))) return true;
        }
        return false;
    }

    private static string ShopMessage() => TextField(UnityEngine.Object.FindFirstObjectByType<ShopView>(), "messageText").text;

    private static TMP_Text TextField(object _owner, string _name) => (TMP_Text)Get(_owner, _name);

    private static object Get(object _owner, string _name) => _owner.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_owner);

    private static void Set(object _owner, string _name, object _value)
    {
        _owner.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_owner, _value);
    }

    private static object Call(object _owner, string _name, params object[] _args) =>
        _owner.GetType().GetMethod(_name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_owner, _args);

    private static IEnumerator Frames(int _count)
    {
        for (int i = 0; i < _count; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator Wait(Func<bool> _condition, float _seconds, string _label)
    {
        float end = Time.time + _seconds;
        while (!_condition() && Time.time < end)
        {
            yield return null;
        }
        Check(_condition(), _label);
    }

    private static void OnLog(string _message, string _stack, LogType _type)
    {
        if ((_type == LogType.Error && !_message.StartsWith(TAG, StringComparison.Ordinal)) || _type == LogType.Exception)
        {
            failures++;
        }
    }

    private static void Check(bool _condition, string _label)
    {
        if (_condition)
        {
            passes++;
            Debug.Log($"{TAG} PASS: {_label}");
        }
        else
        {
            failures++;
            Debug.LogError($"{TAG} FAIL: {_label}");
        }
    }

    private static void Finish()
    {
        SessionState.SetBool(RUNNING, false);
        Debug.Log($"{TAG} finished real play-mode frames={Time.frameCount}, passes={passes}, failures={failures}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }
}
