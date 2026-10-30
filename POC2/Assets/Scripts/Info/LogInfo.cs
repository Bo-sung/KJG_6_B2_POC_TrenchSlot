using System;

public static class LogInfo
{
    public static bool TryValidate(LogData _table, out string _error)
    {
        _error = null;
        if (_table == null || _table.schemaVersion <= 0 ||
            !IsPositiveFinite(_table.deathEnemyRadius) ||
            !IsPositiveFinite(_table.flushIntervalSeconds) ||
            _table.maxBufferedRecords <= 0 || _table.maxBufferedBytes <= 0 ||
            _table.maxRecordBytes <= 0 || _table.maxRecordBytes > _table.maxBufferedBytes ||
            _table.maxWarningsPerSession <= 0)
        {
            _error = "Invalid LogData: schema, radius, flush and buffer/warning limits must be positive and finite; record bytes must fit the buffer.";
        }
        return _error == null;
    }

    public static LogData Copy(LogData _table)
    {
        if (_table == null)
        {
            return null;
        }
        return new LogData
        {
            enabled = _table.enabled,
            schemaVersion = _table.schemaVersion,
            deathEnemyRadius = _table.deathEnemyRadius,
            flushIntervalSeconds = _table.flushIntervalSeconds,
            maxBufferedRecords = _table.maxBufferedRecords,
            maxBufferedBytes = _table.maxBufferedBytes,
            maxRecordBytes = _table.maxRecordBytes,
            maxWarningsPerSession = _table.maxWarningsPerSession
        };
    }

    private static bool IsPositiveFinite(float _value)
    {
        return !float.IsNaN(_value) && !float.IsInfinity(_value) && _value > 0f;
    }
}
