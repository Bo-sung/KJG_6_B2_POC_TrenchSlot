using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

public sealed class LogDiagnostics
{
    public long FailureCount { get; internal set; }
    public long DroppedRecords { get; internal set; }
    public int WarningCount { get; internal set; }
    public string LastError { get; internal set; }
    public bool StorageFailed { get; internal set; }
}

public sealed class LogSession : IDisposable
{
    private sealed class WeaponId
    {
        public string value;
    }

    private readonly LogData settings;
    private readonly ILogUtcClock utcClock;
    private readonly LogFixedClock fixedClock;
    private readonly Action<string> warning;
    private readonly List<string> buffer = new List<string>();
    private readonly Dictionary<LogIdKind, long> ids = new Dictionary<LogIdKind, long>();
    private readonly ConditionalWeakTable<object, WeaponId> weapons = new ConditionalWeakTable<object, WeaponId>();
    private readonly Dictionary<int, int> summaryRevisions = new Dictionary<int, int>();
    private ILogStore store;
    private int bufferedBytes;
    private double lastFlushSeconds;
    private long sequence;
    private bool ended;
    private bool enabled;

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public LogDiagnostics Diagnostics { get; } = new LogDiagnostics();
    public bool IsActive => !ended;
    public bool IsRecording => enabled && !ended && !Diagnostics.StorageFailed;
    public double ElapsedSeconds => fixedClock.ElapsedSeconds;
    public double? FixedDeltaTime => fixedClock.FixedDeltaTime;
    public long FixedTick => fixedClock.TickCount;
    public string StartedUtc { get; }
    public int? WaveNumber { get; private set; }
    public string Phase { get; private set; } = "unknown";
    public int BufferedRecords => buffer.Count;
    public int BufferedBytes => bufferedBytes;
    public long Sequence => sequence;

    public LogSession(LogData _settings, Func<string, ILogStore> _storeFactory,
        ILogUtcClock _utcClock = null, Action<string> _warning = null, double? _initialFixedDeltaTime = null)
    {
        settings = LogInfo.Copy(_settings);
        fixedClock = new LogFixedClock(_initialFixedDeltaTime);
        utcClock = _utcClock ?? new LogUtcClock();
        warning = _warning;
        StartedUtc = LogJson.Utc(utcClock.UtcNow);
        if (!LogInfo.TryValidate(settings, out string error))
        {
            ReportFailure(error);
            return;
        }
        enabled = settings.enabled;
        if (!enabled)
        {
            return;
        }
        try
        {
            store = _storeFactory(SessionId);
            if (store == null)
            {
                throw new InvalidOperationException("Log store factory returned null.");
            }
        }
        catch (Exception exception)
        {
            FailStorage(exception);
        }
    }

    public string ReadUtc()
    {
        return LogJson.Utc(utcClock.UtcNow);
    }

