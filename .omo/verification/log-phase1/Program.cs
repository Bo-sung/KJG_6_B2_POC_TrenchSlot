using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static int CHECKS;

    private sealed class TestUtcClock : ILogUtcClock
    {
        public DateTimeOffset UtcNow => new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.FromHours(9));
    }

    private sealed class FaultStore : ILogStore
    {
        public string stage;
        public int disposeCalls;

        public void Append(string _line)
        {
            if (stage == "append")
            {
                throw new IOException("append denied");
            }
        }

        public void Flush()
        {
            if (stage == "flush")
            {
                throw new IOException("flush denied");
            }
        }

        public void Dispose()
        {
            disposeCalls++;
            if (stage == "dispose")
            {
                throw new IOException("dispose denied");
            }
        }
    }

    private static void Main(string[] _args)
    {
        string root = Path.GetFullPath(_args.Length == 0 ? "." : _args[0]);
        LogData settings = Newtonsoft.Json.JsonConvert.DeserializeObject<LogData>(
            File.ReadAllText(Path.Combine(root, "POC2/Assets/Resources/LogData.json")));
        Check(LogInfo.TryValidate(settings, out _), "actual LogData validates");
        Check(settings.deathEnemyRadius == 10f, "configured proposed radius is 10m");
        InvalidSettings(settings);
        SerializationAndTime(settings);
        IdentityAndBoundaries(settings);
        InitialStepAndSnapshots(settings);
        BufferingAndFailures(settings);
        DiskAndSample(settings, root);
        Console.WriteLine("PASS: " + CHECKS + " non-Unity checks; no Unity API executed.");
    }

    private static void InvalidSettings(LogData _settings)
    {
        Check(!LogInfo.TryValidate(null, out _), "missing settings rejected");
        var invalid = LogInfo.Copy(_settings);
        invalid.flushIntervalSeconds = float.NaN;
        Check(!LogInfo.TryValidate(invalid, out _), "NaN rejected");
        invalid = LogInfo.Copy(_settings);
        invalid.deathEnemyRadius = float.PositiveInfinity;
        Check(!LogInfo.TryValidate(invalid, out _), "infinite radius rejected");
        invalid = LogInfo.Copy(_settings);
        invalid.maxRecordBytes = invalid.maxBufferedBytes + 1;
        Check(!LogInfo.TryValidate(invalid, out _), "record must fit bounded buffer");
        invalid = LogInfo.Copy(_settings);
        invalid.schemaVersion = 0;
        int calls = 0;
        using (var session = new LogSession(invalid, _id => { calls++; return new MemoryLogStore(1); }))
        {
            Check(!session.TryRecord("invalid", null) && calls == 0 && session.Diagnostics.FailureCount == 1,
                "invalid configuration disables output with diagnostic");
        }
        var disabled = LogInfo.Copy(_settings);
        disabled.enabled = false;
        using (var session = new LogSession(disabled, _id => { calls++; throw new Exception("must not open"); }))
        {
            session.AdvanceFixedUpdate(0.02);
            Check(!session.TryRecord("disabled", null) && calls == 0, "disabled output never creates store");
            Check(session.ElapsedSeconds == 0.02, "disabled logger retains session clock");
        }
    }

    private static void SerializationAndTime(LogData _settings)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var memory = new MemoryLogStore(100);
            var config = LogInfo.Copy(_settings);
            using (var session = new LogSession(config, _id => memory, new TestUtcClock()))
            {
                config.enabled = false;
                config.schemaVersion = 999;
                Check(session.StartedUtc == "2026-10-30T14:00:00.0000000Z", "injected UTC converted to ISO 8601 UTC");
                var payload = new Dictionary<string, object>
                {
                    ["unknown"] = null,
                    ["zero"] = 0,
                    ["text"] = "한국어\n\r\t\"\\\u0001",
                    ["value"] = 1.25,
                    ["array"] = new object[] { null, 0, "탄약" }
                };
                Check(session.TryRecord("contract_probe", payload), "record accepts null dictionary and arrays");
                payload["text"] = "mutated";
                session.AdvanceFixedUpdate(0.02);
                session.SetContext(1, "combat");
                Check(session.TryRecord("probe_a", null) && session.TryRecord("probe_b", new { count = 0 }), "same tick events accepted");
                session.SetContext(1, "shop");
                session.AdvanceFixedUpdate(0.03);
                Check(Math.Abs(session.ElapsedSeconds - 0.05) < 1e-12, "shop accumulates actual changed fixedDeltaTime");
                double paused = session.ElapsedSeconds;
                Check(session.ElapsedSeconds == paused, "no tick means no elapsed time");
                session.AdvanceFixedUpdate(0.03);
                session.Flush();
                using (var first = JsonDocument.Parse(memory.Lines[0]))
                {
                    JsonElement data = first.RootElement;
                    Check(data.GetProperty("waveNumber").ValueKind == JsonValueKind.Null, "pre-wave JSON null");
                    Check(data.GetProperty("schemaVersion").GetInt32() == _settings.schemaVersion, "configuration is copied");
                    JsonElement p = data.GetProperty("payload");
                    Check(p.GetProperty("unknown").ValueKind == JsonValueKind.Null && p.GetProperty("zero").GetInt32() == 0, "null distinct from zero");
                    Check(p.GetProperty("text").GetString() == "한국어\n\r\t\"\\\u0001", "Korean escapes roundtrip and payload is frozen");
                    Check(p.GetProperty("array").GetArrayLength() == 3 && p.GetProperty("value").GetDouble() == 1.25, "array and invariant number roundtrip");
                    Check(!memory.Lines[0].Contains('\n') && !memory.Lines[0].Contains('\r'), "one physical JSON line");
                }
                using (var a = JsonDocument.Parse(memory.Lines[1]))
                using (var b = JsonDocument.Parse(memory.Lines[2]))
                {
                    Check(a.RootElement.GetProperty("elapsedSeconds").GetDouble() == b.RootElement.GetProperty("elapsedSeconds").GetDouble(), "same tick shares clock");
                    Check(b.RootElement.GetProperty("sequence").GetInt64() == a.RootElement.GetProperty("sequence").GetInt64() + 1, "same tick has ordered sequences");
                }
                using (var delta = JsonDocument.Parse(memory.Lines[3]))
                {
                    Check(delta.RootElement.GetProperty("eventType").GetString() == "fixed_delta_time_changed" &&
                        Math.Abs(delta.RootElement.GetProperty("elapsedSeconds").GetDouble() - 0.05) < 1e-12 &&
                        delta.RootElement.GetProperty("payload").GetProperty("fixedDeltaTime").GetDouble() == 0.03,
                        "step change records time and actual value exactly once");
                }
                Check(memory.Lines.Count == 4, "unchanged step does not generate extra change record");
                long sequence = session.Sequence;
                Check(!session.TryRecord("bad", new { value = double.NaN }), "non-finite JSON rejected");
                var cycle = new Dictionary<string, object>();
                cycle["self"] = cycle;
                Check(!session.TryRecord("cycle", cycle), "reference loop isolated");
                Check(session.Sequence == sequence && session.TryRecord("recovered", null), "serialization failure does not consume sequence or poison session");
                bool rejected = false;
                try
                {
                    session.AdvanceFixedUpdate(0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    rejected = true;
                }
                Check(rejected && Math.Abs(session.ElapsedSeconds - 0.08) < 1e-12, "invalid step cannot corrupt clock");
            }
            Check(memory.IsDisposed, "dispose releases store");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private static void IdentityAndBoundaries(LogData _settings)
    {
        var memory = new MemoryLogStore(100);
        var first = new LogSession(_settings, _id => memory);
        using (var second = new LogSession(_settings, _id => new MemoryLogStore(100)))
        {
            Check(first.SessionId != second.SessionId && second.Sequence == 0 && second.ElapsedSeconds == 0, "new session resets identity sequence and clock");
            var weapon = new object();
            string weaponId = first.GetWeaponId(weapon);
            Check(weaponId == first.GetWeaponId(weapon) && weaponId != first.GetWeaponId(new object()), "weapon reference retains stable ID across moves");
            var unique = new HashSet<string>();
            foreach (LogIdKind kind in Enum.GetValues(typeof(LogIdKind)))
            {
                unique.Add(first.NewId(kind));
                unique.Add(first.NewId(kind));
            }
            Check(unique.Count == 10 && unique.All(_id => _id.StartsWith(first.SessionId + ":", StringComparison.Ordinal)), "shot projectile spin purchase and weapon namespaces do not collide");
            Check(!first.TryReviseSummary(1, out _, out _), "unclosed summary cannot be revised");
            Check(first.TryCloseSummary(1, out string summary) && !first.TryCloseSummary(1, out _), "wave summary closes once");
            Check(first.TryReviseSummary(1, out string revised, out int revision) && revised == summary && revision == 1, "late result references original key with increasing revision");
            first.SetContext(2, "combat");
            first.TryRecord("late_result_probe", new { originWaveNumber = 1, summaryId = revised, revision });
            first.Dispose();
            first.Dispose();
            Check(memory.Lines.Count == 1, "repeated disposal does not duplicate buffered output");
            using (var record = JsonDocument.Parse(memory.Lines[0]))
            {
                Check(record.RootElement.GetProperty("waveNumber").GetInt32() == 2 &&
                    record.RootElement.GetProperty("payload").GetProperty("originWaveNumber").GetInt32() == 1, "origin and occurrence wave remain distinct");
            }
            double endedTime = first.ElapsedSeconds;
            first.AdvanceFixedUpdate(0.02);
            Check(!first.TryRecord("late", null) && first.NewId(LogIdKind.Spin) == null && first.ElapsedSeconds == endedTime,
                "ended session rejects callbacks and does not restart");
            Check(!first.TryCloseSummary(2, out _) && !first.TryReviseSummary(1, out _, out _), "ended session cannot mutate summary state");
            Check(second.BufferedRecords == 0 && second.Sequence == 0, "old callbacks do not contaminate new session");
        }
    }

    private static void BufferingAndFailures(LogData _settings)
    {
        var config = LogInfo.Copy(_settings);
        config.maxBufferedRecords = 3;
        var memory = new MemoryLogStore(2000);
        using (var session = new LogSession(config, _id => memory))
        {
            for (int i = 0; i < 1000; i++)
            {
                session.AdvanceFixedUpdate(0.02);
                Check(session.TryRecord("tick_probe", new { tick = i }), "tick " + i + " accepted");
                Check(session.BufferedRecords < 3 && session.BufferedBytes <= config.maxBufferedBytes, "buffer bounds " + i);
            }
            session.Flush();
            Check(memory.Lines.Count == 1000 && Math.Abs(session.ElapsedSeconds - 20d) < 1e-9, "flush preserves every fixed tick");
            for (int i = 0; i < memory.Lines.Count; i++)
            {
                using (var json = JsonDocument.Parse(memory.Lines[i]))
                {
                    Check(json.RootElement.GetProperty("sequence").GetInt64() == i + 1 &&
                        json.RootElement.GetProperty("payload").GetProperty("tick").GetInt32() == i, "ordered tick " + i);
                }
            }
        }
        config = LogInfo.Copy(_settings);
        config.maxBufferedBytes = 1000;
        config.maxRecordBytes = 600;
        memory = new MemoryLogStore(100);
        using (var session = new LogSession(config, _id => memory))
        {
            for (int i = 0; i < 8; i++)
            {
                session.TryRecord("byte_probe", new { text = new string('x', 200) });
            }
            Check(memory.FlushCount > 0 && session.BufferedBytes <= 1000, "byte pressure flushes before overflow");
            long seq = session.Sequence;
            Check(!session.TryRecord("oversize", new { text = new string('x', 1000) }) && session.Sequence == seq, "oversize record rejected without poisoning buffer");
            session.Flush();
            Check(memory.Lines.Count == 8, "byte flush preserves all records");
        }
        memory = new MemoryLogStore(100);
        using (var session = new LogSession(_settings, _id => memory))
        {
            session.TryRecord("interval_probe", null);
            session.AdvanceFixedUpdate(_settings.flushIntervalSeconds);
            Check(memory.Lines.Count == 1, "configured fixed-clock flush interval works");
        }
        using (var session = new LogSession(_settings, _id => throw new UnauthorizedAccessException("factory denied")))
        {
            Check(!session.TryRecord("failed", null) && session.Diagnostics.StorageFailed, "factory failure isolated");
        }
        foreach (string stage in new[] { "append", "flush", "dispose" })
        {
            var fault = new FaultStore { stage = stage };
            int warnings = 0;
            var session = new LogSession(_settings, _id => fault, _warning: _message => { warnings++; throw new Exception("diagnostic listener failed"); });
            session.TryRecord("fault_probe", null);
            session.Dispose();
            session.Dispose();
            Check(session.Diagnostics.StorageFailed && session.Diagnostics.FailureCount > 0 && warnings == 1 && fault.disposeCalls == 1,
                stage + " and warning callback failures never escape or prevent cleanup");
            Check(session.BufferedRecords == 0 && session.BufferedBytes == 0, stage + " releases buffered memory");
        }
        int warningCount = 0;
        using (var session = new LogSession(_settings, _id => new MemoryLogStore(100), _warning: _message => warningCount++))
        {
            for (int i = 0; i < 20; i++)
            {
                session.TryRecord("bad", new { value = double.PositiveInfinity });
            }
            Check(warningCount == _settings.maxWarningsPerSession && session.Diagnostics.DroppedRecords == 20 && session.Diagnostics.FailureCount == 20,
                "warning throttle retains failure and loss counts");
        }
    }

    private static void InitialStepAndSnapshots(LogData _settings)
    {
        var memory = new MemoryLogStore(100);
        using (var session = new LogSession(_settings, _id => memory, _initialFixedDeltaTime: 0.02))
        {
            Check(session.FixedDeltaTime == 0.02 && session.ElapsedSeconds == 0, "initial actual step captured without fictitious tick");
            session.AdvanceFixedUpdate(0.04);
            var weapon = new object();
            session.TryRecord("snapshot_probe", new LogPlayerSnapshot
            {
                activeHand = "right",
                activeSlot = 0,
                weapons = new[]
                {
                    new LogWeaponSnapshot { instanceId = session.GetWeaponId(weapon), hand = "right", loadedAmmo = 0, active = true }
                },
                reserves = new[] { new LogReserveSnapshot { ammoTypeId = "synthetic", reserve = 0 } }
            });
            session.Flush();
            Check(memory.Lines.Count == 2, "first tick detects change from captured initial step");
            using (var parsed = JsonDocument.Parse(memory.Lines[1]))
            {
                var p = parsed.RootElement.GetProperty("payload");
                Check(p.GetProperty("health").ValueKind == JsonValueKind.Null && p.GetProperty("position").ValueKind == JsonValueKind.Null,
                    "uncollected snapshot fields remain null");
                Check(p.GetProperty("weapons")[0].GetProperty("loadedAmmo").GetInt32() == 0 &&
                    p.GetProperty("reserves")[0].GetProperty("maximum").ValueKind == JsonValueKind.Null,
                    "snapshot ammunition distinguishes actual zero from unknown maximum");
            }
        }
    }

    private static void DiskAndSample(LogData _settings, string _root)
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrenchSlot-LogPhase1-" + Guid.NewGuid().ToString("N"));
        try
        {
            bool traversalRejected = false;
            try
            {
                new FileLogStore(directory, "../escape");
            }
            catch (ArgumentException)
            {
                traversalRejected = true;
            }
            Check(traversalRejected, "session path traversal rejected");
            FileLogStore store = null;
            using (var session = new LogSession(_settings, _id => store = new FileLogStore(directory, _id), new TestUtcClock()))
            {
                session.TryRecord("contract_probe", new
                {
                    synthetic = true,
                    utc = session.StartedUtc,
                    unknown = (int?)null,
                    actualZero = 0,
                    text = "한국어\n\"테스트\"\\",
                    values = new object[] { null, 0, "unknown" }
                });
                session.SetContext(1, "combat");
                session.AdvanceFixedUpdate(0.02);
                session.TryRecord("contract_probe", new { synthetic = true, shotId = session.NewId(LogIdKind.Shot) });
                session.AdvanceFixedUpdate(0.03);
                session.Flush();
                using (var reader = new StreamReader(new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    int lineCount = 0;
                    while (reader.ReadLine() != null)
                    {
                        lineCount++;
                    }
                    Check(lineCount == 3, "real store is readable after flush before close");
                }
            }
            byte[] bytes = File.ReadAllBytes(store.FilePath);
            Check(!(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf), "UTF-8 without BOM");
            Check(bytes[bytes.Length - 1] == 10, "JSONL ends in newline");
            string[] lines = File.ReadAllLines(store.FilePath, new UTF8Encoding(false, true));
            for (int i = 0; i < lines.Length; i++)
            {
                using (var parsed = JsonDocument.Parse(lines[i]))
                {
                    Check(parsed.RootElement.GetProperty("sequence").GetInt64() == i + 1 && parsed.RootElement.EnumerateObject().Count() == 8,
                        "real file envelope " + i);
                }
            }
            string before = File.ReadAllText(store.FilePath);
            using (var collision = new LogSession(_settings, _id => new FileLogStore(directory, Path.GetFileNameWithoutExtension(store.FilePath))))
            {
                collision.TryRecord("collision", null);
                collision.Flush();
                Check(collision.Diagnostics.StorageFailed, "existing session file cannot be overwritten");
            }
            Check(File.ReadAllText(store.FilePath) == before, "collision preserves original output");
            File.Copy(store.FilePath, Path.Combine(_root, "Docs/로그-시스템-1차.synthetic.jsonl"), true);
            string occupied = Path.Combine(directory, "occupied");
            File.WriteAllText(occupied, "not a directory");
            using (var denied = new LogSession(_settings, _id => new FileLogStore(occupied, _id)))
            {
                denied.TryRecord("io_failure", null);
                denied.Flush();
                Check(denied.Diagnostics.StorageFailed && denied.Diagnostics.DroppedRecords == 1, "real filesystem failure is isolated and counted");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
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
