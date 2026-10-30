using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int CHECKS;

    private sealed class TestClock : ILogUtcClock
    {
        public DateTimeOffset UtcNow => new DateTimeOffset(2026, 10, 31, 1, 0, 0, TimeSpan.FromHours(9));
    }

    private sealed class Rig : IDisposable
    {
        public readonly MemoryLogStore store = new MemoryLogStore(10000);
        public readonly LogSession session;
        public readonly LogGameLifecycle life;
        public readonly LogFixedTickCollector ticks;
        public readonly LogEnemyPopulation population = new LogEnemyPopulation();

        public Rig(LogData _settings)
        {
            session = new LogSession(_settings, _id => store, new TestClock(), _initialFixedDeltaTime: 0.02);
            life = new LogGameLifecycle(session);
            ticks = new LogFixedTickCollector(life);
            life.Start("synthetic-build", null, null, "synthetic-map");
        }

        public void Begin(int _wave, params string[] _kinds)
        {
            population.Reset(_kinds);
            life.BeginWave(Plan(_wave, _kinds), null);
        }

        public JsonElement[] Rows()
        {
            session.Flush();
            return Parse(store.Lines);
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

    private static void Main(string[] _args)
    {
        string root = Path.GetFullPath(_args.Length == 0 ? "." : _args[0]);
        LogData settings = Newtonsoft.Json.JsonConvert.DeserializeObject<LogData>(File.ReadAllText(Path.Combine(root, "POC2/Assets/Resources/LogData.json")));
        ContinuousTicks(settings);
        ControlAndBoundaries(settings);
        Population();
        Disabled(settings);
        FileAndSample(settings, root);
        StaticIntegration(root);
        Console.WriteLine("PASS: " + CHECKS + " phase-3 non-Unity checks; no Unity API executed.");
    }

    private static LogWavePlan Plan(int _wave, IEnumerable<string> _kinds)
    {
        return new LogWavePlan { waveNumber = _wave, durationSeconds = 40, plannedEnemies = _kinds.ToDictionary(_kind => _kind, _kind => 1) };
    }

    private static LogPlayerTickSnapshot Player(double _x = 1)
    {
        return new LogPlayerTickSnapshot
        {
            x = _x, z = 2, yaw = 350, pitch = 345, activeHand = "both", activeSlot = 0,
            weapons = new[]
            {
                new LogHeldWeaponSnapshot { instanceId = "synthetic-right", weaponTypeId = "synthetic-weapon", hand = "right", slot = 0 },
                new LogHeldWeaponSnapshot { instanceId = "synthetic-left", weaponTypeId = "synthetic-weapon", hand = "left", slot = 1 }
            }
        };
    }

    private static JsonElement[] Parse(IEnumerable<string> _lines)
    {
        return _lines.Select(_line =>
        {
            using (JsonDocument json = JsonDocument.Parse(_line))
            {
                return json.RootElement.Clone();
            }
        }).ToArray();
    }

    private static void ContinuousTicks(LogData _settings)
    {
        var config = LogInfo.Copy(_settings);
        config.maxBufferedRecords = 3;
        using (var rig = new Rig(config))
        {
            rig.Begin(1, "synthetic-rusher", "synthetic-heavy");
            Check(!rig.ticks.Record(true, Player(), null), "no fictitious tick before first FixedUpdate");
            double elapsed = 0;
            var expectedTimes = new List<double>();
            for (int i = 0; i < 1000; i++)
            {
                double step = i < 500 ? 0.02 : 0.03;
                rig.life.AdvanceFixedUpdate(step);
                elapsed += step;
                expectedTimes.Add(elapsed);
                var enemies = rig.population.CreateSnapshot(null, null, false);
                rig.population.AddAlive(enemies, "synthetic-rusher", null);
                rig.population.AddAlive(enemies, "synthetic-rusher", null);
                Check(rig.ticks.Record(true, Player(), enemies), "stationary control tick accepted " + i);
                Check(!rig.ticks.Record(true, Player(), enemies), "duplicate same fixed tick rejected " + i);
                Check(rig.session.BufferedRecords < 3 && rig.session.BufferedBytes <= config.maxBufferedBytes, "finite buffer " + i);
            }
            JsonElement[] positions = rig.Events("player_position");
            JsonElement[] counts = rig.Events("enemy_alive_counts");
            Check(positions.Length == 1000 && counts.Length == 1000, "one of each record for every controllable tick across flushes");
            Check(Math.Abs(rig.session.ElapsedSeconds - 25) < 1e-9 && rig.session.FixedTick == 1000, "sum of actual changing steps and fixed tick count");
            Check(rig.Events("fixed_delta_time_changed").Length == 1, "actual step change reported once");
            for (int i = 0; i < 1000; i++)
            {
                JsonElement p = positions[i];
                JsonElement e = counts[i];
                Check(Math.Abs(p.GetProperty("elapsedSeconds").GetDouble() - expectedTimes[i]) < 1e-12 &&
                    p.GetProperty("elapsedSeconds").GetDouble() == e.GetProperty("elapsedSeconds").GetDouble(), "same shared time " + i);
                Check(p.GetProperty("payload").GetProperty("fixedTick").GetInt64() == i + 1 &&
                    e.GetProperty("payload").GetProperty("fixedTick").GetInt64() == i + 1 &&
                    e.GetProperty("sequence").GetInt64() == p.GetProperty("sequence").GetInt64() + 1, "paired distinct ordered records " + i);
                Check(e.GetProperty("payload").GetProperty("enemies").GetProperty("aliveCount").GetInt32() == 2 &&
                    e.GetProperty("payload").GetProperty("enemies").GetProperty("aliveByKind").GetProperty("synthetic-heavy").GetInt32() == 0,
                    "single population snapshot total and zero kind " + i);
            }
            JsonElement state = positions[0].GetProperty("payload").GetProperty("player");
            Check(state.GetProperty("x").GetDouble() == 1 && state.GetProperty("z").GetDouble() == 2 &&
                state.GetProperty("yaw").GetDouble() == 350 && state.GetProperty("pitch").GetDouble() == 345, "world x z and actual Euler yaw pitch preserved");
            Check(state.GetProperty("activeHand").GetString() == "both" && state.GetProperty("weapons").GetArrayLength() == 2 &&
                state.GetProperty("weapons")[1].GetProperty("hand").GetString() == "left", "active slot and separate held hands recorded");
            Check(positions.All(_row => _row.GetProperty("waveNumber").GetInt32() == 1 && _row.GetProperty("phase").GetString() == "wave"), "tick records carry active wave and phase");
        }
    }

    private static void ControlAndBoundaries(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            rig.life.AdvanceFixedUpdate(0.02);
            Check(!rig.ticks.Record(true, Player(), null), "active pre-wave session does not collect player ticks");
            rig.Begin(1, "old-kind");
            rig.life.AdvanceFixedUpdate(0.02);
            Check(!rig.ticks.CanRecord(false) && !rig.ticks.Record(false, null, null), "input blocked or unavailable player emits neither record");
            double before = rig.session.ElapsedSeconds;
            long tickBefore = rig.session.FixedTick;
            int countBefore = rig.Rows().Length;
            Check(rig.session.ElapsedSeconds == before && rig.session.FixedTick == tickBefore && rig.Rows().Length == countBefore,
                "long pause represented by no tick calls produces no inferred records or elapsed time");
            for (int i = 0; i < 3; i++)
            {
                rig.life.AdvanceFixedUpdate(0.02);
                Check(rig.ticks.Record(true, Player(), rig.population.CreateSnapshot(null, null, false)), "multiple ticks in one render input accepted " + i);
            }
            Check(rig.Events("player_position").Length == 3 && rig.Events("enemy_alive_counts").Length == 3, "resume has exactly actual ticks without catch-up or dedup by unchanged position");
            rig.life.AdvanceFixedUpdate(0.02);
            Check(rig.ticks.Record(true, Player(9), rig.population.CreateSnapshot(null, null, false)), "slot-spinning controllable input uses same path");
            rig.life.EnemiesCleared();
            rig.life.AdvanceFixedUpdate(0.02);
            var unknownCamera = Player(10);
            unknownCamera.yaw = null;
            unknownCamera.pitch = null;
            var zeroEnemies = rig.population.CreateSnapshot(null, null, false);
            Check(rig.ticks.Record(true, unknownCamera, zeroEnemies), "early clear keeps collecting until actual shop transition");
            unknownCamera.x = 999;
            zeroEnemies.aliveByKind["old-kind"] = 999;
            JsonElement last = rig.Events("player_position").Last().GetProperty("payload").GetProperty("player");
            Check(last.GetProperty("x").GetDouble() == 10 && last.GetProperty("yaw").ValueKind == JsonValueKind.Null, "copied player state and missing camera remain accurate");
            Check(rig.Events("enemy_alive_counts").Last().GetProperty("payload").GetProperty("enemies").GetProperty("aliveByKind").GetProperty("old-kind").GetInt32() == 0,
                "post-clear zero counts frozen before mutation");
            rig.life.CompleteWave(null, null, 0, 0);
            rig.life.SetPhase("shop");
            int oldPositions = rig.Events("player_position").Length;
            before = rig.session.ElapsedSeconds;
            rig.life.AdvanceFixedUpdate(0.04);
            Check(!rig.ticks.Record(true, Player(), null), "shop suppresses both tick records even if caller says controllable");
            Check(Math.Abs(rig.session.ElapsedSeconds - before - 0.04) < 1e-12 && rig.Events("player_position").Length == oldPositions, "shop continues common clock only");
            rig.Begin(2, "new-kind");
            rig.life.AdvanceFixedUpdate(0.04);
            Check(rig.ticks.Record(true, Player(), rig.population.CreateSnapshot(null, null, false)), "next wave resumes actual ticks");
            JsonElement next = rig.Events("enemy_alive_counts").Last();
            JsonElement kinds = next.GetProperty("payload").GetProperty("enemies").GetProperty("aliveByKind");
            Check(next.GetProperty("waveNumber").GetInt32() == 2 && kinds.TryGetProperty("new-kind", out _) && !kinds.TryGetProperty("old-kind", out _), "new wave resets kind set and living counts");
            rig.life.End("death", null, null, true);
            before = rig.session.ElapsedSeconds;
            rig.life.AdvanceFixedUpdate(0.04);
            Check(!rig.ticks.Record(true, Player(), null) && rig.session.ElapsedSeconds == before, "death and inactive session stop collection and clock");
        }
        using (var fresh = new Rig(_settings))
        {
            fresh.Begin(1, "fresh-kind");
            Check(fresh.session.FixedTick == 0, "new session resets fixed tick correlation");
            fresh.life.AdvanceFixedUpdate(0.02);
            Check(fresh.ticks.Record(true, Player(), fresh.population.CreateSnapshot(null, null, false)), "new collector does not inherit old session suppression");
        }
    }

    private static void Population()
    {
        var population = new LogEnemyPopulation();
        population.Reset(new[] { "rusher", "heavy" });
        var pending = population.CreateSnapshot(null, null, false);
        Check(pending.aliveCount == 0 && pending.aliveByKind.Count == 2 && pending.aliveByKind.Values.All(_count => _count == 0), "scheduled unspawned kinds start at zero");
        population.ObserveKind("actual-extra");
        var living = population.CreateSnapshot(null, 10, true);
        population.AddAlive(living, "rusher", true);
        population.AddAlive(living, "rusher", false);
        population.AddAlive(living, "actual-extra", true);
        Check(living.aliveCount == living.aliveByKind.Values.Sum() && living.aliveCount == 3 && living.nearbyAliveCount == 2 && living.radius == 10,
            "same snapshot derives total kind counts and death radius");
        var afterKill = population.CreateSnapshot(null, null, false);
        population.AddAlive(afterKill, "rusher", null);
        Check(afterKill.aliveCount == 1 && afterKill.aliveByKind["actual-extra"] == 0 && afterKill.nearbyAliveCount == null, "dead actual-only kind remains zero without invented radius");
        var cleared = population.CreateSnapshot(null, null, false);
        Check(cleared.aliveCount == 0 && cleared.aliveByKind.Count == 3 && cleared.aliveByKind.Values.All(_count => _count == 0), "empty source after kills or forced removal preserves current-wave zeros");
        population.Reset(new[] { "next-kind" });
        var next = population.CreateSnapshot(null, null, false);
        Check(next.aliveByKind.Count == 1 && !next.aliveByKind.ContainsKey("rusher") && !next.aliveByKind.ContainsKey("actual-extra"), "next Begin removes prior planned and actual kinds");
        population.ObserveKind(null);
        var unknown = population.CreateSnapshot(null, null, false);
        population.AddAlive(unknown, null, null);
        Check(unknown.aliveByKind["unknown"] == 1 && unknown.aliveCount == 1, "unidentified living enemy has explicit unknown kind");
    }

    private static void Disabled(LogData _settings)
    {
        var config = LogInfo.Copy(_settings);
        config.enabled = false;
        using (var rig = new Rig(config))
        {
            rig.Begin(1, "kind");
            rig.life.AdvanceFixedUpdate(0.02);
            Check(!rig.ticks.CanRecord(true) && !rig.ticks.Record(true, Player(), null) && rig.Rows().Length == 0, "disabled output avoids collection");
        }
        using (var session = new LogSession(_settings, _id => throw new IOException("store denied")))
        {
            var life = new LogGameLifecycle(session);
            life.Start(null, null, null, null);
            life.BeginWave(Plan(1, new[] { "kind" }), null);
            life.AdvanceFixedUpdate(0.02);
            var collector = new LogFixedTickCollector(life);
            Check(!collector.CanRecord(true) && !collector.Record(true, Player(), null), "failed store avoids ongoing expensive snapshot collection");
            life.End("unknown", null, null);
        }
    }

    private static void FileAndSample(LogData _settings, string _root)
    {
        string temp = Path.Combine(Path.GetTempPath(), "TrenchSlot-LogPhase3-" + Guid.NewGuid().ToString("N"));
        FileLogStore store = null;
        try
        {
            using (var session = new LogSession(_settings, _id => store = new FileLogStore(temp, _id), new TestClock(), _initialFixedDeltaTime: 0.02))
            {
                var life = new LogGameLifecycle(session);
                var collector = new LogFixedTickCollector(life);
                var population = new LogEnemyPopulation();
                life.Start("synthetic-build", null, null, "synthetic-map");
                population.Reset(new[] { "synthetic-rusher" });
                life.BeginWave(Plan(1, new[] { "synthetic-rusher" }), null);
                for (int i = 0; i < 3; i++)
                {
                    life.AdvanceFixedUpdate(0.02);
                    Check(collector.Record(true, Player(i), population.CreateSnapshot(null, null, false)), "synthetic file tick pair " + i);
                }
                life.End("unknown", null, null);
            }
            JsonElement[] rows = Parse(File.ReadAllLines(store.FilePath));
            Check(rows.Count(_row => _row.GetProperty("eventType").GetString() == "player_position") == 3 &&
                rows.Count(_row => _row.GetProperty("eventType").GetString() == "enemy_alive_counts") == 3, "real UTF-8 JSONL preserves all tick pairs after shutdown flush");
            for (int i = 0; i < rows.Length; i++)
            {
                Check(rows[i].GetProperty("sequence").GetInt64() == i + 1, "real file sequence " + i);
            }
            File.Copy(store.FilePath, Path.Combine(_root, "Docs/로그-시스템-3차.synthetic.jsonl"), true);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void StaticIntegration(string _root)
    {
        string assets = Path.Combine(_root, "POC2/Assets");
        string adapter = File.ReadAllText(Path.Combine(assets, "Scripts/Game/GameManager.Logging.cs"));
        string movement = File.ReadAllText(Path.Combine(assets, "Scripts/Player/PlayerMovement.cs"));
        string weapon = File.ReadAllText(Path.Combine(assets, "Scripts/Player/PlayerWeapon.cs"));
        string wave = File.ReadAllText(Path.Combine(assets, "Scripts/Wave/WaveController.cs"));
        string collector = File.ReadAllText(Path.Combine(assets, "Scripts/Logging/LogFixedTickCollector.cs"));
        int clock = adapter.IndexOf("telemetry.AdvanceFixedUpdate(Time.fixedDeltaTime)", StringComparison.Ordinal);
        int sample = adapter.IndexOf("ObserveLogging(CaptureLogFixedTick)", StringComparison.Ordinal);
        Check(clock >= 0 && sample > clock && Regex.Matches(adapter, @"telemetry\.AdvanceFixedUpdate\(Time\.fixedDeltaTime\)").Count == 1, "single owner advances clock before collecting");
        Check(movement.Contains("IsControlEnabled => isActiveAndEnabled") && movement.Contains("controller.enabled") &&
            movement.Contains("InputGate.PlayerEnabled && (health == null || health.IsAlive)"), "control observation follows actual enabled controller gate and health");
        string sampleBody = adapter.Substring(adapter.IndexOf("private void CaptureLogFixedTick()", StringComparison.Ordinal));
        sampleBody = sampleBody.Substring(0, sampleBody.IndexOf("private void OnApplicationQuit()", StringComparison.Ordinal));
        Check(sampleBody.Contains("phase == GamePhase.Wave && playerHealth != null && playerHealth.IsAlive") && sampleBody.Contains("movement.IsControlEnabled"), "Unity adapter checks actual phase health and movement");
        Check(sampleBody.IndexOf("CanRecord(controllable)", StringComparison.Ordinal) < sampleBody.IndexOf("playerHealth.transform.position", StringComparison.Ordinal), "inactive and disabled paths skip Unity snapshot reads");
        Check(Regex.Matches(sampleBody, @"waves\.CaptureAliveSnapshot\(null, null, null\)").Count == 1 && !sampleBody.Contains("AliveCount"), "one common living snapshot and no separate total read");
        Check(sampleBody.Contains("camera.transform.eulerAngles") && weapon.Contains("public Camera AimCamera => aimCamera"), "camera is the actual configured aiming camera without a scene search");
        Check(!sampleBody.Contains("Spin") && !sampleBody.Contains("Time.frameCount") && !sampleBody.Contains("ReadValue") && !sampleBody.Contains("FindObject"), "slot movement stationary ticks and multiple fixed updates are not filtered");
        Check(!collector.Contains("AdvanceFixedUpdate") && !collector.Contains("UnityEngine") && !collector.Contains("Timer"), "pure collector reads existing time and introduces no timer or Unity API");
        Check(wave.IndexOf("logPopulation.Reset(plannedKinds)", StringComparison.Ordinal) > wave.IndexOf("Stop();", StringComparison.Ordinal) &&
            wave.Contains("logPopulation.ObserveKind(enemy.KindId)"), "wave reset and successful spawn own kind history");
        string stop = wave.Substring(wave.IndexOf("public void Stop()", StringComparison.Ordinal));
        stop = stop.Substring(0, stop.IndexOf("private bool Spawn(", StringComparison.Ordinal));
        Check(stop.Contains("alive.Clear()") && !stop.Contains("logPopulation.Reset"), "force cleanup empties living source while preserving zero history until next wave");
        Check(!Regex.IsMatch(adapter, @"Time\.(fixedDeltaTime|timeScale)\s*="), "logging does not change Unity timing");
        string[] allMeta = Directory.GetFiles(assets, "*.meta", SearchOption.AllDirectories);
        foreach (string name in new[] { "LogFixedTickCollector", "LogEnemyPopulation" })
        {
            string meta = File.ReadAllText(Path.Combine(assets, "Scripts/Logging/" + name + ".cs.meta"));
            string guid = Regex.Match(meta, @"(?m)^guid: ([a-f0-9]{32})").Groups[1].Value;
            Check(guid.Length == 32 && allMeta.Count(_path => File.ReadAllText(_path).Contains("guid: " + guid)) == 1, "source meta globally unique: " + name);
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