    public void SetContext(int? _waveNumber, string _phase)
    {
        if (ended)
        {
            return;
        }
        if (_waveNumber.HasValue && _waveNumber.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_waveNumber));
        }
        WaveNumber = _waveNumber;
        Phase = string.IsNullOrWhiteSpace(_phase) ? "unknown" : _phase;
    }

    public void AdvanceFixedUpdate(double _fixedDeltaTime)
    {
        if (ended)
        {
            return;
        }
        double? previous = fixedClock.FixedDeltaTime;
        fixedClock.Advance(_fixedDeltaTime);
        if (previous.HasValue && previous.Value != _fixedDeltaTime)
        {
            TryRecord("fixed_delta_time_changed", new
            {
                previousFixedDeltaTime = previous.Value,
                fixedDeltaTime = _fixedDeltaTime
            });
        }
        if (ElapsedSeconds - lastFlushSeconds >= settings?.flushIntervalSeconds)
        {
            Flush();
        }
    }

    public string NewId(LogIdKind _kind)
    {
        if (ended)
        {
            return null;
        }
        if (!Enum.IsDefined(typeof(LogIdKind), _kind))
        {
            throw new ArgumentOutOfRangeException(nameof(_kind));
        }
        ids.TryGetValue(_kind, out long value);
        value = checked(value + 1);
        ids[_kind] = value;
        return SessionId + ":" + _kind.ToString().ToLowerInvariant() + ":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public string GetWeaponId(object _weaponInstance)
    {
        if (ended || _weaponInstance == null)
        {
            return null;
        }
        return weapons.GetValue(_weaponInstance, _key => new WeaponId { value = NewId(LogIdKind.Weapon) }).value;
    }

    public bool TryCloseSummary(int _waveNumber, out string _summaryId)
    {
        _summaryId = null;
        if (ended || _waveNumber <= 0 || summaryRevisions.ContainsKey(_waveNumber))
        {
            return false;
        }
        summaryRevisions.Add(_waveNumber, 0);
        _summaryId = SummaryId(_waveNumber);
        return true;
    }

    public bool TryReviseSummary(int _waveNumber, out string _summaryId, out int _revision)
    {
        _summaryId = null;
        _revision = 0;
        if (ended || !summaryRevisions.TryGetValue(_waveNumber, out int previous))
        {
            return false;
        }
        _revision = checked(previous + 1);
        summaryRevisions[_waveNumber] = _revision;
        _summaryId = SummaryId(_waveNumber);
        return true;
    }

    public bool TryRecord(string _eventType, object _payload)
    {
        if (!IsRecording)
        {
            return false;
        }
        try
        {
            if (string.IsNullOrWhiteSpace(_eventType))
            {
                throw new ArgumentException("An event type is required.");
            }
            long next = checked(sequence + 1);
            string line = LogJson.Serialize(new LogRecord(settings.schemaVersion, _eventType, SessionId,
                next, ElapsedSeconds, WaveNumber, Phase, LogJson.CopyPayload(_payload)));
            int bytes = checked(Encoding.UTF8.GetByteCount(line) + 1);
            if (bytes > settings.maxRecordBytes)
            {
                throw new ArgumentException("Telemetry record exceeds maxRecordBytes.");
            }
            if (buffer.Count >= settings.maxBufferedRecords || bytes > settings.maxBufferedBytes - bufferedBytes)
            {
                Flush();
            }
            if (!IsRecording)
            {
                Diagnostics.DroppedRecords++;
                return false;
            }
            buffer.Add(line);
            bufferedBytes += bytes;
            sequence = next;
            if (buffer.Count >= settings.maxBufferedRecords || bufferedBytes >= settings.maxBufferedBytes)
            {
                Flush();
            }
            return !Diagnostics.StorageFailed;
        }
        catch (Exception exception)
        {
            Diagnostics.DroppedRecords++;
            ReportFailure(exception.GetType().Name + ": " + exception.Message);
            return false;
        }
    }

    public void Flush()
    {
        if (!IsRecording)
        {
            return;
        }
        try
        {
            foreach (string line in buffer)
            {
                store.Append(line);
            }
            store.Flush();
            buffer.Clear();
            bufferedBytes = 0;
            lastFlushSeconds = ElapsedSeconds;
        }
        catch (Exception exception)
        {
            FailStorage(exception);
        }
    }

    public void ReportCollectionFailure(Exception _exception)
    {
        ReportFailure(_exception.GetType().Name + ": " + _exception.Message);
    }

    public void Dispose()
    {
        if (ended)
        {
            return;
        }
        Flush();
        ended = true;
        try
        {
            store?.Dispose();
        }
        catch (Exception exception)
        {
            FailStorage(exception);
        }
        buffer.Clear();
        bufferedBytes = 0;
        store = null;
    }

    private string SummaryId(int _waveNumber)
    {
        return SessionId + ":wave:" + _waveNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void FailStorage(Exception _exception)
    {
        Diagnostics.StorageFailed = true;
        Diagnostics.DroppedRecords += buffer.Count;
        buffer.Clear();
        bufferedBytes = 0;
        ReportFailure(_exception.GetType().Name + ": " + _exception.Message);
    }

    private void ReportFailure(string _error)
    {
        Diagnostics.FailureCount++;
        Diagnostics.LastError = _error;
        if (Diagnostics.WarningCount >= (settings?.maxWarningsPerSession > 0 ? settings.maxWarningsPerSession : 1))
        {
            return;
        }
        Diagnostics.WarningCount++;
        try
        {
            warning?.Invoke("Telemetry: " + _error);
        }
        catch (Exception)
        {
            Diagnostics.LastError = _error + " (warning callback also failed)";
        }
    }
}
