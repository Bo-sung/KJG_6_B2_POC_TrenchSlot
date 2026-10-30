using System;
using System.Collections.Generic;
using LogKit;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 게임 로그 어댑터. LogKit(GameLogger) 결합은 여기에만 둔다. 게임 코드는 이 정적 메서드만 부른다.
// 이벤트: category(Run·Wave·Slot·Shop·Weapon·Combat·Player) + message(이벤트 이름) + data(TelemetryEvents 직렬화 자료).
// 지표(Count/Measure) 키는 아래 상수로 고정하며 한 사건을 한 번만 센다.
// 로깅 실패는 모두 삼켜 게임 진행을 멈추지 않는다. 매 프레임 호출 지점은 없다(이벤트 발생 시에만).
public static class GameTelemetry
{
    public const string CATEGORY_RUN = "Run";
    public const string CATEGORY_WAVE = "Wave";
    public const string CATEGORY_SLOT = "Slot";
    public const string CATEGORY_SHOP = "Shop";
    public const string CATEGORY_WEAPON = "Weapon";
    public const string CATEGORY_COMBAT = "Combat";
    public const string CATEGORY_PLAYER = "Player";

    public const string METRIC_RUN_START = "RunStart";
    public const string METRIC_GAME_OVER = "GameOver";
    public const string METRIC_GAME_OVER_WAVE = "GameOverWave";
    public const string METRIC_WAVE_START = "WaveStart";
    public const string METRIC_WAVE_CLEAR = "WaveClear";
    public const string METRIC_WAVE_COMPLETE = "WaveComplete";
    public const string METRIC_SLOT_SPIN = "SlotSpin";
    public const string METRIC_SLOT_WIN = "SlotWin";
    public const string METRIC_SLOT_PAYOUT = "SlotPayout";
    public const string METRIC_SHOP_PURCHASE = "ShopPurchase";
    public const string METRIC_SHOP_PURCHASE_FAILED = "ShopPurchaseFailed";
    public const string METRIC_SHOP_SPEND = "ShopSpend";
    public const string METRIC_WEAPON_PICKUP = "WeaponPickup";
    public const string METRIC_ATTACK = "Attack";
    public const string METRIC_ATTACK_HIT = "AttackHit";
    public const string METRIC_DAMAGE_DEALT = "DamageDealt";
    public const string METRIC_KILL = "Kill";
    public const string METRIC_PLAYER_DAMAGE = "PlayerDamage";
    public const string METRIC_PLAYER_DAMAGE_TAKEN = "PlayerDamageTaken";

    public const string END_GAME_OVER = "GameOver";
    public const string END_SCENE_UNLOADED = "SceneUnloaded";
    public const string END_QUIT = "Quit";

    private const string LEVEL_INFO = "Info";
    // 적중 중복 판정에 남겨 두는 최근 발사 수. 날아가는 탄보다 충분히 크다.
    private const int HIT_SHOT_WINDOW = 1024;

    private static TelemetryData settings;
    private static bool configured;
    private static bool runActive;
    private static string runId = "";
    private static float runStartTime;
    private static int wave;
    private static int lastBalance;
    private static int totalKills;
    private static int shots;
    private static int shotsHit;
    private static float damageTaken;
    private static int lastShotId;
    // 한 발(산탄 포함)의 첫 적중만 AttackHit으로 센다.
    private static readonly HashSet<int> HIT_SHOTS = new HashSet<int>();
    // 한 적의 사망은 한 번만 센다(웨이브마다 비움).
    private static readonly HashSet<Enemy> KILLED = new HashSet<Enemy>();
    // 고빈도 이벤트는 자료를 재사용한다. GameLogger.Data가 호출 즉시 직렬화하므로 안전하다.
    private static readonly ShotEvent SHOT_EVENT = new ShotEvent();
    private static readonly HitEvent HIT_EVENT = new HitEvent();

