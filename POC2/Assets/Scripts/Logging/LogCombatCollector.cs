using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal sealed class LogCombatWaveState
{
    internal int waveNumber;
    internal bool closed;
    internal int pendingShots;
    internal double equipmentSince;
    internal JObject closedSummary;
    internal readonly Dictionary<string, LogWeaponCombatTotals> weapons = new Dictionary<string, LogWeaponCombatTotals>(StringComparer.Ordinal);
    internal readonly Dictionary<string, LogReceivedDamageTotals> damage = new Dictionary<string, LogReceivedDamageTotals>(StringComparer.Ordinal);
    internal readonly HashSet<string> killedTargets = new HashSet<string>(StringComparer.Ordinal);
    internal LogHeldWeaponSnapshot[] equipped = Array.Empty<LogHeldWeaponSnapshot>();
}

public sealed class LogCombatCollector : ILogWaveSummaryCollector, ILogWaveSummaryReceiver
{
    private readonly LogGameLifecycle lifecycle;
    private LogCombatWaveState current;
    private LogHeldWeaponSnapshot[] equipped = Array.Empty<LogHeldWeaponSnapshot>();

    public string Key => "combat";
    private bool CanObserve => lifecycle.IsActive && lifecycle.Session.IsRecording;

    public LogCombatCollector(LogGameLifecycle _lifecycle)
    {
        lifecycle = _lifecycle ?? throw new ArgumentNullException(nameof(_lifecycle));
    }

    public void BeginWave(LogWaveBoundary _boundary)
    {
        current = new LogCombatWaveState
        {
            waveNumber = _boundary.WaveNumber,
            equipmentSince = _boundary.ElapsedSeconds,
            equipped = equipped
        };
    }

    public object CloseWave(LogWaveBoundary _boundary)
    {
        if (current == null || current.closed || current.waveNumber != _boundary.WaveNumber)
        {
            return null;
        }
        AccrueEquipment(current, _boundary.ElapsedSeconds);
        current.closed = true;
        return Snapshot(current);
    }

    public void SummaryClosed(LogWaveBoundary _boundary, object _summary)
    {
        if (current != null && current.waveNumber == _boundary.WaveNumber && current.pendingShots > 0)
        {
            current.closedSummary = (JObject)LogJson.CopyPayload(_summary);
        }
    }

    public void SetEquipped(IEnumerable<LogHeldWeaponSnapshot> _weapons)
    {
        Observe(() =>
        {
            LogHeldWeaponSnapshot[] next = (_weapons ?? Array.Empty<LogHeldWeaponSnapshot>()).Select(_weapon =>
                new LogHeldWeaponSnapshot { instanceId = _weapon.instanceId, weaponTypeId = _weapon.weaponTypeId, hand = _weapon.hand, slot = _weapon.slot }).ToArray();
            if (SameEquipment(equipped, next))
            {
                return;
            }
            if (current != null && !current.closed)
            {
                AccrueEquipment(current, lifecycle.Session.ElapsedSeconds);
                current.equipped = next;
            }
            equipped = next;
            lifecycle.Session.TryRecord("weapon_equipped", new { weapons = next });
        });
    }

    public LogShotContext StartShot(object _weapon, string _typeId, int _hand, LogPosition3 _origin, int _requestedProjectiles,
        int? _magazine = null, int? _reserve = null, bool? _akimbo = null, bool? _aiming = null)
    {
        LogShotContext context = null;
        Observe(() =>
        {
            LogSession session = lifecycle.Session;
            LogCombatWaveState wave = current != null && !current.closed ? current : null;
            string instanceId = session.GetWeaponId(_weapon);
            context = new LogShotContext(this, wave, session.NewId(LogIdKind.Shot), instanceId,
                string.IsNullOrWhiteSpace(_typeId) ? "unknown" : _typeId, _hand == 0 ? "right" : "left",
                session.WaveNumber, session.Phase, session.ElapsedSeconds, _origin, _requestedProjectiles, _magazine, _reserve, _akimbo, _aiming);
            if (wave != null)
            {
                wave.pendingShots++;
                LogWeaponCombatTotals totals = Weapon(wave, context.weaponKey, context.WeaponInstanceId, context.WeaponTypeId);
                totals.shots++;
                totals.requestedProjectiles += _requestedProjectiles;
            }
        });
        return context;
    }

    internal string NewProjectileId(LogShotContext _shot)
    {
        string id = null;
        Observe(() =>
        {
            id = lifecycle.Session.NewId(LogIdKind.Projectile);
            _shot.launches.Add(id, false);
        });
        return id;
    }

