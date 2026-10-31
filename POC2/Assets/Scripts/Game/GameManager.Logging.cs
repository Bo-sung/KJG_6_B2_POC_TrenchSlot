using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GameManager
{
    private LogGameLifecycle telemetry;
    private LogGameplayEvents gameplayLog;
    private LogFixedTickCollector logFixedTicks;
    private LogCombatCollector logCombat;
    private LogAmmoCollector logAmmo;
    private LogSlotCollector logSlots;
    private bool loggingInitialized;
    private float? logDeathRadius;

    public LogGameLifecycle Telemetry => telemetry;
    public LogGameplayEvents GameplayLog => gameplayLog;
    public LogSlotCollector SlotLog => logSlots;

    private void InitializeLogging()
    {
        if (loggingInitialized || !Application.isPlaying || gameObject.scene.name != SceneNames.GAME)
        {
            return;
        }
        loggingInitialized = true;
        LogSession session = null;
        try
        {
            var table = TableManager.Instance;
            if (LogInfo.TryValidate(table.Log, out _))
            {
                logDeathRadius = table.Log.deathEnemyRadius;
            }
            session = LogUnityAdapter.CreateSession();
            telemetry = new LogGameLifecycle(session);
            gameplayLog = new LogGameplayEvents(telemetry);
            logFixedTicks = new LogFixedTickCollector(telemetry);
            logCombat = new LogCombatCollector(telemetry);
            logAmmo = new LogAmmoCollector(telemetry, CaptureLogAmmo);
            logSlots = new LogSlotCollector(telemetry);
            logShop = new LogShopCollector(telemetry);
            logAmmo.Observed += logSlots.ObserveAmmo;
            logAmmo.Observed += logShop.ObserveAmmo;
            telemetry.RegisterCollector(logCombat);
            telemetry.RegisterCollector(logAmmo);
            string fingerprint = null;
            try
            {
                fingerprint = LogGameLifecycle.ComputeDataFingerprint(new
                {
                    table.SlotMachine,
                    table.Weapon,
                    table.Part,
                    table.Ammo,
                    table.Enemy,
                    table.EnemyWeapon,
                    table.EnemyAi,
                    table.Wave,
                    table.Shop,
                    table.Perk,
                    table.GameConfig,
                    table.WeaponAim,
                    table.Log
                });
            }
            catch (Exception exception)
            {
                session.ReportCollectionFailure(exception);
            }
            telemetry.Start(Application.version, Application.buildGUID, fingerprint, gameObject.scene.path);
            if (weapon != null)
            {
                weapon.CombatLog = logCombat;
                weapon.AmmoLog = logAmmo;
            }
            if (playerHealth != null)
            {
                playerHealth.CombatLog = logCombat;
            }
            if (ammo != null)
            {
                ammo.AmmoLog = logAmmo;
            }
            if (loadout != null)
            {
                for (int i = 0; i < loadout.SlotCount; i++)
                {
                    ObserveNewLogWeapon(loadout.Get(i), "initial_weapon");
                }
            }
            SynchronizeCombatEquipment();
        }
        catch (Exception exception)
        {
            if (session != null)
            {
                session.ReportCollectionFailure(exception);
                session.Dispose();
            }
            else
            {
                Debug.LogWarning("Telemetry initialization failed: " + exception.Message);
            }
        }
    }

    private void FixedUpdate()
    {
        ObserveLogging(() => telemetry.AdvanceFixedUpdate(Time.fixedDeltaTime));
        ObserveLogging(CaptureLogFixedTick);
    }

    private void CaptureLogFixedTick()
    {
        bool controllable = phase == GamePhase.Wave && playerHealth != null && playerHealth.IsAlive &&
            movement != null && movement.IsControlEnabled;
        if (logFixedTicks == null || !logFixedTicks.CanRecord(controllable))
        {
            return;
        }
        Vector3 position = playerHealth.transform.position;
        Camera camera = weapon != null ? weapon.AimCamera : null;
        Vector3? angles = camera != null ? camera.transform.eulerAngles : (Vector3?)null;
        var held = new List<LogHeldWeaponSnapshot>();
        if (weapon != null)
        {
            for (int hand = 0; hand < weapon.HandCount; hand++)
            {
                WeaponInstance instance = weapon.Hand(hand);
                if (instance == null)
                {
                    continue;
                }
                int slot = loadout != null ? loadout.IndexOf(instance) : -1;
                held.Add(new LogHeldWeaponSnapshot
                {
                    instanceId = telemetry.Session.GetWeaponId(instance),
                    weaponTypeId = instance.WeaponId,
                    hand = hand == 0 ? "right" : "left",
                    slot = slot >= 0 ? slot : (int?)null
                });
            }
        }
        var player = new LogPlayerTickSnapshot
        {
            x = position.x,
            z = position.z,
            yaw = angles.HasValue ? angles.Value.y : (double?)null,
            pitch = angles.HasValue ? angles.Value.x : (double?)null,
            activeHand = held.Count > 1 ? "both" : held.Count == 1 ? held[0].hand : null,
            activeSlot = loadout != null ? loadout.ActiveIndex : (int?)null,
            weapons = weapon != null ? held.ToArray() : null
        };
        LogEnemySnapshot enemies = waves != null ? waves.CaptureAliveSnapshot(null, null, null) : null;
        logFixedTicks.Record(controllable, player, enemies);
    }

    private void OnApplicationQuit()
    {
        EndLogging("application_quit");
    }

    private void SynchronizeCombatEquipment()
    {
        if (logCombat == null || weapon == null)
        {
            return;
        }
        var held = new List<LogHeldWeaponSnapshot>();
        for (int hand = 0; hand < weapon.HandCount; hand++)
        {
            WeaponInstance instance = weapon.Hand(hand);
            if (instance == null)
            {
                continue;
            }
            int slot = loadout != null ? loadout.IndexOf(instance) : -1;
            held.Add(new LogHeldWeaponSnapshot
            {
                instanceId = telemetry.Session.GetWeaponId(instance),
                weaponTypeId = instance.WeaponId,
                hand = hand == 0 ? "right" : "left",
                slot = slot >= 0 ? slot : (int?)null
            });
        }
        logCombat.SetEquipped(held);
    }

    private void DetachCombatLogging()
    {
        if (logAmmo != null && logShop != null)
        {
            logAmmo.Observed -= logShop.ObserveAmmo;
        }
        if (logAmmo != null && logSlots != null)
        {
            logAmmo.Observed -= logSlots.ObserveAmmo;
        }
        if (ammo != null && ReferenceEquals(ammo.AmmoLog, logAmmo))
        {
            ammo.AmmoLog = null;
        }
        if (weapon != null && ReferenceEquals(weapon.AmmoLog, logAmmo))
        {
            weapon.AmmoLog = null;
        }
        if (weapon != null && ReferenceEquals(weapon.CombatLog, logCombat))
        {
            weapon.CombatLog = null;
        }
        if (playerHealth != null && ReferenceEquals(playerHealth.CombatLog, logCombat))
        {
            playerHealth.CombatLog = null;
        }
    }

    public void PrepareForTitleReturn()
    {
        EndLogging("return_to_title");
    }

    private void EndLogging(string _reason, bool _death = false)
    {
        if (telemetry == null || !telemetry.IsActive)
        {
            return;
        }
        LogPlayerSnapshot player = null;
        LogEnemySnapshot enemies = null;
        ObserveLogging(() => player = CaptureLogPlayer());
        ObserveLogging(() => enemies = CaptureLogEnemies());
        ObserveShopSessionEnd(_death ? "death" : _reason);
        ObserveLogging(() => telemetry.End(_reason, player, enemies, _death));
    }

    private void ObserveLogging(Action _observation)
    {
        if (telemetry == null || !telemetry.IsActive)
        {
            return;
        }
        try
        {
            _observation();
        }
        catch (Exception exception)
        {
            telemetry.Session.ReportCollectionFailure(exception);
        }
    }

    private LogPlayerSnapshot CaptureLogPlayer()
    {
        var snapshot = new LogPlayerSnapshot
        {
            health = playerHealth != null ? playerHealth.CurrentHealth : (double?)null,
            money = wallet != null ? wallet.Balance : (int?)null,
            activeSlot = loadout != null ? loadout.ActiveIndex : (int?)null,
            position = playerHealth != null ? CopyLogPosition(playerHealth.transform.position) : null
        };
        WeaponInstance right = weapon != null ? weapon.Hand(0) : null;
        WeaponInstance left = weapon != null ? weapon.Hand(1) : null;
        snapshot.activeHand = left != null ? "both" : right != null ? "right" : null;
        if (loadout != null)
        {
            var owned = new List<LogWeaponSnapshot>();
            for (int i = 0; i < loadout.SlotCount; i++)
            {
                WeaponInstance instance = loadout.Get(i);
                if (instance == null)
                {
                    continue;
                }
                var entry = WeaponInfo.Find(TableManager.Instance.Weapon, instance.WeaponId);
                owned.Add(new LogWeaponSnapshot
                {
                    instanceId = telemetry.Session.GetWeaponId(instance),
                    weaponTypeId = instance.WeaponId,
                    ammoTypeId = entry != null ? entry.ammoId : null,
                    hand = instance == right ? "right" : instance == left ? "left" : null,
                    slot = i,
                    active = instance == right || instance == left,
                    loadedAmmo = instance.Magazine,
                    partIds = new List<string>(instance.PartIds).ToArray()
                });
            }
            snapshot.weapons = owned.ToArray();
        }
        var ammoTable = TableManager.Instance.Ammo;
        if (ammo != null && ammoTable?.ammoTypes != null)
        {
            var reserves = new List<LogReserveSnapshot>();
            foreach (var entry in ammoTable.ammoTypes)
            {
                if (entry == null || !ammo.Contains(entry.id))
                {
                    continue;
                }
                reserves.Add(new LogReserveSnapshot
                {
                    ammoTypeId = entry.id,
                    reserve = ammo.GetReserve(entry.id),
                    maximum = ammo.GetMaximum(entry.id)
                });
            }
            snapshot.reserves = reserves.ToArray();
        }
        return snapshot;
    }

    public LogSlotState CaptureSlotLogState()
    {
        LogSlotState snapshot = null;
        ObserveLogging(() => snapshot = new LogSlotState
        {
            waveNumber = telemetry.Session.WaveNumber,
            phase = telemetry.Session.Phase,
            elapsedSeconds = telemetry.Session.ElapsedSeconds,
            player = CaptureLogPlayer(),
            enemies = CaptureLogEnemies()
        });
        return snapshot;
    }

    private LogAmmoSnapshot CaptureLogAmmo()
    {
        LogPlayerSnapshot player = CaptureLogPlayer();
        var entries = new List<LogAmmoWeaponSnapshot>();
        if (player.weapons != null)
        {
            foreach (LogWeaponSnapshot held in player.weapons)
            {
                entries.Add(new LogAmmoWeaponSnapshot { instanceId = held.instanceId, weaponTypeId = held.weaponTypeId,
                    ammoTypeId = held.ammoTypeId, location = "carried", hand = held.hand, slot = held.slot, loadedAmmo = held.loadedAmmo });
            }
        }
        Transform root = GroundRoot();
        for (int i = 0; i < root.childCount; i++)
        {
            WeaponPickup pickup = root.GetChild(i).GetComponent<WeaponPickup>();
            if (pickup == null || pickup.IsConsumed)
            {
                continue;
            }
            WeaponInstance instance = pickup.Weapon;
            var entry = WeaponInfo.Find(TableManager.Instance.Weapon, pickup.WeaponId);
            entries.Add(new LogAmmoWeaponSnapshot { instanceId = telemetry.Session.GetWeaponId(instance),
                weaponTypeId = pickup.WeaponId, ammoTypeId = entry?.ammoId, location = "floor",
                loadedAmmo = instance != null ? instance.Magazine : (int?)null });
        }
        return new LogAmmoSnapshot { reserves = player.reserves, weapons = entries.ToArray() };
    }

    private void ObserveNewLogWeapon(WeaponInstance _weapon, string _source, string _causeId = null)
    {
        if (_weapon == null)
        {
            return;
        }
        var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weapon.WeaponId);
        logAmmo?.Weapon(_source, _weapon, entry?.ammoId, _weapon.Magazine, null, _causeId);
    }

    private void ObserveLogWeaponMove(WeaponInstance _weapon, string _source)
    {
        if (_weapon == null)
        {
            return;
        }
        var entry = WeaponInfo.Find(TableManager.Instance.Weapon, _weapon.WeaponId);
        logAmmo?.Weapon(_source, _weapon, entry?.ammoId, _weapon.Magazine);
    }

    private LogWavePlan CaptureLogPlan()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        AddPlannedKind(counts, EnemyInfo.RUSHER_ID, currentWave.rushers);
        AddPlannedKind(counts, EnemyInfo.RIFLEMAN_ID, currentWave.riflemen);
        AddPlannedKind(counts, EnemyInfo.HEAVY_ID, currentWave.heavies);
        return new LogWavePlan
        {
            waveNumber = currentWave.wave,
            durationSeconds = currentWave.durationSeconds,
            plannedEnemies = counts,
            eraId = currentWave.era,
            enemyWeaponId = currentWave.enemyWeapon
        };
    }

    private LogEnemySnapshot CaptureLogEnemies()
    {
        return waves != null ? waves.CaptureAliveSnapshot(
            playerHealth != null ? playerHealth.transform.position : (Vector3?)null,
            logDeathRadius, CaptureLogPlan().plannedEnemies.Keys) : null;
    }

    private static void AddPlannedKind(Dictionary<string, int> _counts, string _kind, int _count)
    {
        if (_count > 0)
        {
            _counts.Add(_kind, _count);
        }
    }

    private static LogPosition3 CopyLogPosition(Vector3 _position)
    {
        return new LogPosition3 { x = _position.x, y = _position.y, z = _position.z };
    }

    private static string LogPhase(GamePhase _phase)
    {
        switch (_phase)
        {
            case GamePhase.Wave:
                return "wave";
            case GamePhase.Shop:
                return "shop";
            case GamePhase.GameOver:
                return "game_over";
            default:
                return "unknown";
        }
    }
}
