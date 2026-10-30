using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int CHECKS;
    private static LogData SETTINGS;
    private static AmmoData AMMO;
    private static SlotMachineData SLOT;

    private sealed class BrokenEndObserver : ILogSessionEndObserver
    {
        public void SessionEnding(string _reason) => throw new IOException("synthetic end observer failure");
    }

    private sealed class Rig : IDisposable
    {
        public readonly MemoryLogStore store = new MemoryLogStore(10000);
        public readonly LogSession session;
        public readonly LogGameLifecycle life;
        public readonly LogSlotCollector slots;
        public readonly LogAmmoCollector ammoLog;
        public readonly AmmoInventory ammo = new AmmoInventory(AMMO);
        public readonly PlayerWallet wallet = new PlayerWallet(SLOT.spinCost * 100);
        public readonly object machine = new object();
        public readonly object original = new object();
        public readonly object swapped = new object();
        public readonly object left = new object();
        public int rightAmmo;
        public int leftAmmo;
        public bool swap;
        public bool akimbo;
        public double health = 50;
        public string AmmoId => AMMO.ammoTypes[0].id;

        public Rig(LogData _settings = null, bool _storageFailure = false)
        {
            session = new LogSession(_settings ?? SETTINGS, _id => _storageFailure ? throw new IOException("synthetic storage failure") : store);
            life = new LogGameLifecycle(session);
            slots = new LogSlotCollector(life);
            ammoLog = new LogAmmoCollector(life, () => new LogAmmoSnapshot { reserves = State().player.reserves });
            ammoLog.Observed += slots.ObserveAmmo;
            ammo.AmmoLog = ammoLog;
            life.RegisterCollector(ammoLog);
            life.Start("synthetic-build", null, null, "synthetic-map");
            life.BeginWave(new LogWavePlan { waveNumber = 1, durationSeconds = 30,
                plannedEnemies = new Dictionary<string, int> { ["synthetic-enemy"] = 3 } }, null);
            life.AdvanceFixedUpdate(0.02);
        }

        public LogSlotState State()
        {
            return new LogSlotState
            {
                waveNumber = session.WaveNumber, phase = session.Phase, elapsedSeconds = session.ElapsedSeconds,
                player = new LogPlayerSnapshot
                {
                    money = wallet.Balance, health = health, position = new LogPosition3 { x = 1, y = 2, z = 3 },
                    weapons = new[]
                    {
                        new LogWeaponSnapshot { instanceId = session.GetWeaponId(swap ? swapped : original), weaponTypeId = swap ? "synthetic-swapped" : "synthetic-original",
                            ammoTypeId = AmmoId, hand = "right", slot = 0, loadedAmmo = rightAmmo, active = true },
                        new LogWeaponSnapshot { instanceId = session.GetWeaponId(left), weaponTypeId = "synthetic-left",
                            ammoTypeId = AmmoId, hand = akimbo ? "left" : null, slot = 1, loadedAmmo = leftAmmo, active = akimbo }
                    },
                    reserves = AMMO.ammoTypes.Select(_entry => new LogReserveSnapshot { ammoTypeId = _entry.id,
                        reserve = ammo.GetReserve(_entry.id), maximum = ammo.GetMaximum(_entry.id) }).ToArray()
                },
                enemies = new LogEnemySnapshot { aliveCount = 3, aliveByKind = new Dictionary<string, int> { ["synthetic-enemy"] = 3 } }
            };
        }

        public string[] Symbols(int[] _faces)
        {
            return _faces.Select(_face => SLOT.symbols.FirstOrDefault(_symbol => _symbol.reelIndex == _face)?.id).ToArray();
        }

        public LogSpinContext Begin(int[] _faces, bool _accepted = true, int? _cost = null)
        {
            LogSlotState before = State();
            int cost = _cost ?? SLOT.spinCost;
            Check(wallet.TrySpend(cost), "Controlled spin payment succeeds");
            if (!_accepted)
            {
                wallet.Add(cost);
                return null;
            }
            return slots.Start(machine, cost, SLOT.spinCost, SLOT.spinSeconds, _faces, Symbols(_faces), before);
        }

        public void GrantLoaded(LogSpinContext _spin, int _hand, int _capacity)
        {
            int before = _hand == 0 ? rightAmmo : leftAmmo;
            int added = Math.Max(0, _capacity - before);
            if (_hand == 0)
            {
                rightAmmo += added;
            }
            else
            {
                leftAmmo += added;
            }
            ammoLog.Weapon("slot_loaded", _hand == 0 ? (swap ? swapped : original) : left,
                AmmoId, added, _hand == 0 ? "right" : "left", _spin.SpinId);
        }

        public JObject[] Rows()
        {
            session.Flush();
            return store.Lines.Select(JObject.Parse).ToArray();
        }

        public JObject[] Events(string _type) => Rows().Where(_row => (string)_row["eventType"] == _type).ToArray();

        public void Dispose() => life.End("return_to_title", null, null);
    }

    private static void Check(bool _value, string _message)
    {
        CHECKS++;
        if (!_value)
        {
            throw new Exception(_message);
        }
    }

    private static void Main()
    {
        SETTINGS = JsonConvert.DeserializeObject<LogData>(File.ReadAllText("POC2/Assets/Resources/LogData.json"));
        AMMO = JsonConvert.DeserializeObject<AmmoData>(File.ReadAllText("POC2/Assets/Resources/AmmoData.json"));
        SLOT = JsonConvert.DeserializeObject<SlotMachineData>(File.ReadAllText("POC2/Assets/Resources/SlotMachineData.json"));
        Check(SLOT.symbols.Length >= 3 && SLOT.spinCost > 0, "Read current JSON symbols, costs and ammo tables");
        StartAndRefund();
        Outcomes();
        SwapsAndActualAmmo();
        Boundaries();
        IncompleteAndIsolation();
        Failures();
        StaticIntegration();
        Console.WriteLine("PASS: " + CHECKS + " phase-6 non-Unity checks; no Unity API executed.");
    }

    private static void StartAndRefund()
    {
        using (var rig = new Rig())
        {
            int[] faces = SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray();
            int balance = rig.wallet.Balance;
            long sequence = rig.session.Sequence;
            Check(rig.Begin(faces, false) == null && rig.wallet.Balance == balance, "Rejected BeginSpin refunded without spin context");
            Check(rig.session.Sequence == sequence && rig.slots.PendingCount == 0, "Refund does not create successful start or consume spin ID");
            int discounted = SLOT.spinCost / 2;
            var spin = rig.Begin(faces, true, discounted);
            Check(spin.SpinId.EndsWith(":spin:1") && rig.slots.PendingCount == 1, "First accepted spin gets first spin ID");
            Check(rig.slots.Start(rig.machine, discounted, SLOT.spinCost, 1, faces, rig.Symbols(faces), rig.State()) == null, "Duplicate owner input creates no new context");
            string firstSymbol = rig.Symbols(faces)[0];
            faces[0] = -999;
            var start = rig.Events("slot_spin_started").Single()["payload"];
            Check((int)start["actualCost"] == discounted && (int)start["baseCost"] == SLOT.spinCost, "Discount cost separate from payout basis");
            Check((int)start["before"]["player"]["money"] == balance && (double)start["before"]["player"]["health"] == 50, "Pre-payment money and health preserved");
            Check((double)start["before"]["player"]["position"]["z"] == 3 && (int)start["before"]["enemies"]["aliveCount"] == 3, "Position and alive enemies captured");
            Check((string)start["symbolIds"][0] == firstSymbol && (int)start["faces"][0] != -999, "Original three faces and IDs copied");
            rig.slots.Healed(spin, 7, 57);
            rig.slots.Healed(spin, 7, 57);
            Check(rig.Events("slot_spin_healed").Length == 1, "Actual heal recorded once");
            rig.slots.Resolve(spin, rig.State(), rig.State(), new LogSlotOutcome { isMiss = true }, 1, null, 0);
            Check(!rig.slots.Resolve(spin, null, null, null, 1, null, 0) && rig.Events("slot_spin_resolved").Length == 1, "Duplicate result rejected");
            var next = rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray());
            Check(next.SpinId != spin.SpinId, "Next spin gets new identity");
            Check(!rig.slots.Resolve(spin, null, null, null, 1, null, 0) && rig.slots.PendingCount == 1, "Old callback cannot close next spin");
        }
    }

    private static void Outcomes()
    {
        var cashSymbol = SLOT.symbols.First(_s => _s.multiplier > 0);
        var ammoSymbol = SLOT.symbols.First(_s => _s.fullAmmo);
        foreach (string scenario in new[] { "miss", "cash", "ammo", "cash_and_ammo" })
        {
            using (var rig = new Rig())
            {
                bool cash = scenario.Contains("cash");
                bool ammo = scenario.Contains("ammo");
                int[] faces = cash ? Enumerable.Repeat(cashSymbol.reelIndex, 3).ToArray()
                    : ammo ? Enumerable.Repeat(ammoSymbol.reelIndex, 3).ToArray()
                    : SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray();
                var spin = rig.Begin(faces);
                var before = rig.State();
                int actualAmmo = ammo ? rig.ammo.Add(rig.AmmoId, 4, "slot_reserve", spin.SpinId) : 0;
                int payout = cash ? (int)Math.Floor(SLOT.spinCost * cashSymbol.multiplier * 1.5) : 0;
                rig.wallet.Add(payout);
                var outcome = new LogSlotOutcome { multiplier = cash ? cashSymbol.multiplier : 0,
                    tripleFace = cash ? cashSymbol.reelIndex : ammo ? ammoSymbol.reelIndex : -1,
                    pairFace = -1, isMiss = !cash && !ammo, hasAmmo = ammo };
                Check(rig.slots.Resolve(spin, before, rig.State(), outcome, 1.5, cash ? 1.5 : null, payout), "Controlled " + scenario + " resolved");
                var result = rig.Events("slot_spin_resolved").Single()["payload"];
                Check((long)result["actualMoneyDelta"] == payout && (int)result["balanceAfter"] == rig.wallet.Balance, "Actual " + scenario + " cash delta");
                Check((int)result["calculatedPayout"] == payout && (double)result["piggyMultiplier"] == 1.5, "Final payout and multiplier preserved");
                Check(result["ammoGranted"].Sum(_r => (int)_r["acquired"]) == actualAmmo, "Actual " + scenario + " ammunition");
                Check(result["faces"].Values<int>().SequenceEqual(faces) && (string)result["spinId"] == spin.SpinId, "Start/result faces and spin match");
                if (ammo)
                {
                    var grant = result["ammoGranted"].Single();
                    Check((string)grant["causeId"] == spin.SpinId && (int)grant["reserveDelta"] == actualAmmo, "Canonical ammo change linked by cause");
                    Check(rig.Events("ammo_changed").Length == 1, "Result does not emit second ammo acquisition");
                }
            }
        }
    }

    private static void SwapsAndActualAmmo()
    {
        using (var rig = new Rig())
        {
            var spin = rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray());
            string original = (string)rig.Events("slot_spin_started")[0]["payload"]["before"]["player"]["weapons"][0]["instanceId"];
            rig.swap = true;
            rig.akimbo = true;
            rig.rightAmmo = 5;
            rig.leftAmmo = 2;
            var before = rig.State();
            rig.GrantLoaded(spin, 0, 5);
            rig.GrantLoaded(spin, 1, 5);
            int room = rig.ammo.GetMaximum(rig.AmmoId) - rig.ammo.GetReserve(rig.AmmoId);
            int actual = rig.ammo.Add(rig.AmmoId, room + 100, "slot_reserve", spin.SpinId);
            rig.ammo.Add(rig.AmmoId, 3, "slot_reserve", spin.SpinId);
            rig.slots.Resolve(spin, before, rig.State(), new LogSlotOutcome { hasAmmo = true }, 1, null, 0);
            var result = rig.Events("slot_spin_resolved").Single()["payload"];
            Check((string)result["before"]["player"]["weapons"][0]["instanceId"] != original, "Result uses currently swapped weapon");
            var grants = result["ammoGranted"].ToArray();
            Check(grants.Length == 4 && (int)grants[0]["acquired"] == 0 && (int)grants[1]["acquired"] == 3, "Full right and partial left grant actual zero/three");
            Check((string)grants[1]["hand"] == "left" && (int)grants[1]["loadedDelta"] == 3 && (int)grants[1]["reserveDelta"] == 0, "Loaded versus shared reserve distinguished");
            Check((int)grants[2]["acquired"] == actual && (int)grants[3]["acquired"] == 0, "Reserve partial cap and full-zero are actual values");
            Check((int)result["after"]["player"]["weapons"][0]["loadedAmmo"] == 5 && (int)result["after"]["player"]["weapons"][1]["loadedAmmo"] == 5, "Hands captured after payout");
            rig.life.CompleteWave(null, null, 0, 0);
            var summary = rig.Events("wave_summary")[0]["payload"]["summaries"]["ammo"]["data"]["ammoTypes"].Single(_r => (string)_r["ammoTypeId"] == rig.AmmoId);
            Check((long)summary["acquiredBySource"]["slot_loaded"] == 3 && (long)summary["acquiredBySource"]["slot_reserve"] == actual, "Wave acquisition not doubled by slot result");
            rig.life.End("return_to_title", null, null);
            File.WriteAllLines("Docs/로그-시스템-6차.synthetic.jsonl", rig.store.Lines);
        }
    }

    private static void Boundaries()
    {
        using (var rig = new Rig())
        {
            var spin = rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray());
            rig.life.CompleteWave(null, null, 0, 0);
            rig.life.SetPhase("shop");
            rig.life.AdvanceFixedUpdate(0.05);
            var before = rig.State();
            rig.ammo.Add(rig.AmmoId, 1, "slot_reserve", spin.SpinId);
            rig.wallet.Add(7);
            rig.slots.Resolve(spin, before, rig.State(), new LogSlotOutcome { isMiss = true }, 1, null, 0);
            var result = rig.Events("slot_spin_resolved").Single();
            Check((string)result["phase"] == "shop" && (int)result["payload"]["originWaveNumber"] == 1, "Shop result retains origin wave");
            Check((double)result["payload"]["resolvedElapsedSeconds"] > spin.StartedElapsedSeconds, "Shared clock advances through shop");
            Check((long)result["payload"]["actualMoneyDelta"] == 7, "Fallback cash included even calculated payout zero");
            Check(rig.Events("wave_summary_correction").Length == 0 && rig.Events("wave_summary").Length == 1, "Late slot does not revise closed wave");
        }
        using (var rig = new Rig())
        {
            var spin = rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray());
            rig.life.CompleteWave(null, null, 0, 0);
            rig.life.BeginWave(new LogWavePlan { waveNumber = 2, durationSeconds = 10 }, null);
            rig.slots.Resolve(spin, rig.State(), rig.State(), new LogSlotOutcome(), 1, null, 0);
            var result = rig.Events("slot_spin_resolved").Single();
            Check((int)result["waveNumber"] == 2 && (int)result["payload"]["originWaveNumber"] == 1, "Envelope wave differs from original spin wave");
        }
    }

    private static void IncompleteAndIsolation()
    {
        foreach (string reason in new[] { "death", "return_to_title", "application_quit", "unknown" })
        {
            using (var old = new Rig())
            using (var next = new Rig())
            {
                var spin = old.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray());
                old.life.End(reason, null, null, reason == "death");
                old.life.End(reason, null, null, reason == "death");
                var incomplete = old.Events("slot_spin_incomplete").Single();
                Check((string)incomplete["payload"]["spinId"] == spin.SpinId && (string)incomplete["payload"]["status"] == "incomplete", "Unresolved " + reason + " gets incomplete once");
                Check(incomplete["payload"]["outcome"] == null && incomplete["payload"]["actualMoneyDelta"] == null, "Incomplete invents no outcome or reward");
                Check((long)incomplete["sequence"] < (long)old.Events("session_end").Single()["sequence"] && old.slots.PendingCount == 0, "Incomplete precedes end and releases owner");
                long previous = old.session.Sequence;
                old.wallet.Add(1);
                Check(!old.slots.Resolve(spin, null, null, null, 1, null, 0) && old.session.Sequence == previous, "Late old-session callback rejected");
                Check(!next.slots.Resolve(spin, null, null, null, 1, null, 0) && next.slots.PendingCount == 0, "Foreign spin rejected in new session");
                Check(next.Events("slot_spin_resolved").Length == 0, "Old result never leaks into restart");
            }
        }
    }

    private static void Failures()
    {
        LogData disabled = LogInfo.Copy(SETTINGS);
        disabled.enabled = false;
        using (var rig = new Rig(disabled))
        {
            int before = rig.wallet.Balance;
            Check(rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray()) == null && rig.wallet.Balance == before - SLOT.spinCost,
                "Disabled logger does not change gameplay payment");
            Check(rig.session.Sequence == 0, "Disabled logger writes nothing");
        }
        using (var rig = new Rig(_storageFailure: true))
        {
            Check(rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray()) == null && rig.session.Diagnostics.StorageFailed,
                "Failed storage leaves gameplay running");
        }
        using (var rig = new Rig())
        {
            Check(rig.slots.Start(rig.machine, 1, 1, double.NaN, new[] { 0, 1, 2 }, new[] { "a", "b", "c" }, rig.State()) == null &&
                rig.slots.PendingCount == 0, "Bad start collected as diagnostic without phantom spin");
            var spin = rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray());
            Check(!rig.slots.Resolve(spin, rig.State(), rig.State(), new LogSlotOutcome(), double.NaN, null, 0), "Invalid result rejected");
            Check(rig.Events("slot_spin_incomplete").Length == 1 && rig.slots.PendingCount == 0, "Collection failure closes unresolved spin explicitly");
            var second = rig.Begin(SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray());
            rig.slots.Resolve(second, null, null, new LogSlotOutcome(), 1, null, 0);
            Check(rig.Events("slot_spin_resolved").Single()["payload"]["actualMoneyDelta"].Type == JTokenType.Null, "Unknown cash is null rather than fabricated zero");
            rig.ammoLog.Observed += _change => throw new Exception("synthetic observer failure");
            int before = rig.ammo.GetReserve(rig.AmmoId);
            Check(rig.ammo.Add(rig.AmmoId, 1, "drop") == 1 && rig.ammo.GetReserve(rig.AmmoId) == before + 1, "Observation failure does not block actual ammunition");
        }
        var store = new MemoryLogStore(100);
        var session = new LogSession(SETTINGS, _id => store);
        var life = new LogGameLifecycle(session);
        life.RegisterSessionEndObserver(new BrokenEndObserver());
        var slots = new LogSlotCollector(life);
        life.Start("synthetic", null, null, null);
        int[] faces = SLOT.symbols.Take(3).Select(_s => _s.reelIndex).ToArray();
        var first = slots.Start(new object(), SLOT.spinCost, SLOT.spinCost, 1, faces,
            SLOT.symbols.Take(3).Select(_s => _s.id).ToArray(), new LogSlotState());
        slots.Start(new object(), SLOT.spinCost, SLOT.spinCost, 1, faces,
            SLOT.symbols.Take(3).Select(_s => _s.id).ToArray(), new LogSlotState());
        Check(slots.PendingCount == 2, "Different machines can have separate pending spins");
        Check(!slots.Resolve(first, null, null, null, 1, null, 0), "Unconfirmed outcome cannot be marked resolved");
        life.End("application_quit", null, null);
        var rows = store.Lines.Select(JObject.Parse).ToArray();
        Check(rows.Count(_row => (string)_row["eventType"] == "slot_spin_incomplete") == 2 &&
            rows.Count(_row => (string)_row["eventType"] == "session_end") == 1, "Failed end observer cannot suppress other spin closures or session end");
        Check(slots.PendingCount == 0 && session.Diagnostics.FailureCount > 0, "Observer failure diagnosed and pending state released");
    }

    private static void StaticIntegration()
    {
        string source = File.ReadAllText("POC2/Assets/Scripts/Slot/SlotMachinePresenter.cs");
        Check(source.IndexOf("if (!started)") < source.IndexOf("activeSlotLog?.Start("), "Success context created only after successful BeginSpin");
        Check(source.Contains("gameManager.Wallet.Add(cost)") && source.Contains("spinOperation = null;"), "Refund and callback token retirement preserved");
        Check(source.Contains("ReferenceEquals(spinOperation, operation)"), "Stale animation callback identity guarded");
        Check(source.Contains("SymbolIds(faces)") && source.Contains("symbol.reelIndex == _faces[i]"), "Actual faces mapped by table index without re-roll");
        Check(source.Contains("gameManager.Wallet, activeLogSpin?.SpinId)"), "Slot reserve reward passes canonical spin cause");
        Check(source.IndexOf("beforePayout =") < source.IndexOf("SlotRewardInfo.Apply(") &&
            source.IndexOf("afterPayout =") > source.IndexOf("gameManager.Wallet.Add(payout)"), "State brackets actual reward application");
        Check(source.Contains("SpinStarted?.Invoke(cost)") && source.Contains("SpinResolved?.Invoke(_outcome, payout, text.ToString())"), "Legacy subscriber event signatures retained");
        Check(!source.Contains("InputGate.PlayerEnabled =") && !source.Contains("SlotSpinStarted(") && !source.Contains("SlotSpinResolved("), "No input gate or duplicate legacy writer");
        string lifecycle = File.ReadAllText("POC2/Assets/Scripts/Logging/LogGameLifecycle.cs");
        Check(lifecycle.IndexOf("observer.SessionEnding(reason)") < lifecycle.IndexOf("session.TryRecord(\"session_end\""), "Pending spins close before session end");
        Check(File.Exists("POC2/Assets/Scripts/Logging/LogSlotCollector.cs.meta"), "Unity source metadata supplied");
    }
}
