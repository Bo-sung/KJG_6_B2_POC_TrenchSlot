using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using LogKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// CLI: -batchmode -nographics -executeMethod PocTelemetryVerification.RunChecks (no -quit).
// 저장된 Game 씬을 실제 PlayerLoop로 돌려 LogKit 게임 로그를 검증한다. 씬·테이블은 저장하지 않는다.
// 확인: 로거 1개(씬 재시작 후에도), 이벤트·지표 기록, 중복 방지, 상세 발사 억제, 서버 없음 안전성, 종료 flush 후 파일 내용.
[InitializeOnLoad]
public static class PocTelemetryVerification
{
    private const string RUNNING = "PocTelemetryVerification.running";
    private const string VERIFY_FILES = "PocTelemetryVerification.verifyFiles";
    private const string STARTED_UTC = "PocTelemetryVerification.startedUtc";
    private const string PASSES = "PocTelemetryVerification.passes";
    private const string FAILURES = "PocTelemetryVerification.failures";
    private const string EXPECTED_KILLS = "PocTelemetryVerification.kills";
    private const string EXPECTED_ATTACKS = "PocTelemetryVerification.attacks";
    private const string EXPECTED_HITS = "PocTelemetryVerification.hits";
    private const string SHOP_DURATION = "PocTelemetryVerification.shopDuration";
    private const string SPIN_BALANCE_DELTA = "PocTelemetryVerification.spinDelta";
    private const string BOUGHT_STAT = "PocTelemetryVerification.stat";
    private const string EXPECTED_SHOT_LINES = "PocTelemetryVerification.shotLines";
    private const string TAG = "[PocTelemetryVerification]";
    private static readonly Stack<IEnumerator> STEPS = new Stack<IEnumerator>();
    private static int lastFrame = -1;
    private static bool initialized;
    private static float deadline;
    private static double exitDeadline;

    static PocTelemetryVerification()
    {
        EditorApplication.update += Tick;
    }