    public static bool IsRunActive => runActive;
    public static string RunId => runId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        settings = null;
        configured = false;
        runActive = false;
        runId = "";
        wave = 0;
        lastBalance = 0;
        ResetRunTotals();
        lastShotId = 0;
    }

    // LogKit 자동 부트스트랩(AfterSceneLoad)보다 먼저 JSON 설정을 넣는다. 로거를 직접 만들지 않으므로 중복 로거가 생기지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureLogger()
    {
        try
        {
            settings = TableManager.Instance.Telemetry;
            if (!TelemetryInfo.Enabled(settings))
            {
                GameLogger.Configure(captureUnityLogs: false, uploadOnQuit: false, retryPendingOnStartup: false, uploadIntervalSeconds: 0f);
                return;
            }
            bool upload = TelemetryInfo.UploadEnabled(settings);
            GameLogger.Configure(
                developmentServerUrl: TelemetryInfo.OrNull(settings.developmentServerUrl),
                releaseServerUrl: TelemetryInfo.OrNull(settings.releaseServerUrl),
                apiKey: TelemetryInfo.OrNull(settings.apiKey),
                captureUnityLogs: settings.captureUnityLogs,
                flushIntervalSeconds: TelemetryInfo.FlushIntervalSeconds(settings),
                uploadOnQuit: upload,
                retryPendingOnStartup: upload && settings.retryPendingOnStartup,
                requestTimeoutSeconds: TelemetryInfo.RequestTimeoutSeconds(settings),
                uploadIntervalSeconds: upload ? TelemetryInfo.UploadIntervalSeconds(settings) : 0f);
            configured = true;

            // 런 종료를 로거의 종료 플러시보다 먼저 남긴다. 다시 들어와도 한 번만 구독한다.
            Application.wantsToQuit -= OnWantsToQuit;
            Application.wantsToQuit += OnWantsToQuit;
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }
        catch (Exception)
        {
            // 설정 실패 시에도 게임은 그대로 진행한다(LogKit 기본값 또는 기록 없음).
            configured = false;
        }
    }

    private static bool OnWantsToQuit()
    {
        EndRun(END_QUIT);
        return true;
    }

#if UNITY_EDITOR
    private static void OnPlayModeStateChanged(PlayModeStateChange _state)
    {
        if (_state != PlayModeStateChange.ExitingPlayMode)
        {
            return;
        }
        EndRun(END_QUIT);
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        Application.wantsToQuit -= OnWantsToQuit;
    }