    internal void ProjectileCreated(LogShotContext _shot, string _id, bool _created)
    {
        Observe(() =>
        {
            if (_id == null || !_shot.launches.ContainsKey(_id) || _shot.finalized || _shot.launches[_id])
            {
                return;
            }
            _shot.launches[_id] = _created;
            if (_created && _shot.wave != null)
            {
                Weapon(_shot.wave, _shot.weaponKey, _shot.WeaponInstanceId, _shot.WeaponTypeId).projectiles++;
            }
        });
    }

    internal void FinalizeShot(LogShotContext _shot)
    {
        Observe(() =>
        {
            if (_shot.finalized)
            {
                return;
            }
            _shot.finalized = true;
            lifecycle.Session.TryRecord("player_shot", new
            {
                shotId = _shot.ShotId,
                weaponInstanceId = _shot.WeaponInstanceId,
                weaponTypeId = _shot.WeaponTypeId,
                hand = _shot.Hand,
                magazineAmmo = _shot.MagazineAmmo,
                reserveAmmo = _shot.ReserveAmmo,
                akimbo = _shot.Akimbo,
                aiming = _shot.Aiming,
                origin = _shot.Origin,
                originWaveNumber = _shot.OriginWaveNumber,
                originPhase = _shot.OriginPhase,
                firedElapsedSeconds = _shot.FiredElapsedSeconds,
                requestedProjectileCount = _shot.RequestedProjectiles,
                createdProjectileCount = _shot.launches.Values.Count(_created => _created),
                failedProjectileCount = _shot.launches.Values.Count(_created => !_created),
                projectileIds = _shot.launches.Where(_entry => _entry.Value).Select(_entry => _entry.Key).ToArray()
            });
            ReleaseIfComplete(_shot);
        });
    }

    internal void Impact(LogShotContext _shot, LogCombatImpact _impact)
    {
        Observe(() =>
        {
            if (_impact?.projectileId == null || !_shot.launches.TryGetValue(_impact.projectileId, out bool created) || !created ||
                _shot.ended.Contains(_impact.projectileId) || !_shot.impacts.Add(_impact.projectileId))
            {
                return;
            }
            LogJson.CopyPayload(_impact);
            LogCombatWaveState wave = _shot.wave;
            bool changed = false;
            if (_impact.hitEnemy && wave != null)
            {
                LogWeaponCombatTotals totals = Weapon(wave, _shot.weaponKey, _shot.WeaponInstanceId, _shot.WeaponTypeId);
                totals.hitProjectiles++;
                totals.actualDamageDealt += Math.Max(0d, _impact.actualDamage);
                if (!_shot.hit)
                {
                    _shot.hit = true;
                    totals.hitShots++;
                }
                if (_impact.killed && wave.killedTargets.Add(_impact.targetId ?? _impact.projectileId))
                {
                    totals.kills++;
                }
                changed = true;
            }
            lifecycle.Session.TryRecord("projectile_impact", new
            {
                shotId = _shot.ShotId,
                weaponInstanceId = _shot.WeaponInstanceId,
                weaponTypeId = _shot.WeaponTypeId,
                hand = _shot.Hand,
                originWaveNumber = _shot.OriginWaveNumber,
                originPhase = _shot.OriginPhase,
                impact = _impact,
                lateResult = wave != null && wave.closed
            });
            if (changed && wave.closed)
            {
                CorrectClosedSummary(wave);
            }
        });
    }

    internal void ProjectileEnded(LogShotContext _shot, string _id, string _reason)
    {
        try
        {
            if (_id == null || !_shot.launches.TryGetValue(_id, out bool created) || !created || !_shot.ended.Add(_id))
            {
                return;
            }
            ReleaseIfComplete(_shot);
        }
        catch (Exception exception)
        {
            lifecycle.Session.ReportCollectionFailure(exception);
        }
    }

    public void PlayerDamaged(LogAttackSource _source, double _before, double _after)
    {
        Observe(() =>
        {
            LogDamageSnapshot damage = (_source ?? new LogAttackSource(null, null, null)).Damage(_before, _after);
            LogJson.CopyPayload(damage);
            if (damage.actualDamage <= 0d)
            {
                return;
            }
            lifecycle.SetLastDamage(damage);
            if (current != null && !current.closed)
            {
                if (!current.damage.TryGetValue(damage.enemyTypeId, out LogReceivedDamageTotals totals))
                {
                    totals = new LogReceivedDamageTotals { enemyTypeId = damage.enemyTypeId };
                    current.damage.Add(damage.enemyTypeId, totals);
                }
                totals.actualDamage += damage.actualDamage.Value;
                totals.hits++;
            }
            lifecycle.Session.TryRecord("player_damage", damage);
        });
    }