    public static void RunChecks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
        SessionState.SetBool(RUNNING, true);
        SessionState.SetBool(VERIFY_FILES, false);
        SessionState.SetInt(PASSES, 0);
        SessionState.SetInt(FAILURES, 0);
        SessionState.SetString(STARTED_UTC, DateTime.UtcNow.AddSeconds(-1).Ticks.ToString());
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (SessionState.GetBool(VERIFY_FILES, false) && !EditorApplication.isPlaying)
        {
            if (exitDeadline <= 0d)
            {
                exitDeadline = EditorApplication.timeSinceStartup + 3d;
            }
            // 종료 flush·실패 전송 스레드가 끝날 시간을 짧게 준다.
            if (EditorApplication.timeSinceStartup < exitDeadline)
            {
                return;
            }
            SessionState.SetBool(VERIFY_FILES, false);
            VerifyFiles();
            Finish();
            return;
        }
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
                throw new TimeoutException("Telemetry checks exceeded 150 seconds.");
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
            ExitPlay();
        }
        catch (Exception exception)
        {
            Fail();
            Debug.LogException(exception);
            ExitPlay();
        }
    }

    private static IEnumerator Scenarios()
    {
        var game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerHealth>();
        var waves = UnityEngine.Object.FindFirstObjectByType<WaveController>();
        var weapon = UnityEngine.Object.FindFirstObjectByType<PlayerWeapon>();
        var slot = UnityEngine.Object.FindFirstObjectByType<SlotMachinePresenter>();
        Check(game != null && player != null && waves != null && weapon != null && slot != null, "saved Game scene gameplay references present");
        yield return Wait(() => game.WaveNumber == 1, 3f, "real Start starts wave one");
        Check(LoggerCount() == 1, "LogKit auto bootstrap creates exactly one GameLogger");
        Check(GameTelemetry.IsRunActive && GameTelemetry.RunId.Length == 8, "run started with run id");
        string firstRun = GameTelemetry.RunId;
        Check(!TableManager.Instance.Telemetry.uploadEnabled, "repository default keeps upload disabled (local JSONL only)");

        // 실제 사격 경로 1회 → Attack 1, Shot 1줄.
        int attacks = 0;
        int shotLines = 0;
        Call(weapon, "TryFire");
        attacks++;
        shotLines++;
        int shotId = (int)StaticField(typeof(GameTelemetry), "lastShotId");
        Check(shotId == 1, "real TryFire records one shot id");
        // 같은 발의 여러 산탄·콜라이더 적중은 AttackHit을 한 번만 센다.
        GameTelemetry.PlayerShotHit(shotId, "test", 10f, false, false, false, false);
        GameTelemetry.PlayerShotHit(shotId, "test", 10f, true, false, false, false);
        Check((int)StaticField(typeof(GameTelemetry), "shotsHit") == 1, "two impacts of one shot count one AttackHit");

        // 상세 발사 기록 억제: 설정을 끄면 Attack만 늘고 Shot 줄은 없다.
        TableManager.Instance.Telemetry.logShotEvents = false;
        float nextShot = weapon.HandNextShotTime(0);
        yield return Wait(() => Time.time >= nextShot, 3f, "weapon fire interval elapses");
        Call(weapon, "TryFire");
        attacks++;
        TableManager.Instance.Telemetry.logShotEvents = true;
        Check((int)StaticField(typeof(GameTelemetry), "shots") == attacks, "suppressed shot still counts Attack");

        // 적 사망은 한 번만 센다(같은 적 반복 치명타·직접 재호출).
        yield return Wait(() => waves.AliveCount > 0, 12f, "wave spawns enemies");
        int killed = 0;
        foreach (var enemy in UnityEngine.Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (!enemy.IsDead)
            {
                enemy.TakeDamage(100000f);
                enemy.TakeDamage(100000f);
                GameTelemetry.EnemyKilled(enemy, game.KillsThisWave, game.TotalKills, game.Wallet.Balance);
                killed++;
            }
        }
        yield return Frames(3);
        Check(game.TotalKills >= killed && (int)StaticField(typeof(GameTelemetry), "totalKills") == game.TotalKills,
            "kills recorded once per enemy death (repeated lethal damage and direct re-report ignored)");
        int expectedKills = game.TotalKills;

        player.ApplyDamage(10f);
        Check(Mathf.Approximately((float)StaticField(typeof(GameTelemetry), "damageTaken"), 10f), "player damage recorded");

        // 서버 없는 전송 시도: 아무도 듣지 않는 localhost 포트로 주기 전송을 켜도 게임은 계속된다.
        int deadPort = FreePort();
        GameLogger.Configure(developmentServerUrl: $"http://127.0.0.1:{deadPort}/api/logs", uploadOnQuit: true, uploadIntervalSeconds: 1f, requestTimeoutSeconds: 1);
        float frameStart = Time.realtimeSinceStartup;
        yield return Frames(120);
        Check(Time.realtimeSinceStartup - frameStart < 10f && game.Phase == GameManager.GamePhase.Wave, "failed uploads do not stall gameplay frames");

        // 슬롯: 회전 시작 회복(실제 회복량) → 회전 중 웨이브 만료 → 정비 중 결과 지급(현금·현물)까지.
        game.Wallet.Add(1000);
        player.ApplyDamage(30f);
        int spinsResolved = 0;
        slot.SpinResolved += (_outcome, _paid, _text) => spinsResolved++;
        float healthBeforeSpin = player.CurrentHealth;
        slot.Interact();
        float expectedSpinHeal = Mathf.Min(SlotMachineInfo.SpinHealAmount(TableManager.Instance.SlotMachine), player.MaxHealth - healthBeforeSpin);
        Check(slot.IsSpinning && InputGate.PlayerEnabled, "slot spin starts without input lock");
        Check(Mathf.Approximately(player.CurrentHealth - healthBeforeSpin, expectedSpinHeal), $"spin start heals actual {expectedSpinHeal}");
        slot.Interact();
        Check(spinsResolved == 0, "re-entering Interact while spinning does not start a second spin");

        // 정비: 시간 만료 회복·지급 + 상점 성공/실패.
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 2f, "wave completes into shop");
        Check(Mathf.Approximately(game.IntermissionLeft, game.ShopDuration) || game.IntermissionLeft < game.ShopDuration, "shop timer starts from ShopDuration");
        SessionState.SetFloat(SHOP_DURATION, game.ShopDuration);
        Check(slot.IsSpinning, "slot spin still in flight when shop opens");
        int balanceBeforeResolve = game.Wallet.Balance;
        yield return Wait(() => !slot.IsSpinning, 10f, "in-flight slot spin resolves during shop");
        Check(spinsResolved == 1 && game.Phase == GameManager.GamePhase.Shop, "slot resolved once in shop");
        SessionState.SetInt(SPIN_BALANCE_DELTA, game.Wallet.Balance - balanceBeforeResolve);
        Check(game.TryBuyWeapon("mp18", 0) != "", "locked weapon purchase fails with reason");
        game.Wallet.Add(1000);
        var stat = TableManager.Instance.Shop.stats[0];
        Check(game.TryBuyStat(stat.id) == "", $"stat purchase {stat.id} succeeds");
        SessionState.SetString(BOUGHT_STAT, stat.id);

        // 게임 오버 → 같은 씬 재시작(중복 로거·중복 구독 없음).
        int expectedHits = (int)StaticField(typeof(GameTelemetry), "shotsHit");
        game.StartNextWave();
        waves.enabled = false;
        player.ApplyDamage(100000f);
        yield return Frames(2);
        Check(game.Phase == GameManager.GamePhase.GameOver && !GameTelemetry.IsRunActive, "game over closes run");
        SceneManager.LoadScene(SceneNames.GAME);
        yield return Frames(5);
        game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        yield return Wait(() => game != null && game.WaveNumber == 1, 3f, "reloaded Game scene starts wave one");
        Check(LoggerCount() == 1, "scene reload keeps exactly one GameLogger");
        Check(GameTelemetry.IsRunActive && GameTelemetry.RunId != firstRun, "reloaded scene starts a new run id");
        Debug.Log($"{TAG} unity console capture probe");

        SessionState.SetInt(EXPECTED_KILLS, expectedKills);
        SessionState.SetInt(EXPECTED_HITS, expectedHits);
        SessionState.SetInt(EXPECTED_ATTACKS, attacks);
        SessionState.SetInt(EXPECTED_SHOT_LINES, shotLines);
    }

    // 플레이 종료 후(OnApplicationQuit flush 이후) 파일 내용을 확인한다.
    private static void VerifyFiles()
    {
        string directory = Path.Combine(Application.persistentDataPath, "Logs");
        long startedTicks = long.Parse(SessionState.GetString(STARTED_UTC, "0"));
        var sessionFile = Directory.Exists(directory)
            ? new DirectoryInfo(directory).GetFiles("*.jsonl").Where(_f => _f.CreationTimeUtc.Ticks >= startedTicks)
                .OrderByDescending(_f => _f.CreationTimeUtc).FirstOrDefault()
            : null;
        Check(sessionFile != null, $"session JSONL created under {directory}");
        if (sessionFile == null)
        {
            return;
        }
        Debug.Log($"{TAG} session file {sessionFile.FullName} ({sessionFile.Length} bytes)");
        string[] lines = File.ReadAllLines(sessionFile.FullName);
        var entries = lines.Skip(1).Select(_l => JsonUtility.FromJson<LogEntry>(_l)).ToList();
        int CountOf(string _category, string _message) => entries.Count(_e => _e.category == _category && _e.message == _message);

        Check(JsonUtility.FromJson<LogSessionHeader>(lines[0]).sessionId.Length > 0, "first line is session header");
        Check(CountOf("Run", "RunStarted") == 2, "two RunStarted (initial + reload)");
        Check(CountOf("Run", "GameOver") == 1, "one GameOver");
        var ends = entries.Where(_e => _e.category == "Run" && _e.message == "RunEnded").Select(_e => JsonUtility.FromJson<RunEndedEvent>(_e.dataJson)).ToList();
        Check(ends.Count == 2 && ends[0].reason == GameTelemetry.END_GAME_OVER && ends[1].reason == GameTelemetry.END_QUIT,
            "each run ends once (GameOver, then Quit on play exit before flush)");
        Check(ends.Count == 2 && ends[0].runId != ends[1].runId, "run ids differ per run");
        Check(CountOf("Wave", "WaveStarted") >= 3 && CountOf("Wave", "WaveCompleted") == 1 && CountOf("Wave", "IntermissionStarted") == 1,
            "wave start/completion/intermission recorded");
        Check(CountOf("Wave", "EnemiesCleared") <= 1, "enemy wipe recorded at most once");
        Check(CountOf("Combat", "EnemyKilled") == SessionState.GetInt(EXPECTED_KILLS, -1), "EnemyKilled once per enemy");
        Check(CountOf("Combat", "Shot") == SessionState.GetInt(EXPECTED_SHOT_LINES, -1), "Shot lines respect logShotEvents");
        Check(CountOf("Combat", "Hit") >= 2, "Hit detail lines recorded per damaging impact");
        Check(CountOf("Player", "PlayerDamaged") >= 2, "player damage recorded");
        // develop의 PocSlotRewardVerification([InitializeOnLoad] delayCall)은 플레이 진입 리로드 때도 모델 검사로 OnSpinStopped를 직접 부른다.
        // 그 하네스 결과는 실제 회전(SpinStarted) 이전 줄이므로 따로 세어 보고하고, 실제 회전 결과는 SpinStarted 이후 줄만 본다.
        int spinStartIndex = entries.FindIndex(_e => _e.category == "Slot" && _e.message == "SpinStarted");
        int harnessResolved = entries.Take(Math.Max(0, spinStartIndex)).Count(_e => _e.category == "Slot" && _e.message == "SpinResolved");
        Debug.Log($"{TAG} SpinResolved lines before the real spin (other editor verification harness): {harnessResolved}");
        var realResolved = entries.Skip(spinStartIndex + 1).Where(_e => _e.category == "Slot" && _e.message == "SpinResolved").ToList();
        Check(CountOf("Slot", "SpinRequested") == 1 && CountOf("Slot", "SpinStarted") == 1 && realResolved.Count == 1,
            "slot request/start/result recorded once for the real spin");
        var spin = JsonUtility.FromJson<SlotSpinStartedEvent>(entries.First(_e => _e.message == "SpinStarted").dataJson);
        Check(spin.cost > 0 && spin.faces != null && spin.faces.Length == SlotMachineInfo.REEL_COUNT, "spin data has cost and faces");
        Check(spin.healthRestored > 0f && spin.health > 0f, "spin start records actual heal and health");
        var resolved = JsonUtility.FromJson<SlotSpinResolvedEvent>(realResolved.First().dataJson);
        Check(resolved.payout + resolved.rewardCash == SessionState.GetInt(SPIN_BALANCE_DELTA, -1),
            $"spin result cash (payout {resolved.payout} + reward {resolved.rewardCash}) matches real wallet delta");
        Check(resolved.rewardPartIds != null && (resolved.rewardAmmoAmount >= 0) && resolved.wave == spin.wave,
            $"spin result records goods (ammo {resolved.rewardAmmoId} x{resolved.rewardAmmoAmount}, parts {resolved.rewardPartIds?.Length}) with spin wave");
        var intermission = JsonUtility.FromJson<IntermissionStartedEvent>(entries.First(_e => _e.message == "IntermissionStarted").dataJson);
        Check(Mathf.Approximately(intermission.intermissionSeconds, SessionState.GetFloat(SHOP_DURATION, -1f)), "IntermissionStarted uses ShopDuration");
        var failed = entries.Where(_e => _e.message == "PurchaseFailed").Select(_e => JsonUtility.FromJson<ShopPurchaseEvent>(_e.dataJson)).ToList();
        Check(failed.Any(_p => _p.itemId == "mp18" && _p.reason.Length > 0 && _p.price == 0), "failed purchase keeps reason and zero price");
        var bought = entries.Where(_e => _e.message == "PurchaseSucceeded").Select(_e => JsonUtility.FromJson<ShopPurchaseEvent>(_e.dataJson)).ToList();
        Check(bought.Any(_p => _p.itemType == "Stat" && _p.itemId == SessionState.GetString(BOUGHT_STAT, "") && _p.price > 0 && _p.balance == _p.balanceBefore - _p.price),
            "successful purchase records price and balance");
        Check(entries.All(_e => _e.dataJson.IndexOf("\"itemType\":\"Heal\"", StringComparison.Ordinal) < 0), "no removed heal-shop purchase events");
        Check(entries.Any(_e => _e.category == "Unity" && _e.message.Contains("unity console capture probe")), "Unity Debug.Log auto capture");
        Check(entries.All(_e => _e.level != "Exception" || IsEditorSearchNoise(_e.stackTrace)), "no gameplay exceptions captured during play");

        string metricsPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(sessionFile.Name) + ".metrics.json");
        Check(LogFileWriter.TryReadMetrics(metricsPath, out MetricSample[] metrics), "metrics file flushed on exit");
        long Metric(string _key) => metrics.Where(_m => _m.key == _key).Select(_m => _m.count).FirstOrDefault();
        Check(Metric(GameTelemetry.METRIC_ATTACK) == SessionState.GetInt(EXPECTED_ATTACKS, -1), "Attack metric counts trigger pulls");
        int expectedHits = SessionState.GetInt(EXPECTED_HITS, -1);
        Check(expectedHits >= 1 && expectedHits <= SessionState.GetInt(EXPECTED_ATTACKS, -1) && Metric(GameTelemetry.METRIC_ATTACK_HIT) == expectedHits,
            "AttackHit metric deduplicated per shot (never above Attack)");
        Check(Metric(GameTelemetry.METRIC_KILL) == SessionState.GetInt(EXPECTED_KILLS, -1), "Kill metric deduplicated per enemy");
        Check(Metric(GameTelemetry.METRIC_RUN_START) == 2 && Metric(GameTelemetry.METRIC_GAME_OVER) == 1, "run metrics");
        Check(Metric(GameTelemetry.METRIC_PLAYER_DAMAGE) >= 2 && Metric(GameTelemetry.METRIC_SLOT_SPIN) == 1, "player damage and slot metrics");
        Check(metrics.Any(_m => _m.key == GameTelemetry.METRIC_SLOT_HEAL && _m.count == 1 && _m.sum > 0d), "SlotHeal metric measures actual spin heal");
        Check(File.Exists(sessionFile.FullName), "failed upload to absent server keeps local JSONL for retry");
    }

    private static int LoggerCount() => UnityEngine.Object.FindObjectsByType<GameLogger>(FindObjectsSortMode.None).Length;

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static object StaticField(Type _type, string _name) => _type.GetField(_name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

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

    // 배치 모드 기동 때 에디터 검색 인덱서(UnityEditor.Search)가 던지는 예외. 게임·LogKit 코드와 무관해 제외한다.
    private static bool IsEditorSearchNoise(string _stack) => _stack != null && _stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup");

    private static void OnLog(string _message, string _stack, LogType _type)
    {
        if (_type == LogType.Exception && IsEditorSearchNoise(_stack))
        {
            return;
        }
        if ((_type == LogType.Error && !_message.StartsWith(TAG, StringComparison.Ordinal)) || _type == LogType.Exception)
        {
            Fail();
        }
    }

    private static void Check(bool _condition, string _label)
    {
        if (_condition)
        {
            SessionState.SetInt(PASSES, SessionState.GetInt(PASSES, 0) + 1);
            Debug.Log($"{TAG} PASS: {_label}");
        }
        else
        {
            Fail();
            Debug.LogError($"{TAG} FAIL: {_label}");
        }
    }

    private static void Fail()
    {
        SessionState.SetInt(FAILURES, SessionState.GetInt(FAILURES, 0) + 1);
    }

    private static void ExitPlay()
    {
        Application.logMessageReceived -= OnLog;
        SessionState.SetBool(RUNNING, false);
        SessionState.SetBool(VERIFY_FILES, true);
        EditorApplication.isPlaying = false;
    }

    private static void Finish()
    {
        int passes = SessionState.GetInt(PASSES, 0);
        int failures = SessionState.GetInt(FAILURES, 0);
        Debug.Log($"{TAG} finished passes={passes}, failures={failures}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }
}
