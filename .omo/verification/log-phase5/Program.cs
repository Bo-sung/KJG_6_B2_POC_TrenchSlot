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
    private static AmmoData TABLE;

    private sealed class Rig : IDisposable
    {
        public readonly MemoryLogStore store = new MemoryLogStore(10000);
        public readonly LogSession session;
        public readonly LogGameLifecycle life;
        public readonly LogCombatCollector combat;
        public readonly LogAmmoCollector log;
        public readonly AmmoInventory inventory;
        public readonly object right = new object();
        public readonly object left = new object();
        public int rightAmmo;
        public int leftAmmo;
        public bool floor;
        public readonly string ammoId = TABLE.ammoTypes[0].id;

        public Rig(LogData _settings = null)
        {
            session = new LogSession(_settings ?? SETTINGS, _id => store);
            life = new LogGameLifecycle(session);
            combat = new LogCombatCollector(life);
            inventory = new AmmoInventory(TABLE);
            log = new LogAmmoCollector(life, Snapshot);
            life.RegisterCollector(combat);
            life.RegisterCollector(log);
            inventory.AmmoLog = log;
            life.Start("synthetic-build", null, null, "synthetic-map");
        }

        public LogAmmoSnapshot Snapshot()
        {
            return new LogAmmoSnapshot
            {
                reserves = TABLE.ammoTypes.Select(_entry => new LogReserveSnapshot
                {
                    ammoTypeId = _entry.id, reserve = inventory.GetReserve(_entry.id), maximum = inventory.GetMaximum(_entry.id)
                }).ToArray(),
                weapons = new[]
                {
                    new LogAmmoWeaponSnapshot { instanceId = session.GetWeaponId(right), weaponTypeId = "synthetic-right", ammoTypeId = ammoId,
                        loadedAmmo = rightAmmo, location = floor ? "floor" : "carried", hand = floor ? null : "right" },
                    new LogAmmoWeaponSnapshot { instanceId = session.GetWeaponId(left), weaponTypeId = "synthetic-left", ammoTypeId = ammoId,
                        loadedAmmo = leftAmmo, location = "carried", hand = "left" }
                }
            };
        }

        public void Begin(int _number = 1)
        {
            life.BeginWave(new LogWavePlan { waveNumber = _number, durationSeconds = 30,
                plannedEnemies = new Dictionary<string, int>() }, null);
        }

        public int Total => inventory.GetReserve(ammoId) + rightAmmo + leftAmmo;

        public void Reload(int _hand, int _requested)
        {
            int transferred = inventory.Consume(ammoId, _requested);
            if (_hand == 0)
            {
                rightAmmo += transferred;
            }
            else
            {
                leftAmmo += transferred;
            }
            log.Reload(_hand == 0 ? right : left, ammoId, _hand, transferred);
        }

        public void Fire(bool _saved, int _pellets)
        {
            int before = rightAmmo;
            if (!_saved)
            {
                rightAmmo--;
            }
            LogShotContext shot = combat.StartShot(right, "synthetic-right", 0, null, _pellets);
            shot.FinalizeShot();
            log.Shot(shot, ammoId, before, rightAmmo);
        }

        public JObject[] Rows()
        {
            session.Flush();
            return store.Lines.Select(JObject.Parse).ToArray();
        }

        public JObject Summary(int _index = 0)
        {
            return (JObject)Rows().Where(_r => (string)_r["eventType"] == "wave_summary").ElementAt(_index)["payload"]["summaries"]["ammo"]["data"];
        }

        public JObject Totals(int _index = 0)
        {
            return (JObject)Summary(_index)["ammoTypes"].Single(_r => (string)_r["ammoTypeId"] == ammoId);
        }

        public void Dispose()
        {
            life.End("unknown", null, null);
        }
    }

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
        SETTINGS = JsonConvert.DeserializeObject<LogData>(File.ReadAllText("POC2/Assets/Resources/LogData.json"));
        TABLE = JsonConvert.DeserializeObject<AmmoData>(File.ReadAllText("POC2/Assets/Resources/AmmoData.json"));
        Check(TABLE.ammoTypes.Length > 0 && JObject.Parse(File.ReadAllText("POC2/Assets/Resources/ShopData.json"))["ammoPacks"] != null,
            "Load actual ammo types and shop packs from JSON");
        Acquisition();
        TransfersAndShots();
        MovesAndCapacity();
        InitialAndFloor();
        ShortagesAndBoundaries();
        Failures();
        SourceIntegration();
        Console.WriteLine("PASS: " + CHECKS + " phase-5 non-Unity checks; no Unity API executed.");
    }

    private static void Acquisition()
    {
        using (var rig = new Rig())
        {
            rig.Begin();
            foreach (var entry in TABLE.ammoTypes)
            {
                int before = rig.inventory.GetReserve(entry.id);
                int requested = entry.maxReserve + 10;
                int actual = rig.inventory.Add(entry.id, requested, "drop");
                Check(actual == entry.maxReserve - before, "Drop accepts only capacity per JSON type");
                Check(rig.inventory.Add(entry.id, 1, "drop") == 0, "Full inventory accepts zero");
                Check(rig.inventory.Add(entry.id, 0, "slot_reserve") == 0, "Zero slot grant observed");
                int used = rig.inventory.Consume(entry.id, Math.Min(7, entry.maxReserve));
                Check(rig.inventory.Add(entry.id, used + 100, "shop_pack") == used, "Pack fill actual amount capped");
            }
            rig.life.CompleteWave(null, null, 0, 0);
            Check(rig.Summary()["start"]["reserves"].Count() == TABLE.ammoTypes.Length, "Every table type has starting reserve and maximum");
            foreach (var value in rig.Summary()["ammoTypes"])
            {
                Check((long)value["acquiredBySource"]["slot_reserve"] == 0, "Observed zero differs from absent source");
            }
            Check(rig.Rows().Where(_r => (string)_r["eventType"] == "ammo_changed").Select(_r => (string)_r["payload"]["transactionId"]).Distinct().Count()
                == rig.Rows().Count(_r => (string)_r["eventType"] == "ammo_changed"), "Each observation has own transaction ID");
        }
    }

    private static void TransfersAndShots()
    {
        using (var rig = new Rig())
        {
            rig.Begin();
            int total = rig.Total;
            rig.Reload(0, 3);
            rig.Reload(1, 2);
            Check(rig.Total == total, "Both hands reload without acquisition or consumption");
            rig.Fire(true, 8);
            rig.Fire(false, 8);
            Check(rig.Total == total - 1, "Saved shot consumes zero, pellet shot consumes one");
            rig.Reload(0, 1);
            Check(rig.Total == total - 1, "Partial reload cancellation adds no phantom ammunition");
            int before = rig.rightAmmo;
            rig.rightAmmo += 4;
            rig.log.Weapon("slot_loaded", rig.right, rig.ammoId, rig.rightAmmo - before, "right", "synthetic-spin");
            rig.log.Weapon("slot_loaded", rig.left, rig.ammoId, 0, "left", "synthetic-spin");
            rig.life.CompleteWave(null, null, 0, 0);
            var totals = rig.Totals();
            Check((long)totals["consumed"] == 1 && (long)totals["reloaded"] == 6, "Summary separates actual spending and transfers");
            Check((long)totals["acquiredBySource"]["slot_loaded"] == 4, "Slot loaded grant measured once per hand");
            Check(totals["acquiredBySource"]["reload"] == null, "Reload is not an acquisition source");
            var shots = rig.Rows().Where(_r => (string)_r["eventType"] == "player_shot").ToArray();
            var changes = rig.Rows().Where(_r => (string)_r["payload"]?["source"] == "shot").ToArray();
            Check(shots.Length == 2 && changes.Length == 2 && changes.All(_r => shots.Any(_s => (string)_s["payload"]["shotId"] == (string)_r["payload"]["shotId"])),
                "Consumption reuses canonical shot IDs");
            Check((int)changes[0]["payload"]["consumed"] == 0 && (int)changes[1]["payload"]["consumed"] == 1, "Saved shot is an observed zero");
        }
    }

    private static void MovesAndCapacity()
    {
        using (var rig = new Rig())
        {
            rig.rightAmmo = 10;
            rig.Begin();
            int initial = rig.Total;
            string identity = rig.session.GetWeaponId(rig.right);
            rig.log.Weapon("weapon_drop", rig.right, rig.ammoId, rig.rightAmmo);
            rig.floor = true;
            Check(rig.Total == initial && rig.Snapshot().weapons[0].location == "floor", "Floor retains ammunition in conservation scope");
            rig.log.Weapon("weapon_pickup", rig.right, rig.ammoId, rig.rightAmmo);
            rig.floor = false;
            Check(rig.session.GetWeaponId(rig.right) == identity, "Floor round trip retains instance identity");
            int current = rig.inventory.GetReserve(rig.ammoId);
            Check(rig.inventory.SetMaximum(rig.ammoId, current + 2, false), "Set near-full capacity");
            string cause = rig.log.NewCauseId();
            int returned = rig.inventory.Add(rig.ammoId, 5, "capacity_return", cause);
            rig.rightAmmo -= 5;
            rig.log.Shrink(rig.right, rig.ammoId, 5, returned, cause);
            Check(returned == 2 && rig.Total == initial - 3, "Capacity shrink returns actual two and discards actual three");
            int before = rig.inventory.GetReserve(rig.ammoId);
            Check(!rig.inventory.SetMaximum(rig.ammoId, before - 1, false), "Refused cap shrink does not mutate inventory");
            Check(rig.inventory.SetMaximum(rig.ammoId, before - 1, true), "Explicit cap discard observed");
            rig.leftAmmo += 6;
            rig.log.Weapon("new_weapon", rig.left, rig.ammoId, 6);
            rig.life.CompleteWave(null, null, 0, 0);
            var totals = rig.Totals();
            Check((long)totals["acquiredBySource"]["new_weapon"] == 6 && totals["acquiredBySource"]["capacity_return"] == null,
                "Only newly created loaded rounds are acquisition");
            Check((long)totals["discarded"] == 4 && (long)totals["moved"] == 20, "Discard and round-trip movement separate");
            Check(rig.Total == initial + 6 - 4, "Reserve plus carried/floor loaded conservation");
            var pair = rig.Rows().Where(_r => (string)_r["payload"]?["causeId"] == cause).ToArray();
            Check(pair.Length == 2, "Return and shrink share one cause without counting a grant twice");
            rig.life.End("return_to_title", null, null);
            File.WriteAllLines("Docs/로그-시스템-5차.synthetic.jsonl", rig.store.Lines);
        }
    }

    private static void ShortagesAndBoundaries()
    {
        using (var rig = new Rig())
        {
            rig.Begin();
            for (int i = 0; i < 100; i++)
            {
                rig.log.FireInput(true);
                rig.log.EmptyFire(rig.ammoId, true, true);
            }
            rig.log.FireInput(false);
            rig.log.EmptyFire(rig.ammoId, true, false);
            rig.log.EmptyFire(rig.ammoId, true, true);
            rig.log.EmptyFire(rig.ammoId, false, true);
            rig.log.EmptyFire(rig.ammoId, false, true);
            rig.log.Shortage(rig.ammoId, "reload");
            rig.log.Shortage(rig.ammoId, "reload");
            rig.life.CompleteWave(null, null, 0, 0);
            Check((int)rig.Totals()["emptyFireAttempts"] == 4, "Hold once, release retries, semiauto each attempt");
            Check((int)rig.Totals()["emptyReloadAttempts"] == 2, "Reload counted per explicit attempt");
            rig.inventory.Add(rig.ammoId, 5, "slot_reserve", "late-spin");
            rig.inventory.Add(rig.ammoId, 5, "shop_pack", "purchase");
            Check(rig.Totals()["acquiredBySource"]["slot_reserve"] == null, "Shop and late slot do not revise closed wave");
            rig.Begin(2);
            int startReserve = rig.inventory.GetReserve(rig.ammoId);
            rig.life.End("death", null, null, true);
            Check((int)rig.Summary(1)["start"]["reserves"][0]["reserve"] == startReserve, "Next wave begins with actual post-shop stock");
            Check((int)rig.Totals(1)["emptyFireAttempts"] == 0, "New wave resets shortage totals");
            var summary = rig.Rows().Last(_r => (string)_r["eventType"] == "wave_summary");
            Check((string)summary["payload"]["status"] == "interrupted" &&
                (string)summary["payload"]["summaries"]["ammo"]["collectionStatus"] == "collected", "Death closes ammunition summary");
            long sequence = rig.session.Sequence;
            rig.inventory.Add(rig.ammoId, 1, "drop");
            Check(rig.session.Sequence == sequence, "Ended session rejects late ammunition events");
        }
    }

    private static void InitialAndFloor()
    {
        using (var rig = new Rig())
        {
            rig.rightAmmo = 4;
            rig.log.Weapon("initial_weapon", rig.right, rig.ammoId, 4);
            rig.Begin();
            rig.leftAmmo += 3;
            rig.log.Weapon("world_weapon", rig.left, rig.ammoId, 3);
            rig.log.Weapon("weapon_drop", rig.right, rig.ammoId, rig.rightAmmo);
            rig.floor = true;
            rig.life.CompleteWave(null, null, 0, 0);
            Check(rig.Totals()["acquiredBySource"]["initial_weapon"] == null, "Initial grant is baseline rather than wave acquisition");
            Check((long)rig.Totals()["acquiredBySource"]["world_weapon"] == 3, "Materialized world weapon counts new loaded rounds");
            Check((string)rig.Summary()["end"]["weapons"][0]["location"] == "floor" &&
                (int)rig.Summary()["end"]["weapons"][0]["loadedAmmo"] == 4, "Wave ending includes floor ammunition");
            Check((string)rig.Summary()["start"]["weapons"][0]["hand"] == "right" &&
                rig.Summary()["end"]["weapons"][0]["hand"].Type == JTokenType.Null, "Floor has no fabricated active hand");
        }
    }

    private static void Failures()
    {
        LogData disabled = LogInfo.Copy(SETTINGS);
        disabled.enabled = false;
        using (var rig = new Rig(disabled))
        {
            rig.Begin();
            Check(rig.inventory.Add(rig.ammoId, 1, "drop") == 1 && rig.session.Sequence == 0, "Disabled logger preserves gameplay");
        }
        var session = new LogSession(SETTINGS, _id => throw new IOException("synthetic failure"));
        var life = new LogGameLifecycle(session);
        life.Start("synthetic", null, null, null);
        var inventory = new AmmoInventory(TABLE) { AmmoLog = new LogAmmoCollector(life, () => null) };
        Check(inventory.Add(TABLE.ammoTypes[0].id, 1, "drop") == 1 && session.Diagnostics.StorageFailed, "Storage failure preserves ammunition");
        life.End("unknown", null, null);
        var healthySession = new LogSession(SETTINGS, _id => new MemoryLogStore(100));
        var healthyLife = new LogGameLifecycle(healthySession);
        healthyLife.RegisterCollector(new LogAmmoCollector(healthyLife, () => throw new IOException("synthetic snapshot failure")));
        healthyLife.Start("synthetic", null, null, null);
        healthyLife.BeginWave(new LogWavePlan { waveNumber = 1, durationSeconds = 1 }, null);
        healthyLife.End("death", null, null, true);
        Check(!healthyLife.IsActive && healthySession.Diagnostics.FailureCount > 0, "Failed ammunition snapshot does not prevent death/session end");
    }

    private static void SourceIntegration()
    {
        string player = File.ReadAllText("POC2/Assets/Scripts/Player/PlayerWeapon.cs");
        string manager = File.ReadAllText("POC2/Assets/Scripts/Game/GameManager.cs");
        string logging = File.ReadAllText("POC2/Assets/Scripts/Game/GameManager.Logging.cs");
        Check(player.Contains("AmmoLog?.Shot(logShot, firingAmmoId, ammoBeforeShot, ammoAfterShot)"), "Canonical shot context and original ammo type hooked");
        string takeShot = player.Substring(player.IndexOf("private int TakeShot"), player.IndexOf("private void TryFire") - player.IndexOf("private int TakeShot"));
        Check(takeShot.IndexOf("int consumed =") < takeShot.IndexOf("AmmoChanged?.Invoke") && takeShot.Contains("return consumed;"),
            "Actual consumption captured before reentrant HUD callbacks");
        Check(player.Contains("AmmoLog?.Reload(Hand(hand), AmmoId, hand, 1)") && player.Contains("AmmoLog?.Reload(Hand(_hand), AmmoId, _hand, added)"), "Per-round and full reload hooks");
        Check(player.Contains("slot_loaded") && player.Contains("offHand.Magazine - beforeLeft"), "Slot grant observes actual left hand");
        Check(player.IndexOf("if (isReloading || aimCamera == null)") < player.IndexOf("AmmoLog?.EmptyFire"), "Ongoing reload excluded from empty fire");
        Check(player.Contains("empty &= HandAmmo(i) <= 0") && player.Contains("candidate.shotsPerSecond > 0f"), "Other available hands and invalid fire settings excluded");
        Check(manager.Contains("ammo.Add(_pickup.AmmoId, _pickup.Amount, \"drop\")"), "Drop actual mutation tagged");
        Check(manager.Contains("\"capacity_return\", causeId") && manager.Contains("logAmmo?.Shrink"), "Part capacity return and loss tagged");
        Check(logging.Contains("telemetry.RegisterCollector(logAmmo)") && logging.Contains("pickup.IsConsumed"), "Collector registered and consumed floor objects excluded");
        Check(logging.Contains("ReferenceEquals(ammo.AmmoLog, logAmmo)") && logging.Contains("ReferenceEquals(weapon.AmmoLog, logAmmo)"), "Observers detached by owner");
        Check(File.ReadAllText("POC2/Assets/Scripts/Model/AmmoShop.cs").Contains("\"shop_pack\"") &&
            File.ReadAllText("POC2/Assets/Scripts/Info/SlotRewardInfo.cs").Contains("\"slot_reserve\""), "Single mutation owner for shop and slot reserve");
        Check(File.Exists("POC2/Assets/Scripts/Logging/LogAmmoCollector.cs.meta"), "New Unity source has meta");
    }
}
