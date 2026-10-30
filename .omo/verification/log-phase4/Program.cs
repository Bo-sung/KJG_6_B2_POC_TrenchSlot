using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private static int CHECKS;

    private sealed class Clock : ILogUtcClock
    {
        public DateTimeOffset UtcNow => new DateTimeOffset(2026, 10, 31, 2, 0, 0, TimeSpan.FromHours(9));
    }

    private sealed class Rig : IDisposable
    {
        public readonly MemoryLogStore store = new MemoryLogStore(5000);
        public readonly LogSession session;
        public readonly LogGameLifecycle life;
        public readonly LogCombatCollector combat;
        public readonly object right = new object();
        public readonly object left = new object();
        public readonly object other = new object();

        public Rig(LogData _settings)
        {
            session = new LogSession(_settings, _id => store, new Clock(), _initialFixedDeltaTime: 0.02);
            life = new LogGameLifecycle(session);
            combat = new LogCombatCollector(life);
            life.RegisterCollector(combat);
            life.Start("synthetic-build", null, null, "synthetic-map");
        }

        public LogHeldWeaponSnapshot Held(object _weapon, string _hand, string _type = "synthetic-pistol")
        {
            return new LogHeldWeaponSnapshot { instanceId = session.GetWeaponId(_weapon), weaponTypeId = _type, hand = _hand, slot = _hand == "right" ? 0 : 1 };
        }

        public void Begin(int _wave)
        {
            life.BeginWave(new LogWavePlan { waveNumber = _wave, durationSeconds = 40, plannedEnemies = new Dictionary<string, int> { ["synthetic-enemy"] = 2 } }, null);
        }

        public LogShotContext Fire(object _weapon, int _hand, int _pellets, out string[] _ids, string _type = "synthetic-pistol", bool _create = true)
        {
            var shot = combat.StartShot(_weapon, _type, _hand, new LogPosition3 { x = 1, y = 2, z = 3 }, _pellets);
            var ids = new List<string>();
            for (int i = 0; i < _pellets; i++)
            {
                string id = shot.NewProjectileId();
                shot.ProjectileCreated(id, _create);
                ids.Add(id);
            }
            shot.FinalizeShot();
            _ids = ids.ToArray();
            return shot;
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

        public JsonElement Summary(int _index = 0)
        {
            return Events("wave_summary")[_index].GetProperty("payload").GetProperty("summaries").GetProperty("combat").GetProperty("data");
        }

        public void Dispose()
        {
            life.End("unknown", null, null);
            session.Dispose();
        }
    }

    private static void Main(string[] _args)
    {
        string root = Path.GetFullPath(_args.Length > 0 ? _args[0] : ".");
        LogData settings = Newtonsoft.Json.JsonConvert.DeserializeObject<LogData>(File.ReadAllText(Path.Combine(root, "POC2/Assets/Resources/LogData.json")));
        ShotsAndPellets(settings);
        EquipmentAndAkimbo(settings);
        DamageAndDeath(settings);
        LateResults(settings);
        FailureAndCancellation(settings);
        StaticIntegration(root);
        Sample(settings, root);
        Console.WriteLine("PASS: " + CHECKS + " phase-4 non-Unity checks; no Unity API executed.");
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

    private static void Hit(LogShotContext _shot, string _id, string _target, bool _killed, double _damage = 2)
    {
        _shot.Impact(new LogCombatImpact { projectileId = _id, targetId = _target, enemyTypeId = "synthetic-enemy", hitEnemy = true, killed = _killed, actualDamage = _damage,
            point = new LogPosition3 { x = 5, y = 1, z = 6 } });
    }

    private static void ShotsAndPellets(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            rig.combat.SetEquipped(new[] { rig.Held(rig.right, "right") });
            rig.Begin(1);
            var single = rig.Fire(rig.right, 0, 1, out string[] singleIds);
            var shotgun = rig.Fire(rig.right, 0, 4, out string[] pelletIds);
            Check(single.ShotId != shotgun.ShotId && single.ShotId.Contains(":shot:") && singleIds[0].Contains(":projectile:"), "single and pellet shots have distinct IDs and separate projectile namespace");
            Check(pelletIds.Distinct().Count() == 4 && !pelletIds.Contains(singleIds[0]), "each pellet gets a unique projectile ID");
            single.ProjectileEnded(singleIds[0], "miss");
            rig.life.AdvanceFixedUpdate(1);
            rig.combat.SetEquipped(new[] { rig.Held(rig.other, "right", "synthetic-rifle") });
            Hit(shotgun, pelletIds[0], "target-a", false, 3);
            Hit(shotgun, pelletIds[1], "target-a", true, 2);
            Hit(shotgun, pelletIds[2], "target-b", true, 1);
            Hit(shotgun, pelletIds[3], "target-a", true, 0);
            Hit(shotgun, pelletIds[3], "target-a", true, 0);
            foreach (string id in pelletIds)
            {
                shotgun.ProjectileEnded(id, "impact");
            }
            shotgun.FinalizeShot();
            rig.life.AdvanceFixedUpdate(1);
            rig.life.CompleteWave(null, null, 0, 0);
            JsonElement[] shots = rig.Events("player_shot");
            Check(shots.Length == 2, "one player_shot per firing success, not per pellet or legacy event");
            Check(shots[0].GetProperty("payload").GetProperty("origin").GetProperty("x").GetDouble() == 1 &&
                shots[1].GetProperty("payload").GetProperty("createdProjectileCount").GetInt32() == 4, "actual origin and created pellet count recorded");
            Check(rig.Events("projectile_impact").Length == 4, "duplicate impact callback ignored");
            JsonElement pistol = rig.Summary().GetProperty("weapons").EnumerateArray().Single(_item => _item.GetProperty("instanceId").GetString() == single.WeaponInstanceId);
            Check(pistol.GetProperty("shots").GetInt32() == 2 && pistol.GetProperty("projectiles").GetInt32() == 5, "shots and actual projectile counts are independent");
            Check(pistol.GetProperty("hitShots").GetInt32() == 1 && pistol.GetProperty("hitProjectiles").GetInt32() == 4, "multiple shotgun targets count as one successful shot and four hit projectiles");
            Check(pistol.GetProperty("kills").GetInt32() == 2 && pistol.GetProperty("actualDamageDealt").GetDouble() == 6, "target kill dedup and contact versus actual damage remain distinct");
            JsonElement rifle = rig.Summary().GetProperty("weapons").EnumerateArray().Single(_item => _item.GetProperty("weaponTypeId").GetString() == "synthetic-rifle");
            Check(rifle.GetProperty("shots").GetInt32() == 0 && pistol.GetProperty("equippedSeconds").GetDouble() == 1, "in-flight weapon change does not misattribute projectile results");
        }
        using (var rig = new Rig(_settings))
        {
            rig.Begin(1);
            var origin = new LogPosition3 { x = 7, y = 8, z = 9 };
            var shot = rig.combat.StartShot(rig.right, "synthetic-pistol", 0, origin, 1);
            origin.x = 999;
            string id = shot.NewProjectileId();
            shot.ProjectileCreated(id, true);
            shot.FinalizeShot();
            Check(shot.Origin.x == 7 && rig.Events("player_shot").Single().GetProperty("payload").GetProperty("origin").GetProperty("x").GetDouble() == 7,
                "fire origin is a value snapshot");
        }
    }

    private static void EquipmentAndAkimbo(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            LogHeldWeaponSnapshot[] both = { rig.Held(rig.right, "right"), rig.Held(rig.left, "left") };
            rig.combat.SetEquipped(both);
            rig.Begin(1);
            var right = rig.Fire(rig.right, 0, 1, out string[] rightIds);
            var left = rig.Fire(rig.left, 1, 1, out string[] leftIds);
            Check(right.Hand == "right" && left.Hand == "left" && right.WeaponInstanceId != left.WeaponInstanceId, "akimbo retains separate firing hands and instances");
            rig.life.AdvanceFixedUpdate(1);
            int events = rig.Events("weapon_equipped").Length;
            for (int i = 0; i < 5; i++)
            {
                rig.combat.SetEquipped(both);
            }
            Check(rig.Events("weapon_equipped").Length == events, "identical refresh is not an equipment transition");
            rig.life.AdvanceFixedUpdate(2);
            rig.combat.SetEquipped(new[] { rig.Held(rig.right, "right") });
            rig.life.AdvanceFixedUpdate(2);
            right.ProjectileEnded(rightIds[0], "miss");
            left.ProjectileEnded(leftIds[0], "miss");
            rig.life.CompleteWave(null, null, 0, 0);
            JsonElement[] weapons = rig.Summary().GetProperty("weapons").EnumerateArray().ToArray();
            Check(weapons.Single(_w => _w.GetProperty("instanceId").GetString() == right.WeaponInstanceId).GetProperty("equippedSeconds").GetDouble() == 5 &&
                weapons.Single(_w => _w.GetProperty("instanceId").GetString() == left.WeaponInstanceId).GetProperty("equippedSeconds").GetDouble() == 3,
                "each active instance closes its own exact equipment duration");
            JsonElement type = rig.Summary().GetProperty("weaponTypes").EnumerateArray().Single();
            Check(type.GetProperty("equippedSeconds").GetDouble() == 8 && type.GetProperty("shots").GetInt32() == 2, "same-type simultaneous equipment sum may exceed five-second wave duration");
            rig.life.SetPhase("shop");
            rig.life.AdvanceFixedUpdate(10);
            rig.combat.SetEquipped(new[] { rig.Held(rig.other, "right", "synthetic-rifle") });
            rig.Begin(2);
            rig.life.AdvanceFixedUpdate(1);
            rig.life.End("death", null, null, true);
            JsonElement next = rig.Summary(1).GetProperty("weapons").EnumerateArray().Single();
            Check(next.GetProperty("weaponTypeId").GetString() == "synthetic-rifle" && next.GetProperty("equippedSeconds").GetDouble() == 1,
                "shop and carried inactive weapons do not accrue wave active time");
        }
    }

    private static void DamageAndDeath(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            rig.Begin(1);
            var position = new LogPosition3 { x = 4, y = 5, z = 6 };
            var source = new LogAttackSource("synthetic-rifleman", "synthetic-rifle", position);
            position.x = 999;
            rig.combat.PlayerDamaged(source, 20, 18);
            rig.combat.PlayerDamaged(null, 18, 13);
            rig.combat.PlayerDamaged(source, 13, 13);
            var lethal = new LogAttackSource("synthetic-rusher", "unarmed", new LogPosition3 { x = 1, y = 2, z = 3 });
            rig.combat.PlayerDamaged(lethal, 13, 0);
            rig.life.End("death", null, null, true);
            JsonElement[] damage = rig.Events("player_damage");
            Check(damage.Length == 3, "only actual health loss counts a hit; fully mitigated input does not");
            Check(damage[0].GetProperty("payload").GetProperty("actualDamage").GetDouble() == 2 &&
                damage[0].GetProperty("payload").GetProperty("attackPosition").GetProperty("x").GetDouble() == 4,
                "armor-applied health delta and immutable pre-destruction attack position recorded");
            Check(damage[1].GetProperty("payload").GetProperty("enemyTypeId").GetString() == "unknown", "legacy unsourced damage remains unknown");
            Check(lethal.Damage(3, 0).actualDamage == 3, "overkill is capped by actual remaining health loss");
            JsonElement last = rig.Events("death_snapshot").Single().GetProperty("payload").GetProperty("lastDamage");
            Check(last.GetProperty("enemyTypeId").GetString() == "synthetic-rusher" && last.GetProperty("enemyWeaponId").GetString() == "unarmed" &&
                last.GetProperty("actualDamage").GetDouble() == 13, "last lethal source reaches death snapshot before closure");
            JsonElement[] totals = rig.Summary().GetProperty("damageReceived").EnumerateArray().ToArray();
            Check(totals.Sum(_t => _t.GetProperty("actualDamage").GetDouble()) == 20 && totals.Sum(_t => _t.GetProperty("hits").GetInt32()) == 3,
                "interrupted summary includes actual damage and hits by source including fatal hit");
            Check(rig.Rows().Select(_r => _r.GetProperty("eventType").GetString()).TakeLast(4).SequenceEqual(new[] { "player_damage", "death_snapshot", "wave_summary", "session_end" }),
                "fatal damage precedes death snapshot and interrupted summary");
        }
    }

    private static void LateResults(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            rig.Begin(1);
            var shot = rig.Fire(rig.right, 0, 2, out string[] ids);
            rig.life.CompleteWave(null, null, 0, 0);
            Check(rig.Summary().GetProperty("weapons")[0].GetProperty("hitProjectiles").GetInt32() == 0, "original closed summary excludes not-yet-observed impacts");
            rig.life.SetPhase("shop");
            Hit(shot, ids[0], "late-a", true);
            shot.ProjectileEnded(ids[0], "impact");
            rig.Begin(2);
            Hit(shot, ids[1], "late-b", true);
            shot.ProjectileEnded(ids[1], "impact");
            JsonElement[] corrections = rig.Events("wave_summary_correction");
            Check(corrections.Length == 2 && corrections[0].GetProperty("payload").GetProperty("revision").GetInt32() == 1 &&
                corrections[1].GetProperty("payload").GetProperty("revision").GetInt32() == 2, "late impacts append increasing corrections");
            string summaryId = rig.Events("wave_summary")[0].GetProperty("payload").GetProperty("summaryId").GetString();
            Check(corrections.All(_c => _c.GetProperty("payload").GetProperty("summaryId").GetString() == summaryId), "corrections keep original summary key");
            Check(corrections[0].GetProperty("phase").GetString() == "shop" && corrections[1].GetProperty("waveNumber").GetInt32() == 2,
                "correction envelope records actual occurrence phase and wave");
            JsonElement corrected = corrections[1].GetProperty("payload").GetProperty("summaries").GetProperty("combat").GetProperty("data").GetProperty("weapons")[0];
            Check(corrected.GetProperty("hitShots").GetInt32() == 1 && corrected.GetProperty("hitProjectiles").GetInt32() == 2 && corrected.GetProperty("kills").GetInt32() == 2,
                "latest full correction contains original-weapon cumulative results");
            Check(rig.Summary().GetProperty("weapons")[0].GetProperty("kills").GetInt32() == 0, "original summary is not overwritten");
            Check(shot.wave.closedSummary == null, "retained closed payload released after last in-flight projectile ends");
            var pending = rig.Fire(rig.other, 0, 1, out string[] pendingIds, "synthetic-rifle");
            rig.life.End("unknown", null, null);
            JsonElement next = rig.Summary(1);
            Check(next.GetProperty("weapons").EnumerateArray().Sum(_w => _w.GetProperty("hitProjectiles").GetInt32()) == 0, "old-wave hits do not contaminate current-wave aggregate");
            int rows = rig.Rows().Length;
            Hit(pending, pendingIds[0], "after-end", true);
            Check(rig.Rows().Length == rows, "closed session rejects late projectile callbacks");
            pending.ProjectileEnded(pendingIds[0], "expired_after_session");
            Check(pending.wave.closedSummary == null, "post-session projectile cleanup releases retained summary without logging");
            using (var fresh = new Rig(_settings))
            {
                fresh.Begin(1);
                Check(fresh.session.SessionId != rig.session.SessionId && fresh.Events("projectile_impact").Length == 0, "old context never routes into restarted session");
            }
        }
    }

    private static void FailureAndCancellation(LogData _settings)
    {
        using (var rig = new Rig(_settings))
        {
            rig.Begin(1);
            rig.Fire(rig.right, 0, 2, out _, _create: false);
            var cancelled = rig.Fire(rig.left, 1, 1, out string[] ids);
            cancelled.ProjectileEnded(ids[0], "cancelled");
            Hit(cancelled, ids[0], "cancelled-target", true);
            rig.life.CompleteWave(null, null, 0, 0);
            JsonElement[] shots = rig.Events("player_shot");
            Check(shots[0].GetProperty("payload").GetProperty("createdProjectileCount").GetInt32() == 0 &&
                shots[0].GetProperty("payload").GetProperty("failedProjectileCount").GetInt32() == 2, "firing success is distinct from failed projectile creation");
            Check(rig.Events("projectile_impact").Length == 0 && rig.Summary().GetProperty("weapons").EnumerateArray().Sum(_w => _w.GetProperty("kills").GetInt32()) == 0,
                "cancel or forced cleanup never counts a hit or kill");
            Check(rig.Summary().GetProperty("weaponTypes")[0].GetProperty("shots").GetInt32() == 2 &&
                rig.Summary().GetProperty("weaponTypes")[0].GetProperty("projectiles").GetInt32() == 1,
                "failed creation preserves shot fact without inventing projectiles");
        }
        LogData disabled = LogInfo.Copy(_settings);
        disabled.enabled = false;
        using (var rig = new Rig(disabled))
        {
            rig.Begin(1);
            Check(rig.combat.StartShot(rig.right, "disabled", 0, null, 1) == null, "disabled logger attaches no live shot context");
            rig.combat.PlayerDamaged(null, 10, 0);
            Check(rig.Rows().Length == 0, "disabled combat observation produces no records");
        }
        using (var session = new LogSession(_settings, _id => throw new IOException("denied")))
        {
            var life = new LogGameLifecycle(session);
            life.Start(null, null, null, null);
            var combat = new LogCombatCollector(life);
            Check(combat.StartShot(new object(), "failed", 0, null, 1) == null, "store failure does not block caller or allocate shot IDs");
            life.End("unknown", null, null);
        }
    }

    private static void StaticIntegration(string _root)
    {
        string assets = Path.Combine(_root, "POC2/Assets");
        string weapon = File.ReadAllText(Path.Combine(assets, "Scripts/Player/PlayerWeapon.cs"));
        string health = File.ReadAllText(Path.Combine(assets, "Scripts/Player/PlayerHealth.cs"));
        string projectile = File.ReadAllText(Path.Combine(assets, "Scripts/Projectile/Projectile.cs"));
        string enemy = File.ReadAllText(Path.Combine(assets, "Scripts/Enemy/Enemy.cs"));
        string game = File.ReadAllText(Path.Combine(assets, "Scripts/Game/GameManager.cs"));
        string adapter = File.ReadAllText(Path.Combine(assets, "Scripts/Game/GameManager.Logging.cs"));
        string fire = weapon.Substring(weapon.IndexOf("private void TryFire()", StringComparison.Ordinal));
        fire = fire.Substring(0, fire.IndexOf("private Vector3 AimPoint", StringComparison.Ordinal));
        Ordered(fire, "PickHand(Time.time)", "WeaponInstance firingWeapon = Hand(hand)", "TakeShot(hand", "Fired?.Invoke()", "HandFired?.Invoke(hand)", "Vector3 origin = MuzzleBlocked", "CombatLog?.StartShot", "ProjectileSpawner.Launch", "ProjectileCreated", "FinalizeShot");
        Check(fire.Contains("int group = pellets > 1 ? ++pelletGroupCounter : 0") && fire.Contains("pelletGroup = group"), "pelletGroup gameplay semantics unchanged for single and pellet shots");
        Check(!adapter.Contains("Fired +=") && !adapter.Contains("HandFired +=") && !adapter.Contains("EnemyImpact +="), "no duplicate legacy fire or impact subscriptions");
        string damage = health.Substring(health.IndexOf("private void ApplyDamageInternal", StringComparison.Ordinal));
        Ordered(damage, "CurrentHealth - _amount * DamageTakenMultiplier", "CombatLog?.PlayerDamaged", "HealthChanged?.Invoke", "DamagedFrom?.Invoke", "Died?.Invoke");
        Check(health.Contains("public void ApplyDamage(float _amount)") && health.Contains("public void ApplyDamage(float _amount, Vector3 _sourceWorldPosition)"), "legacy damage signatures preserved");
        Check(enemy.Contains("target.ApplyDamage(attackDamage, transform.position, CaptureAttackSource(transform.position))") &&
            enemy.Contains("attackSource = CaptureAttackSource(origin)"), "melee and ranged sources captured at attack time");
        Check(projectile.Contains("player.ApplyDamage(shot.damage, shotOrigin, shot.attackSource)") && projectile.Contains("public LogShotContext logContext;"), "projectile carries source and shot context independent of owner lifetime");
        Ordered(projectile.Substring(projectile.IndexOf("float healthBefore = enemy.Health", StringComparison.Ordinal)), "float healthBefore", "enemy.TakeHit", "Mathf.Max(0f, healthBefore - Mathf.Max(0f, enemy.Health))", "Resolve(hit, true");
        Check(projectile.Contains("shot.logContext?.ProjectileEnded") && projectile.Contains("projectile.shot.logContext?.ProjectileEnded"), "expiry and CancelAll notify telemetry without producing kills");
        Check(game.Contains("ObserveLogging(SynchronizeCombatEquipment)") && adapter.Contains("telemetry.RegisterCollector(logCombat)") &&
            adapter.Contains("weapon.CombatLog = logCombat") && adapter.Contains("playerHealth.CombatLog = logCombat"), "combat collector connects once to live game components");
        Check(game.Contains("DetachCombatLogging()") && !adapter.Contains("Refresh +="), "scene cleanup detaches optional observers without UI refresh tracking");
        string[] allMeta = Directory.GetFiles(assets, "*.meta", SearchOption.AllDirectories);
        foreach (string name in new[] { "LogCombatCollector", "LogCombatPayloads" })
        {
            string meta = File.ReadAllText(Path.Combine(assets, "Scripts/Logging/" + name + ".cs.meta"));
            string guid = Regex.Match(meta, @"(?m)^guid: ([a-f0-9]{32})").Groups[1].Value;
            Check(guid.Length == 32 && allMeta.Count(_path => File.ReadAllText(_path).Contains("guid: " + guid)) == 1, "new source metadata unique: " + name);
        }
    }

    private static void Sample(LogData _settings, string _root)
    {
        using (var rig = new Rig(_settings))
        {
            rig.combat.SetEquipped(new[] { rig.Held(rig.right, "right") });
            rig.Begin(1);
            rig.life.AdvanceFixedUpdate(0.02);
            var shot = rig.Fire(rig.right, 0, 2, out string[] ids);
            Hit(shot, ids[0], "synthetic-target", true, 3);
            shot.ProjectileEnded(ids[0], "impact");
            shot.ProjectileEnded(ids[1], "miss");
            rig.life.EnemyKilled();
            rig.combat.PlayerDamaged(new LogAttackSource("synthetic-rusher", "unarmed", new LogPosition3 { x = 4, y = 1, z = 5 }), 3, 0);
            rig.life.End("death", null, null, true);
            JsonElement[] rows = rig.Rows();
            Check(rows.Any(_row => _row.GetProperty("eventType").GetString() == "player_shot") && rows.Any(_row => _row.GetProperty("eventType").GetString() == "death_snapshot"), "synthetic sample contains combat and lethal lifecycle");
            for (int i = 0; i < rows.Length; i++)
            {
                Check(rows[i].GetProperty("sequence").GetInt64() == i + 1, "synthetic sample sequence " + i);
            }
            File.WriteAllLines(Path.Combine(_root, "Docs/로그-시스템-4차.synthetic.jsonl"), rig.store.Lines, new UTF8Encoding(false));
        }
    }

    private static void Ordered(string _source, params string[] _tokens)
    {
        int previous = -1;
        foreach (string token in _tokens)
        {
            int next = _source.IndexOf(token, previous + 1, StringComparison.Ordinal);
            Check(next > previous, "observation order: " + token);
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
