using System;

[Serializable]
public sealed class LogData
{
    public bool enabled;
    public int schemaVersion;
    public float deathEnemyRadius;
    public float flushIntervalSeconds;
    public int maxBufferedRecords;
    public int maxBufferedBytes;
    public int maxRecordBytes;
    public int maxWarningsPerSession;
}
