using System.Collections.Generic;

public sealed class LogAttackSource
{
    public string EnemyTypeId { get; }
    public string EnemyWeaponId { get; }
    private readonly bool hasPosition;
    private readonly double? x;
    private readonly double? y;
    private readonly double? z;

    public LogAttackSource(string _enemyTypeId, string _enemyWeaponId, LogPosition3 _position)
    {
        EnemyTypeId = string.IsNullOrWhiteSpace(_enemyTypeId) ? "unknown" : _enemyTypeId;
        EnemyWeaponId = string.IsNullOrWhiteSpace(_enemyWeaponId) ? "unknown" : _enemyWeaponId;
        hasPosition = _position != null;
        x = _position?.x;
        y = _position?.y;
        z = _position?.z;
    }

    public LogDamageSnapshot Damage(double _healthBefore, double _healthAfter)
    {
        return new LogDamageSnapshot
        {
            enemyTypeId = EnemyTypeId,
            enemyWeaponId = EnemyWeaponId,
            attackPosition = hasPosition ? new LogPosition3 { x = x, y = y, z = z } : null,
            actualDamage = System.Math.Max(0d, _healthBefore - _healthAfter)
        };
    }
}

public sealed class LogCombatImpact
{
    public string projectileId;
    public bool hitEnemy;
    public bool killed;
    public string targetId;
    public string enemyTypeId;
    public double actualDamage;
    public LogPosition3 point;
}

public sealed class LogWeaponCombatTotals
{
    public string instanceId;
    public string weaponTypeId;
    public double equippedSeconds;
    public int shots;
    public int requestedProjectiles;
    public int projectiles;
    public int hitProjectiles;
    public int hitShots;
    public int kills;
    public double actualDamageDealt;
}

public sealed class LogReceivedDamageTotals
{
    public string enemyTypeId;
    public double actualDamage;
    public int hits;
}

public sealed class LogCombatSummary
{
    public LogWeaponCombatTotals[] weapons;
    public LogWeaponCombatTotals[] weaponTypes;
    public LogReceivedDamageTotals[] damageReceived;
}

public interface ILogWaveSummaryReceiver
{
    void SummaryClosed(LogWaveBoundary _boundary, object _summary);
}

public sealed class LogShotContext
{
    private readonly LogCombatCollector owner;
    internal readonly LogCombatWaveState wave;
    internal readonly Dictionary<string, bool> launches = new Dictionary<string, bool>();
    internal readonly HashSet<string> impacts = new HashSet<string>();
    internal readonly HashSet<string> ended = new HashSet<string>();
    internal bool finalized;
    internal bool completed;
    internal bool hit;
    internal readonly string weaponKey;

    public string ShotId { get; }
    public string WeaponInstanceId { get; }
    public string WeaponTypeId { get; }
    public string Hand { get; }
    public int? OriginWaveNumber { get; }
    public string OriginPhase { get; }
    public double FiredElapsedSeconds { get; }
    public int RequestedProjectiles { get; }
    private readonly double? originX;
    private readonly double? originY;
    private readonly double? originZ;

    internal LogShotContext(LogCombatCollector _owner, LogCombatWaveState _wave, string _shotId,
        string _instanceId, string _typeId, string _hand, int? _waveNumber, string _phase,
        double _elapsed, LogPosition3 _origin, int _requested)
    {
        owner = _owner;
        wave = _wave;
        ShotId = _shotId;
        WeaponInstanceId = _instanceId;
        WeaponTypeId = _typeId;
        Hand = _hand;
        OriginWaveNumber = _waveNumber;
        OriginPhase = _phase;
        FiredElapsedSeconds = _elapsed;
        RequestedProjectiles = _requested;
        originX = _origin?.x;
        originY = _origin?.y;
        originZ = _origin?.z;
        weaponKey = _instanceId ?? "unknown:" + _typeId + ":" + _hand;
    }

    public LogPosition3 Origin => new LogPosition3 { x = originX, y = originY, z = originZ };

    public string NewProjectileId()
    {
        return owner.NewProjectileId(this);
    }

    public void ProjectileCreated(string _id, bool _created)
    {
        owner.ProjectileCreated(this, _id, _created);
    }

    public void FinalizeShot()
    {
        owner.FinalizeShot(this);
    }

    public void Impact(LogCombatImpact _impact)
    {
        owner.Impact(this, _impact);
    }

    public void ProjectileEnded(string _id, string _reason)
    {
        owner.ProjectileEnded(this, _id, _reason);
    }
}
