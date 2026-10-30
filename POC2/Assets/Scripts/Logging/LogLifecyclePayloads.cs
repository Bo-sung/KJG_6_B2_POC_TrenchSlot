using System.Collections.Generic;

public sealed class LogEnemySnapshot
{
    public int aliveCount;
    public Dictionary<string, int> aliveByKind;
    public int? nearbyAliveCount;
    public double? radius;
}

public sealed class LogDamageSnapshot
{
    public string enemyTypeId = "unknown";
    public string enemyWeaponId = "unknown";
    public LogPosition3 attackPosition;
    public double? actualDamage;
}

public sealed class LogWavePlan
{
    public int waveNumber;
    public double durationSeconds;
    public Dictionary<string, int> plannedEnemies;
    public string eraId;
    public string enemyWeaponId;
}

public sealed class LogWaveBoundary
{
    public int WaveNumber { get; }
    public double StartedElapsedSeconds { get; }
    public double ElapsedSeconds { get; }
    public string Status { get; }
    public string Reason { get; }

    public LogWaveBoundary(int _waveNumber, double _startedElapsedSeconds, double _elapsedSeconds,
        string _status, string _reason)
    {
        WaveNumber = _waveNumber;
        StartedElapsedSeconds = _startedElapsedSeconds;
        ElapsedSeconds = _elapsedSeconds;
        Status = _status;
        Reason = _reason;
    }
}

public interface ILogWaveSummaryCollector
{
    string Key { get; }
    void BeginWave(LogWaveBoundary _boundary);
    object CloseWave(LogWaveBoundary _boundary);
}

public interface ILogSessionEndObserver
{
    void SessionEnding(string _reason);
}