    private void CorrectClosedSummary(LogCombatWaveState _wave)
    {
        if (_wave.closedSummary == null || !lifecycle.Session.TryReviseSummary(_wave.waveNumber, out string id, out int revision))
        {
            return;
        }
        JObject corrected = (JObject)_wave.closedSummary.DeepClone();
        corrected["summaryId"] = id;
        corrected["revision"] = revision;
        corrected["summaries"]["combat"] = JObject.FromObject(new { collectionStatus = "collected", data = Snapshot(_wave) });
        lifecycle.Session.TryRecord("wave_summary_correction", corrected);
        _wave.closedSummary = corrected;
    }

    private static void ReleaseIfComplete(LogShotContext _shot)
    {
        if (_shot.completed || !_shot.finalized || _shot.ended.Count < _shot.launches.Values.Count(_created => _created))
        {
            return;
        }
        _shot.completed = true;
        if (_shot.wave != null)
        {
            _shot.wave.pendingShots--;
            if (_shot.wave.pendingShots == 0)
            {
                _shot.wave.closedSummary = null;
            }
        }
    }

    private static bool SameEquipment(LogHeldWeaponSnapshot[] _a, LogHeldWeaponSnapshot[] _b)
    {
        return _a.Length == _b.Length && _a.Zip(_b, (_left, _right) =>
            _left.instanceId == _right.instanceId && _left.weaponTypeId == _right.weaponTypeId &&
            _left.hand == _right.hand && _left.slot == _right.slot).All(_same => _same);
    }

    private static LogWeaponCombatTotals Weapon(LogCombatWaveState _wave, string _key, string _instanceId, string _typeId)
    {
        if (!_wave.weapons.TryGetValue(_key, out LogWeaponCombatTotals totals))
        {
            totals = new LogWeaponCombatTotals { instanceId = _instanceId, weaponTypeId = _typeId };
            _wave.weapons.Add(_key, totals);
        }
        return totals;
    }

    private static void AccrueEquipment(LogCombatWaveState _wave, double _now)
    {
        double elapsed = _now - _wave.equipmentSince;
        foreach (var weapon in _wave.equipped.GroupBy(_weapon => _weapon.instanceId).Select(_group => _group.First()))
        {
            string key = weapon.instanceId ?? "unknown:" + weapon.weaponTypeId + ":" + weapon.hand;
            Weapon(_wave, key, weapon.instanceId, weapon.weaponTypeId).equippedSeconds += elapsed;
        }
        _wave.equipmentSince = _now;
    }

    private static LogCombatSummary Snapshot(LogCombatWaveState _wave)
    {
        LogWeaponCombatTotals[] weapons = _wave.weapons.Values.Select(_value => new LogWeaponCombatTotals
        {
            instanceId = _value.instanceId, weaponTypeId = _value.weaponTypeId, equippedSeconds = _value.equippedSeconds,
            shots = _value.shots, requestedProjectiles = _value.requestedProjectiles, projectiles = _value.projectiles,
            hitProjectiles = _value.hitProjectiles, hitShots = _value.hitShots, kills = _value.kills, actualDamageDealt = _value.actualDamageDealt
        }).ToArray();
        return new LogCombatSummary
        {
            weapons = weapons,
            weaponTypes = weapons.GroupBy(_weapon => _weapon.weaponTypeId).Select(_group => new LogWeaponCombatTotals
            {
                weaponTypeId = _group.Key, equippedSeconds = _group.Sum(_weapon => _weapon.equippedSeconds),
                shots = _group.Sum(_weapon => _weapon.shots), requestedProjectiles = _group.Sum(_weapon => _weapon.requestedProjectiles),
                projectiles = _group.Sum(_weapon => _weapon.projectiles), hitProjectiles = _group.Sum(_weapon => _weapon.hitProjectiles),
                hitShots = _group.Sum(_weapon => _weapon.hitShots), kills = _group.Sum(_weapon => _weapon.kills),
                actualDamageDealt = _group.Sum(_weapon => _weapon.actualDamageDealt)
            }).ToArray(),
            damageReceived = _wave.damage.Values.Select(_value => new LogReceivedDamageTotals
            {
                enemyTypeId = _value.enemyTypeId, hits = _value.hits, actualDamage = _value.actualDamage
            }).ToArray()
        };
    }

    private void Observe(Action _observation)
    {
        if (!CanObserve)
        {
            return;
        }
        try
        {
            _observation();
        }
        catch (Exception exception)
        {
            lifecycle.Session.ReportCollectionFailure(exception);
        }
    }
}
