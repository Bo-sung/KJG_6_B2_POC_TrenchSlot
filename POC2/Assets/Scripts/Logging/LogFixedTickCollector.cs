using System;

public sealed class LogHeldWeaponSnapshot
{
    public string instanceId;
    public string weaponTypeId;
    public string hand;
    public int? slot;
}

public sealed class LogPlayerTickSnapshot
{
    public double? x;
    public double? z;
    public double? yaw;
    public double? pitch;
    public string activeHand;
    public int? activeSlot;
    public LogHeldWeaponSnapshot[] weapons;
}

public sealed class LogFixedTickCollector
{
    private readonly LogGameLifecycle lifecycle;
    private long lastRecordedTick;

    public LogFixedTickCollector(LogGameLifecycle _lifecycle)
    {
        lifecycle = _lifecycle ?? throw new ArgumentNullException(nameof(_lifecycle));
    }

    public bool CanRecord(bool _playerControllable)
    {
        LogSession session = lifecycle.Session;
        return _playerControllable && lifecycle.IsActive && lifecycle.IsWaveOpen && session.IsRecording &&
            session.Phase == "wave" && session.FixedTick > lastRecordedTick;
    }

    public bool Record(bool _playerControllable, LogPlayerTickSnapshot _player, LogEnemySnapshot _enemies)
    {
        if (!CanRecord(_playerControllable))
        {
            return false;
        }
        LogSession session = lifecycle.Session;
        long tick = session.FixedTick;
        var player = LogJson.CopyPayload(_player);
        var enemies = LogJson.CopyPayload(_enemies);
        lastRecordedTick = tick;
        bool playerAccepted = session.TryRecord("player_position", new { fixedTick = tick, player });
        bool enemiesAccepted = session.TryRecord("enemy_alive_counts", new { fixedTick = tick, enemies });
        return playerAccepted && enemiesAccepted;
    }
}