#endif

    private static bool Active => configured && TelemetryInfo.Enabled(settings);

    // ---------- 런 ----------

    public static void RunStarted(int _startMoney, float _maxHealth, string _startWeaponId)
    {
        if (!Active)
        {
            return;
        }
        if (runActive)
        {
            EndRun(END_SCENE_UNLOADED);
        }
        runActive = true;
        runId = Guid.NewGuid().ToString("N").Substring(0, 8);
        runStartTime = Time.time;
        wave = 0;
        lastBalance = _startMoney;
        ResetRunTotals();
        Count(METRIC_RUN_START);
        Write(CATEGORY_RUN, "RunStarted", new RunStartedEvent
        {
            startMoney = _startMoney,
            maxHealth = _maxHealth,
            startWeaponId = _startWeaponId ?? ""
        });
    }

    public static void GameOver(int _wave, int _totalKills, int _balance)
    {
        if (!Active || !runActive)
        {
            return;
        }
        wave = _wave;
        totalKills = _totalKills;
        lastBalance = _balance;
        Count(METRIC_GAME_OVER);
        Measure(METRIC_GAME_OVER_WAVE, _wave);
        Write(CATEGORY_RUN, "GameOver", NewRunEnded(END_GAME_OVER));
        EndRun(END_GAME_OVER);
    }

    // 진행 중인 런을 한 번만 닫는다(게임 오버·씬 이탈·종료 중 먼저 온 것).
    public static void EndRun(string _reason)
    {
        if (!Active || !runActive)
        {
            return;
        }
        Write(CATEGORY_RUN, "RunEnded", NewRunEnded(_reason));
        runActive = false;
    }

    // ---------- 웨이브 ----------

    public static void WaveStarted(WaveSpec _spec, int _balance, float _health)
    {
        if (!Active)
        {
            return;
        }
        wave = _spec.wave;
        lastBalance = _balance;
        KILLED.Clear();
        Count(METRIC_WAVE_START);
        Write(CATEGORY_WAVE, "WaveStarted", new WaveStartedEvent
        {
            durationSeconds = _spec.durationSeconds,
            rushers = _spec.rushers,
            riflemen = _spec.riflemen,
            heavies = _spec.heavies,
            era = _spec.era ?? "",
            enemyWeapon = _spec.enemyWeapon ?? "",
            hpMultiplier = _spec.hpMultiplier,
            balance = _balance,
            health = _health
        });
    }

    public static void EnemiesCleared(float _timeLeft, int _killsThisWave)
    {
        if (!Active)
        {
            return;
        }
        Count(METRIC_WAVE_CLEAR);
        Write(CATEGORY_WAVE, "EnemiesCleared", new WaveClearedEvent
        {
            timeLeft = _timeLeft,
            killsThisWave = _killsThisWave
        });
    }

    public static void WaveCompleted(float _healthRestored, int _bonusPaid, int _balance, float _health, int _killsThisWave)
    {
        if (!Active)
        {
            return;
        }
        lastBalance = _balance;
        Count(METRIC_WAVE_COMPLETE);
        Write(CATEGORY_WAVE, "WaveCompleted", new WaveCompletedEvent
        {
            healthRestored = _healthRestored,
            bonusPaid = _bonusPaid,
            balance = _balance,
            health = _health,
            killsThisWave = _killsThisWave
        });
    }

    public static void IntermissionStarted(float _seconds, int _balance)
    {
        if (!Active)
        {
            return;
        }
        Write(CATEGORY_WAVE, "IntermissionStarted", new IntermissionStartedEvent
        {
            intermissionSeconds = _seconds,
            balance = _balance
        });
    }

    // ---------- 슬롯 ----------

    public static void SlotSpinRequested(int _cost, int _balance)
    {
        WriteSlot("SpinRequested", _cost, _balance, "");
    }

    public static void SlotSpinRejected(string _reason, int _cost, int _balance)
    {
        WriteSlot("SpinRejected", _cost, _balance, _reason);
    }

    public static void SlotSpinStarted(int _cost, int _balance, float _spinSeconds, int[] _faces)
    {
        if (!Active)
        {
            return;
        }
        lastBalance = _balance;
        Count(METRIC_SLOT_SPIN);
        Write(CATEGORY_SLOT, "SpinStarted", new SlotSpinStartedEvent
        {
            cost = _cost,
            balance = _balance,
            spinSeconds = _spinSeconds,
            faces = _faces != null ? (int[])_faces.Clone() : Array.Empty<int>()
        });
    }

    public static void SlotSpinResolved(SlotOutcome _outcome, int _payout, float _piggyMultiplier, int _balance, int _magazine, int _reserve)
    {
        if (!Active)
        {
            return;
        }
        lastBalance = _balance;
        if (_payout > 0)
        {
            Count(METRIC_SLOT_WIN);
        }
        Measure(METRIC_SLOT_PAYOUT, _payout);
        Write(CATEGORY_SLOT, "SpinResolved", new SlotSpinResolvedEvent
        {
            multiplier = _outcome.multiplier,
            payout = _payout,
            ammoGranted = _outcome.hasAmmo,
            nearMiss = _outcome.isNearMiss,
            piggyMultiplier = _piggyMultiplier,
            balance = _balance,
            magazineAmmo = _magazine,
            reserveAmmo = _reserve
        });
    }

    private static void WriteSlot(string _event, int _cost, int _balance, string _reason)
    {
        if (!Active)
        {
            return;
        }
        Write(CATEGORY_SLOT, _event, new SlotSpinEvent
        {
            cost = _cost,
            balance = _balance,
            reason = _reason ?? ""
        });
    }

    // ---------- 상점·무기 ----------

    // _failure가 빈 문자열이면 성공. 가격은 실제로 줄어든 소지금이다.
    public static void ShopPurchase(string _itemType, string _itemId, string _failure, int _balanceBefore, int _balanceAfter)
    {
        if (!Active)
        {
            return;
        }
        bool success = string.IsNullOrEmpty(_failure);
        int price = Mathf.Max(0, _balanceBefore - _balanceAfter);
        lastBalance = _balanceAfter;
        if (success)
        {
            Count(METRIC_SHOP_PURCHASE);
            Measure(METRIC_SHOP_SPEND, price);
        }
        else
        {
            Count(METRIC_SHOP_PURCHASE_FAILED);
        }
        Write(CATEGORY_SHOP, success ? "PurchaseSucceeded" : "PurchaseFailed", new ShopPurchaseEvent
        {
            itemType = _itemType ?? "",
            itemId = _itemId ?? "",
            success = success,
            reason = _failure ?? "",
            price = price,
            balanceBefore = _balanceBefore,
            balance = _balanceAfter
        });
    }

    public static void WeaponDropped(string _weaponId, int _magazine, string _reason)
    {
        WriteWeapon("WeaponDropped", _weaponId, -1, "", _magazine, _reason);
    }

    public static void WeaponAcquired(string _event, string _weaponId, int _slot, string _replacedWeaponId, int _magazine)
    {
        if (Active && _event == "WeaponPickedUp")
        {
            Count(METRIC_WEAPON_PICKUP);
        }
        WriteWeapon(_event, _weaponId, _slot, _replacedWeaponId, _magazine, "");
    }

    private static void WriteWeapon(string _event, string _weaponId, int _slot, string _replacedWeaponId, int _magazine, string _reason)
    {
        if (!Active)
        {
            return;
        }
        Write(CATEGORY_WEAPON, _event, new WeaponChangeEvent
        {
            weaponId = _weaponId ?? "",
            slot = _slot,
            replacedWeaponId = _replacedWeaponId ?? "",
            magazine = _magazine,
            reason = _reason ?? ""
        });
    }

    // ---------- 전투 ----------

    // 방아쇠 1회(산탄 전체 포함)를 한 발로 센다. 적중 기록에 넘길 발사 번호를 돌려준다(기록이 꺼져 있으면 0).
    public static int PlayerShotFired(string _weaponId, int _hand, int _pellets, int _magazine, int _reserve, bool _akimbo, bool _aiming)
    {
        if (!Active)
        {
            return 0;
        }
        lastShotId++;
        shots++;
        Count(METRIC_ATTACK);
        if (TelemetryInfo.LogShotEvents(settings))
        {
            SHOT_EVENT.shotId = lastShotId;
            SHOT_EVENT.weaponId = _weaponId ?? "";
            SHOT_EVENT.hand = _hand;
            SHOT_EVENT.pellets = _pellets;
            SHOT_EVENT.magazineAmmo = _magazine;
            SHOT_EVENT.reserveAmmo = _reserve;
            SHOT_EVENT.akimbo = _akimbo;
            SHOT_EVENT.aiming = _aiming;
            Write(CATEGORY_COMBAT, "Shot", SHOT_EVENT);
        }
        return lastShotId;
    }

    // 적에게 실제 피해를 준 탄만 넘긴다. 같은 발의 다른 산탄·콜라이더 적중은 AttackHit을 다시 세지 않는다.
    public static void PlayerShotHit(int _shotId, string _weaponId, float _damage, bool _headshot, bool _helmet, bool _killed, bool _staggered)
    {
        if (!Active || _shotId <= 0)
        {
            return;
        }
        Measure(METRIC_DAMAGE_DEALT, _damage);
        if (HIT_SHOTS.Add(_shotId))
        {
            shotsHit++;
            Count(METRIC_ATTACK_HIT);
            if (HIT_SHOTS.Count > HIT_SHOT_WINDOW)
            {
                int oldest = lastShotId - HIT_SHOT_WINDOW / 2;
                HIT_SHOTS.RemoveWhere(_id => _id < oldest);
            }
        }
        if (TelemetryInfo.LogHitEvents(settings))
        {
            HIT_EVENT.shotId = _shotId;
            HIT_EVENT.weaponId = _weaponId ?? "";
            HIT_EVENT.damage = _damage;
            HIT_EVENT.headshot = _headshot;
            HIT_EVENT.helmet = _helmet;
            HIT_EVENT.killed = _killed;
            HIT_EVENT.staggered = _staggered;
            Write(CATEGORY_COMBAT, "Hit", HIT_EVENT);
        }
    }

    public static void EnemyKilled(Enemy _enemy, int _killsThisWave, int _totalKills, int _balance)
    {
        if (!Active || _enemy == null || !KILLED.Add(_enemy))
        {
            return;
        }
        totalKills = _totalKills;
        lastBalance = _balance;
        Count(METRIC_KILL);
        Write(CATEGORY_COMBAT, "EnemyKilled", new KillEvent
        {
            enemyKind = _enemy.KindId,
            enemyWeaponId = _enemy.WeaponId,
            reward = _enemy.Reward,
            killsThisWave = _killsThisWave,
            totalKills = _totalKills,
            balance = _balance
        });
    }

    public static void PlayerDamaged(float _damage, float _health, float _maxHealth)
    {
        if (!Active || _damage <= 0f)
        {
            return;
        }
        damageTaken += _damage;
        Count(METRIC_PLAYER_DAMAGE);
        Measure(METRIC_PLAYER_DAMAGE_TAKEN, _damage);
        Write(CATEGORY_PLAYER, "PlayerDamaged", new PlayerDamagedEvent
        {
            damage = _damage,
            health = _health,
            maxHealth = _maxHealth,
            lethal = _health <= 0f
        });
    }

    // ---------- 공통 ----------

    private static RunEndedEvent NewRunEnded(string _reason)
    {
        return new RunEndedEvent
        {
            reason = _reason,
            balance = lastBalance,
            totalKills = totalKills,
            shots = shots,
            shotsHit = shotsHit,
            damageTaken = damageTaken
        };
    }

    private static void ResetRunTotals()
    {
        totalKills = 0;
        shots = 0;
        shotsHit = 0;
        damageTaken = 0f;
        HIT_SHOTS.Clear();
        KILLED.Clear();
    }

    private static void Write(string _category, string _event, TelemetryEvent _data)
    {
        try
        {
            _data.runId = runId;
            _data.wave = wave;
            _data.runSeconds = runActive ? Time.time - runStartTime : 0f;
            GameLogger.Data(LEVEL_INFO, _category, _event, _data);
        }
        catch (Exception)
        {
            // 로그 실패는 게임에 영향을 주지 않는다.
        }
    }

    private static void Count(string _key)
    {
        try
        {
            GameLogger.Count(_key);
        }
        catch (Exception)
        {
        }
    }

    private static void Measure(string _key, double _value)
    {
        try
        {
            GameLogger.Measure(_key, _value);
        }
        catch (Exception)
        {
        }
    }
}
