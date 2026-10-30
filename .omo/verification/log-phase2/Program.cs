using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int CHECKS;

    private sealed class TestClock : ILogUtcClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.FromHours(9));
    }

    private sealed class Rig : IDisposable
    {
        public readonly MemoryLogStore store = new MemoryLogStore(500);
        public readonly TestClock clock = new TestClock();
        public readonly LogSession session;
        public readonly LogGameLifecycle life;

        public Rig(LogData _settings, bool _start = true)
        {
            session = new LogSession(_settings, _id => store, clock, _initialFixedDeltaTime: 0.02);
            life = new LogGameLifecycle(session);
            if (_start)
            {
                life.Start("test-build", null, "synthetic-fingerprint", "synthetic-map");
            }
        }

        public JsonElement[] Rows()
        {
            session.Flush();
            return store.Lines.Select(_line =>
            {
                using (var document = JsonDocument.Parse(_line))
                {
                    return document.RootElement.Clone();
                }
            }).ToArray();
        }

        public JsonElement[] Events(string _type)
        {
            return Rows().Where(_row => _row.GetProperty("eventType").GetString() == _type).ToArray();
        }

        public void Dispose()
        {
            life.End("unknown", null, null);
            session.Dispose();
        }
    }

    private sealed class Collector : ILogWaveSummaryCollector
    {
        public string Key { get; set; } = "combat";
        public int begins;
        public int closes;
        public int samples;
        public bool failBegin;
        public bool failClose;
        public object result;
        public Action onClose;

        public void BeginWave(LogWaveBoundary _boundary)
        {
            begins++;
            samples = 0;
            if (failBegin)
            {
                throw new InvalidOperationException("collector begin failed");
            }
        }

        public object CloseWave(LogWaveBoundary _boundary)
        {
            closes++;
            onClose?.Invoke();
            if (failClose)
            {
                throw new InvalidOperationException("collector close failed");
            }
            return result ?? new { count = samples, status = _boundary.Status, elapsed = _boundary.ElapsedSeconds - _boundary.StartedElapsedSeconds };
        }
    }

    private static void Main(string[] _args)
    {
        string root = Path.GetFullPath(_args.Length > 0 ? _args[0] : ".");
        LogData settings = Newtonsoft.Json.JsonConvert.DeserializeObject<LogData>(File.ReadAllText(Path.Combine(root, "POC2/Assets/Resources/LogData.json")));
        Check(LogInfo.TryValidate(settings, out _), "current table validates");
        StartAndPreWaveExit(settings);
        CompletionAndDeath(settings, root);
        ExitReasonsAndReset(settings);
        CollectorBoundaries(settings);
        LastDamageAndDisabled(settings);
        Fingerprint();
        StaticIntegration(root);
        Console.WriteLine("PASS: " + CHECKS + " phase-2 non-Unity checks; no Unity API executed.");
    }

    private static LogWavePlan Plan(int _number)
    {
        return new LogWavePlan
        {
            waveNumber = _number,
            durationSeconds = 40,
            plannedEnemies = new Dictionary<string, int> { ["synthetic-rusher"] = 2, ["synthetic-heavy"] = 1 },
            eraId = "synthetic-era",
            enemyWeaponId = "synthetic-enemy-weapon"
        };
    }

    private static LogPlayerSnapshot Player(double _health, int _money)
    {
        return new LogPlayerSnapshot
        {
            health = _health,
            money = _money,
            position = new LogPosition3 { x = 1, y = 2, z = 3 },
            activeSlot = 0,
            activeHand = "both",
            weapons = new[]
            {
                new LogWeaponSnapshot { instanceId = "synthetic-right", weaponTypeId = "synthetic-weapon", hand = "right", slot = 0, loadedAmmo = 0, active = true },
                new LogWeaponSnapshot { instanceId = "synthetic-left", weaponTypeId = "synthetic-weapon", hand = "left", slot = 1, loadedAmmo = 3, active = true }
            },
            reserves = new[] { new LogReserveSnapshot { ammoTypeId = "synthetic-ammo", reserve = 5, maximum = 100 } }
        };
    }

    private static LogEnemySnapshot Enemies()
    {
        return new LogEnemySnapshot
        {
            aliveCount = 3,
            aliveByKind = new Dictionary<string, int> { ["synthetic-rusher"] = 2, ["synthetic-heavy"] = 1 },
            nearbyAliveCount = 2,
            radius = 10
        };
    }

    private static void StartAndPreWaveExit(LogData _settings)
    {
        using (var rig = new Rig(_settings, false))
        {
            Check(rig.Rows().Length == 0, "construction alone emits no gameplay events");
            Check(!rig.life.BeginWave(Plan(1), null), "wave cannot start before session");
            rig.life.AdvanceFixedUpdate(0.02);
            Check(rig.session.ElapsedSeconds == 0, "unstarted lifecycle does not advance clock");
            Check(rig.life.Start(null, null, null, null), "start once accepted");
            Check(!rig.life.Start("duplicate", "duplicate", "duplicate", "duplicate"), "duplicate session start rejected");
            JsonElement start = rig.Events("session_start").Single();
            Check(start.GetProperty("waveNumber").ValueKind == JsonValueKind.Null, "pre-wave start uses JSON null");
            JsonElement p = start.GetProperty("payload");
            Check(p.GetProperty("buildVersion").ValueKind == JsonValueKind.Null && p.GetProperty("dataVersion").ValueKind == JsonValueKind.Null &&
                p.GetProperty("mapId").ValueKind == JsonValueKind.Null, "unknown versions and map are not fabricated");
            Check(p.GetProperty("utc").GetString() == "2026-10-30T15:00:00.0000000Z" && p.GetProperty("fixedDeltaTime").GetDouble() == 0.02,
                "start includes injected UTC and actual initial step");
            Check(rig.life.End("return_to_title", Player(20, 0), null), "exit before first wave accepted");
            Check(rig.Events("wave_summary").Length == 0, "no fictitious interrupted wave before first wave");
            JsonElement end = rig.Events("session_end").Single().GetProperty("payload");
            Check(end.GetProperty("finalReachedWave").ValueKind == JsonValueKind.Null && end.GetProperty("lastCompletedWave").ValueKind == JsonValueKind.Null &&
                end.GetProperty("totalKills").GetInt32() == 0, "pre-wave exit keeps unknown wave distinct from zero kills");
            Check(!rig.life.End("application_quit", null, null) && !rig.life.Start(null, null, null, null), "closed lifecycle cannot restart or close twice");
        }
    }

    private static void CompletionAndDeath(LogData _settings, string _root)
    {
        using (var rig = new Rig(_settings))
        {
            var plan = Plan(1);
            var player = Player(20, 100);
            Check(rig.life.BeginWave(plan, player), "first wave begins");
            plan.plannedEnemies["synthetic-rusher"] = 999;
            player.health = 999;
            Check(!rig.life.BeginWave(Plan(1), player), "duplicate wave cannot begin");
            rig.life.AdvanceFixedUpdate(0.02);
            rig.life.EnemyKilled();
            rig.life.EnemyKilled();
            rig.life.AdvanceFixedUpdate(0.02);
            Check(rig.life.EnemiesCleared() && !rig.life.EnemiesCleared(), "cleared observed once");
            rig.life.AdvanceFixedUpdate(0.02);
            Check(rig.life.IsWaveOpen && rig.Events("wave_summary").Length == 0, "early clear leaves wave open and time continues");
            Check(rig.life.CompleteWave(Player(25, 100), Enemies(), 0, 5), "explicit normal completion accepted");
            Check(!rig.life.CompleteWave(Player(25, 100), Enemies(), 0, 5), "normal summary closes once");
            JsonElement summary = rig.Events("wave_summary").Single().GetProperty("payload");
            Check(summary.GetProperty("status").GetString() == "completed" && summary.GetProperty("rewardsApplied").GetBoolean(), "normal summary records applied rewards");
            Check(summary.GetProperty("bonusPaid").GetInt32() == 0 && summary.GetProperty("healthRestored").GetDouble() == 5, "actual zero bonus and actual heal retained");
            Check(summary.GetProperty("startPlayer").GetProperty("health").GetDouble() == 20 && summary.GetProperty("endPlayer").GetProperty("health").GetDouble() == 25,
                "start frozen and post-reward end state used");
            Check(summary.GetProperty("plan").GetProperty("plannedEnemies").GetProperty("synthetic-rusher").GetInt32() == 2, "planned enemies copied before later mutation");
            Check(summary.GetProperty("kills").GetInt32() == 2 && summary.GetProperty("clearedElapsedSeconds").GetDouble() == 0.04, "kills and early-clear timestamp retained");
            Check(summary.GetProperty("revision").GetInt32() == 0 && summary.GetProperty("summaryId").GetString() == rig.session.SessionId + ":wave:1", "summary uses phase-1 key and revision");
            Check(summary.GetProperty("summaries").GetProperty("combat").GetProperty("collectionStatus").GetString() == "not_collected" &&
                summary.GetProperty("summaries").GetProperty("ammo").GetProperty("data").ValueKind == JsonValueKind.Null, "future collectors are uncollected instead of zero");
            Check(rig.Events("session_end").Length == 0, "wave completion keeps session open");
            rig.life.SetPhase("shop");
            rig.life.AdvanceFixedUpdate(0.03);
            Check(Math.Abs(rig.session.ElapsedSeconds - 0.09) < 1e-12, "shop shares continuing fixed clock");
            Check(rig.life.BeginWave(Plan(2), Player(25, 200)), "next wave starts after shop state changes");
            rig.life.EnemyKilled();
            rig.life.AdvanceFixedUpdate(0.03);
            var deathPlayer = Player(0, 200);
            var deathEnemies = Enemies();
            rig.clock.UtcNow = rig.clock.UtcNow.AddSeconds(7);
            Check(rig.life.End("ignored", deathPlayer, deathEnemies, true), "death closes session");
            deathEnemies.aliveCount = 0;
            deathPlayer.weapons[0].loadedAmmo = 999;
            JsonElement[] rows = rig.Rows();
            Check(rows.TakeLast(3).Select(_row => _row.GetProperty("eventType").GetString()).SequenceEqual(new[] { "death_snapshot", "wave_summary", "session_end" }),
                "death snapshot then interrupted summary then end exactly in order");
            JsonElement death = rig.Events("death_snapshot").Single().GetProperty("payload");
            Check(death.GetProperty("enemies").GetProperty("aliveCount").GetInt32() == 3 && death.GetProperty("enemies").GetProperty("nearbyAliveCount").GetInt32() == 2 &&
                death.GetProperty("enemies").GetProperty("radius").GetDouble() == 10, "death copies pre-cleanup living counts and applied radius");
            Check(death.GetProperty("player").GetProperty("weapons")[0].GetProperty("loadedAmmo").GetInt32() == 0 &&
                death.GetProperty("player").GetProperty("weapons")[1].GetProperty("loadedAmmo").GetInt32() == 3, "death preserves separate hands and zero ammunition");
            Check(death.GetProperty("lastDamage").GetProperty("enemyTypeId").GetString() == "unknown" &&
                death.GetProperty("lastDamage").GetProperty("actualDamage").ValueKind == JsonValueKind.Null, "unconnected last damage remains unknown");
            JsonElement interrupted = rig.Events("wave_summary")[1].GetProperty("payload");
            Check(interrupted.GetProperty("status").GetString() == "interrupted" && interrupted.GetProperty("reason").GetString() == "death" &&
                !interrupted.GetProperty("rewardsApplied").GetBoolean(), "death is interruption without reward claim");
            Check(interrupted.GetProperty("bonusPaid").ValueKind == JsonValueKind.Null && interrupted.GetProperty("healthRestored").ValueKind == JsonValueKind.Null,
                "unpaid interrupted rewards are null rather than actual zero");
            Check(interrupted.GetProperty("kills").GetInt32() == 1 && interrupted.GetProperty("clearedElapsedSeconds").ValueKind == JsonValueKind.Null, "wave counters and clear state reset");
            JsonElement end = rig.Events("session_end").Single().GetProperty("payload");
            Check(end.GetProperty("finalReachedWave").GetInt32() == 2 && end.GetProperty("lastCompletedWave").GetInt32() == 1 && end.GetProperty("totalKills").GetInt32() == 3,
                "final reached differs from last completed and kills are not cleanup removals");
            Check(end.GetProperty("utc").GetString() == "2026-10-30T15:00:07.0000000Z" && Math.Abs(end.GetProperty("playSeconds").GetDouble() - 0.12) < 1e-12,
                "end UTC and fixed play time remain separate");
            int before = rows.Length;
            Check(!rig.life.End("application_quit", null, null) && !rig.life.End("unknown", null, null), "quit destroy duplicate calls suppressed after death");
            rig.life.AdvanceFixedUpdate(0.02);
            Check(!rig.session.TryRecord("late_slot_result", new { synthetic = true }) && rig.Rows().Length == before && !rig.session.IsActive, "closed old session rejects late slot callbacks");
            for (int i = 0; i < rows.Length; i++)
            {
                Check(rows[i].GetProperty("sequence").GetInt64() == i + 1 && rows[i].GetProperty("sessionId").GetString() == rig.session.SessionId, "single ordered session row " + i);
            }
            File.WriteAllLines(Path.Combine(_root, "Docs/로그-시스템-2차.synthetic.jsonl"), rig.store.Lines, new UTF8Encoding(false));
        }
    }

    private static void ExitReasonsAndReset(LogData _settings)
    {
        string previousSession = null;
        foreach (string reason in new[] { "return_to_title", "application_quit", "unknown", "restart" })
        {
            using (var rig = new Rig(_settings))
            {
                Check(previousSession == null || previousSession != rig.session.SessionId, "restart creates fresh session ID");
                previousSession = rig.session.SessionId;
                Check(rig.Rows()[0].GetProperty("sequence").GetInt32() == 1 && rig.session.ElapsedSeconds == 0, "fresh session sequence and clock reset");
                rig.life.BeginWave(Plan(1), Player(20, 100));
                rig.life.End(reason, Player(20, 100), Enemies());
                Check(rig.Events("wave_summary").Length == 1 && rig.Events("session_end").Length == 1 && rig.Events("death_snapshot").Length == 0, "non-death interruption closes once");
                Check(rig.Events("session_end").Single().GetProperty("payload").GetProperty("reason").GetString() == reason, "observed exit reason preserved");
                Check(rig.Events("wave_summary").Single().GetProperty("payload").GetProperty("bonusPaid").ValueKind == JsonValueKind.Null, "exit is not rewarded completion");
            }
        }
        using (var rig = new Rig(_settings))
        {
            rig.life.BeginWave(Plan(1), Player(20, 100));
            rig.life.CompleteWave(Player(20, 100), Enemies(), 0, 0);
            rig.life.SetPhase("shop");
            rig.life.End(null, Player(20, 100), Enemies());
            Check(rig.Events("wave_summary").Length == 1, "shop exit does not reopen closed wave");
            Check(rig.Events("session_end").Single().GetProperty("payload").GetProperty("reason").GetString() == "unknown", "missing exit reason becomes unknown");
        }
    }

    private static void CollectorBoundaries(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            var collector = new Collector();
            Check(rig.life.RegisterCollector(collector) && !rig.life.RegisterCollector(new Collector()), "collector key has one owner");
            rig.life.BeginWave(Plan(1), null);
            Check(!rig.life.RegisterCollector(new Collector { Key = "late" }), "mid-wave registration rejected");
            collector.samples = 7;
            bool reentrantEnded = true;
            collector.onClose = () => reentrantEnded = rig.life.End("unknown", null, null);
            rig.life.CompleteWave(null, null, 0, 0);
            Check(!reentrantEnded && rig.life.IsActive, "collector cannot reenter close and reorder session end");
            Check(collector.begins == 1 && collector.closes == 1, "collector begins and closes once");
            JsonElement combat = rig.Events("wave_summary").Single().GetProperty("payload").GetProperty("summaries").GetProperty("combat");
            Check(combat.GetProperty("collectionStatus").GetString() == "collected" && combat.GetProperty("data").GetProperty("count").GetInt32() == 7, "registered summary attached");
            rig.life.BeginWave(Plan(2), null);
            rig.life.End("death", null, null, true);
            Check(collector.begins == 2 && collector.closes == 2 && collector.samples == 0, "next-wave collector reset and interruption close");
        }
        foreach (string fault in new[] { "begin", "close", "serialize" })
        {
            using (var rig = new Rig(_settings))
            {
                var collector = new Collector { failBegin = fault == "begin", failClose = fault == "close" };
                if (fault == "serialize")
                {
                    collector.result = new { damage = double.NaN };
                }
                rig.life.RegisterCollector(collector);
                rig.life.BeginWave(Plan(1), null);
                rig.life.End("unknown", null, null);
                Check(rig.Events("session_end").Length == 1 && rig.Events("wave_summary").Length == 1, "collector " + fault + " failure preserves lifecycle records");
                JsonElement combat = rig.Events("wave_summary").Single().GetProperty("payload").GetProperty("summaries").GetProperty("combat");
                Check(combat.GetProperty("collectionStatus").GetString() == "failed" && combat.GetProperty("data").ValueKind == JsonValueKind.Null,
                    "collector " + fault + " failure is explicit");
                Check(rig.session.Diagnostics.FailureCount == 1, "collector " + fault + " diagnostic retained");
            }
        }
    }

    private static void LastDamageAndDisabled(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            var damage = new LogDamageSnapshot { enemyTypeId = "synthetic-enemy", enemyWeaponId = "synthetic-gun", actualDamage = 4, attackPosition = new LogPosition3 { x = 1, y = 2, z = 3 } };
            rig.life.SetLastDamage(damage);
            damage.enemyTypeId = "mutated";
            damage.attackPosition.x = 999;
            rig.life.End("death", null, null, true);
            JsonElement last = rig.Events("death_snapshot").Single().GetProperty("payload").GetProperty("lastDamage");
            Check(last.GetProperty("enemyTypeId").GetString() == "synthetic-enemy" && last.GetProperty("attackPosition").GetProperty("x").GetDouble() == 1,
                "future damage contract freezes source before enemy destruction");
        }
        var disabled = LogInfo.Copy(_settings);
        disabled.enabled = false;
        using (var rig = new Rig(disabled))
        {
            rig.life.BeginWave(Plan(1), null);
            rig.life.EnemiesCleared();
            rig.life.End("death", null, null, true);
            Check(rig.Rows().Length == 0 && !rig.life.IsActive && !rig.session.IsActive, "disabled storage keeps lifecycle cleanup working without records");
        }
        using (var session = new LogSession(_settings, _id => throw new IOException("disk unavailable"), _initialFixedDeltaTime: 0.02))
        {
            var life = new LogGameLifecycle(session);
            life.Start(null, null, null, null);
            life.BeginWave(Plan(1), null);
            Check(life.End("death", null, null, true) && session.Diagnostics.StorageFailed && !session.IsActive, "storage failure does not block lifecycle completion");
        }
    }

    private static void Fingerprint()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        string a = LogGameLifecycle.ComputeDataFingerprint(new { number = 1.25, text = "테이블" });
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            string b = LogGameLifecycle.ComputeDataFingerprint(new { number = 1.25, text = "테이블" });
            string c = LogGameLifecycle.ComputeDataFingerprint(new { number = 1.26, text = "테이블" });
            Check(a == b && a != c && a.StartsWith("sha256:", StringComparison.Ordinal) && a.Length == 71, "loaded-data SHA256 fingerprint is stable across culture and changes with values");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private static void StaticIntegration(string _root)
    {
        string game = File.ReadAllText(Path.Combine(_root, "POC2/Assets/Scripts/Game/GameManager.cs"));
        string adapter = File.ReadAllText(Path.Combine(_root, "POC2/Assets/Scripts/Game/GameManager.Logging.cs"));
        string wave = File.ReadAllText(Path.Combine(_root, "POC2/Assets/Scripts/Wave/WaveController.cs"));
        string presenter = File.ReadAllText(Path.Combine(_root, "POC2/Assets/Scripts/UI/GameOverPresenter.cs"));
        string death = game.Substring(game.IndexOf("private void OnPlayerDied()", StringComparison.Ordinal));
        Ordered(death, "EndLogging(\"death\", true)", "waves.Stop()", "ClearGroundWeapons()", "SetPhase(GamePhase.GameOver)", "GameOver?.Invoke");
        string completion = game.Substring(game.IndexOf("private void EndWave()", StringComparison.Ordinal));
        Ordered(completion, "CaptureLogEnemies()", "waves.Stop()", "playerHealth.Heal(", "wallet.Add(WaveBonusPaid)", "telemetry.CompleteWave(", "WaveEnded?.Invoke", "SetPhase(GamePhase.Shop)");
        string destroy = game.Substring(game.IndexOf("private void OnDestroy()", StringComparison.Ordinal));
        Ordered(destroy, "EndLogging(\"unknown\")", "ClearAmmoDrops()");
        Ordered(game.Substring(game.IndexOf("private void Start()", StringComparison.Ordinal)), "InitializeLogging()", "StartCoroutine(");
        Ordered(presenter.Substring(presenter.IndexOf("private void OnTitleClicked()", StringComparison.Ordinal)), "PrepareForTitleReturn()", "SceneManager.LoadScene");
        Check(adapter.Contains("!Application.isPlaying") && adapter.Contains("gameObject.scene.name != SceneNames.GAME"), "session creation gated to playing Game scene");
        Check(!adapter.Contains("OnDisable") && adapter.Contains("EndLogging(\"application_quit\")"), "disable does not invent voluntary quit");
        Check(Regex.Matches(adapter, @"telemetry\.AdvanceFixedUpdate\(Time\.fixedDeltaTime\)").Count == 1 &&
            !Regex.IsMatch(adapter, @"Time\.(fixedDeltaTime|timeScale)\s*="), "single fixed clock call without changing Unity timing");
        Check(adapter.Contains("TableManager.Instance.Ammo") && adapter.Contains("ammo.GetMaximum(entry.id)") && adapter.Contains("weapon.Hand(1)"), "snapshot observes current table capacities and both hands");
        Check(wave.Contains("enemy == null || enemy.IsDead") && wave.Contains("Vector3.Distance(enemy.transform.position, _center.Value)"), "living snapshot excludes dead and measures actual world distance");
        string query = wave.Substring(wave.IndexOf("public LogEnemySnapshot CaptureAliveSnapshot", StringComparison.Ordinal));
        query = query.Substring(0, query.IndexOf("public void Begin(", StringComparison.Ordinal));
        Check(!query.Contains("FindObjects") && query.Contains("foreach (var enemy in alive)"), "snapshot uses existing living list without global search");
        string[] newFiles =
        {
            "Scripts/Logging/LogGameLifecycle.cs", "Scripts/Logging/LogLifecyclePayloads.cs", "Scripts/Game/GameManager.Logging.cs"
        };
        var newGuids = new List<string>();
        foreach (string file in newFiles)
        {
            string meta = File.ReadAllText(Path.Combine(_root, "POC2/Assets", file + ".meta"));
            var match = Regex.Match(meta, @"(?m)^guid: ([a-f0-9]{32})");
            Check(match.Success, "new source meta " + file);
            newGuids.Add(match.Groups[1].Value);
        }
        string[] allMeta = Directory.GetFiles(Path.Combine(_root, "POC2/Assets"), "*.meta", SearchOption.AllDirectories);
        foreach (string guid in newGuids)
        {
            Check(allMeta.Count(_path => File.ReadAllText(_path).Contains("guid: " + guid)) == 1, "new GUID globally unique");
        }
    }

    private static void Ordered(string _source, params string[] _tokens)
    {
        int previous = -1;
        foreach (string token in _tokens)
        {
            int next = _source.IndexOf(token, previous + 1, StringComparison.Ordinal);
            Check(next > previous, "static lifecycle order: " + token);
            previous = next;
        }
    }

    private static void Check(bool _condition, string _message)
    {
        if (!_condition)
        {
            throw new InvalidOperationException("FAIL: " + _message);
        }
        CHECKS++;
    }
}
