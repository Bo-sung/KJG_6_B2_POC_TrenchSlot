using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int CHECKS;

    private static void Check(bool _condition, string _message)
    {
        CHECKS++;
        if (!_condition)
        {
            throw new Exception(_message);
        }
    }

    private static void Main()
    {
        var store = new MemoryLogStore(100);
        var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<LogData>(File.ReadAllText("POC2/Assets/Resources/LogData.json"));
        var session = new LogSession(settings, _id => store);
        var life = new LogGameLifecycle(session);
        var events = new LogGameplayEvents(life);
        var combat = new LogCombatCollector(life);
        var slots = new LogSlotCollector(life);
        var shop = new LogShopCollector(life);
        var ammo = new LogAmmoCollector(life, () => null);
        ammo.Observed += slots.ObserveAmmo;
        life.RegisterCollector(combat);
        events.SlotSpinRequested(10, 100);
        Check(session.Sequence == 0, "No phantom events before session start");
        life.Start("test", null, null, "test-map");
        life.BeginWave(new LogWavePlan { waveNumber = 1, durationSeconds = 10 }, null);
        life.AdvanceFixedUpdate(0.02);
        events.SlotSpinRequested(10, 100);
        events.SlotSpinRejected("ReelsBusy", 10, 100);
        int[] faces = { 1, 2, 3 };
        var slotBefore = new LogSlotState { waveNumber = 1, phase = "wave",
            player = new LogPlayerSnapshot { money = 100, health = 47 } };
        var spin = slots.Start(new object(), 10, 10, 1, faces, new[] { "a", "b", "c" }, slotBefore);
        slots.Healed(spin, 3, 50);
        faces[0] = 99;
        ammo.Change(new LogAmmoChange
        {
            source = "slot_reserve", ammoTypeId = "rifle", reserveDelta = 5, acquired = 5, causeId = spin.SpinId
        });
        slots.Resolve(spin, new LogSlotState { player = new LogPlayerSnapshot { money = 90 } },
            new LogSlotState { player = new LogPlayerSnapshot { money = 113 } },
            new LogSlotOutcome { multiplier = 2, isNearMiss = true }, 1.5, 1.5, 20, new[] { "part" });
        var purchase = shop.Begin(new LogShopProduct { itemType = "Ammo", itemId = "rifle", price = 10 },
            new LogShopSnapshot { player = new LogPlayerSnapshot { money = 113 } }, null);
        shop.Complete(purchase, true, "", null, new LogShopSnapshot { player = new LogPlayerSnapshot { money = 103 } });
        var rejected = shop.Begin(new LogShopProduct { itemType = "Weapon", itemId = "pistol", price = 200 },
            new LogShopSnapshot { player = new LogPlayerSnapshot { money = 103 } }, null);
        shop.Complete(rejected, true, "InsufficientFunds", "insufficient_funds", new LogShopSnapshot { player = new LogPlayerSnapshot { money = 103 } });
        events.WeaponDropped("pistol", 5, "PickupSwap");
        events.WeaponAcquired("WeaponPickedUp", "rifle", 1, "pistol", 7);
        var shot = combat.StartShot(new object(), "rifle", 0, null, 1, 7, 9, true, true);
        string id = shot.NewProjectileId();
        shot.ProjectileCreated(id, true);
        shot.FinalizeShot();
        shot.FinalizeShot();
        var impact = new LogCombatImpact { projectileId = id, targetId = "enemy", hitEnemy = true,
            actualDamage = 5, killed = true, headshot = true, helmetHit = true, staggered = true };
        shot.Impact(impact);
        shot.Impact(impact);
        shot.ProjectileEnded(id, "impact");
        combat.PlayerDamaged(null, 100, 90);
        life.CompleteWave(null, null, 0, 0);
        events.IntermissionStarted(5, 103);
        life.End("death", null, null, true);
        long closed = session.Sequence;
        slots.Resolve(spin, null, null, null, 1, null, 0);
        combat.PlayerDamaged(null, 90, 0);
        Check(session.Sequence == closed, "Late callbacks do not reopen ended session");
        var rows = store.Lines.Select(JObject.Parse).ToArray();
        Func<string, JObject> row = _type => rows.Single(_r => (string)_r["eventType"] == _type);
        foreach (var r in rows)
        {
            Check((string)r["sessionId"] == session.SessionId, "One shared session ID");
            Check(r["runId"] == null && r["runSeconds"] == null, "No legacy clock or run ID");
        }
        Check(rows.Select(_r => (long)_r["sequence"]).SequenceEqual(Enumerable.Range(1, rows.Length).Select(_n => (long)_n)), "One consecutive sequence");
        Check((int)row("slot_spin_started")["payload"]["faces"][0] == 1, "Slot payload copied immediately");
        Check((float)row("slot_spin_healed")["payload"]["actualHealthRestored"] == 3, "Actual heal preserved");
        Check((string)row("slot_spin_resolved")["payload"]["plannedPartIds"][0] == "part", "Part reward plan preserved");
        Check((int)row("slot_spin_resolved")["payload"]["ammoGranted"][0]["acquired"] == 5, "Actual ammo reward preserved");
        Check((int)rows.Single(_r => (string)_r["eventType"] == "shop_purchase_result" && (bool)_r["payload"]["success"])["payload"]["actualSpent"] == 10, "Actual spending preserved");
        Check((int)rows.Single(_r => (string)_r["eventType"] == "shop_purchase_result" && !(bool)_r["payload"]["success"])["payload"]["actualSpent"] == 0, "Failed purchase not represented as quoted price");
        Check((bool)row("player_shot")["payload"]["aiming"] && (bool)row("player_shot")["payload"]["akimbo"], "Shot posture preserved");
        Check((int)row("player_shot")["payload"]["magazineAmmo"] == 7, "Shot ammo snapshot preserved");
        Check((bool)row("projectile_impact")["payload"]["impact"]["headshot"] &&
            (bool)row("projectile_impact")["payload"]["impact"]["helmetHit"] &&
            (bool)row("projectile_impact")["payload"]["impact"]["staggered"], "Impact details preserved");
        Check((int)row("wave_summary")["payload"]["summaries"]["combat"]["data"]["weapons"][0]["shots"] == 1, "Shot counted once");
        Check((int)row("wave_summary")["payload"]["summaries"]["combat"]["data"]["weapons"][0]["hitShots"] == 1, "Hit counted once");
        Check((double)row("wave_summary")["payload"]["summaries"]["combat"]["data"]["weapons"][0]["actualDamageDealt"] == 5, "Damage counted once");
        settings.enabled = false;
        var disabled = new LogSession(settings, _id => throw new Exception("disabled store"));
        var disabledLife = new LogGameLifecycle(disabled);
        disabledLife.Start("test", null, null, null);
        new LogShopCollector(disabledLife).Begin(new LogShopProduct { itemType = "Ammo", itemId = "x" }, null, null);
        Check(disabled.Sequence == 0, "Canonical setting disables gameplay events");
        foreach (string path in Directory.GetFiles("POC2/Assets", "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            Check(!source.Contains("<<<<<<<") && !source.Contains(">>>>>>>"), "No conflict markers: " + path);
            Check(!source.Contains("GameTelemetry.") && !source.Contains("GameLogger.Count(") && !source.Contains("GameLogger.Measure(") && !source.Contains("GameLogger.Data("), "No legacy gameplay writer: " + path);
        }
        File.WriteAllLines("Docs/로그-시스템-통합.synthetic.jsonl", store.Lines);
        Console.WriteLine("PASS: " + CHECKS + " logging integration checks; no Unity API executed.");
    }
}
