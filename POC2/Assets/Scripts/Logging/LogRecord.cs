using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public enum LogIdKind
{
    Weapon,
    Shot,
    Projectile,
    Spin,
    PurchaseAttempt
}

public sealed class LogRecord
{
    public int SchemaVersion { get; }
    public string EventType { get; }
    public string SessionId { get; }
    public long Sequence { get; }
    public double ElapsedSeconds { get; }
    public int? WaveNumber { get; }
    public string Phase { get; }
    public JToken Payload { get; }

    public LogRecord(int _schemaVersion, string _eventType, string _sessionId, long _sequence,
        double _elapsedSeconds, int? _waveNumber, string _phase, JToken _payload)
    {
        SchemaVersion = _schemaVersion;
        EventType = _eventType;
        SessionId = _sessionId;
        Sequence = _sequence;
        ElapsedSeconds = _elapsedSeconds;
        WaveNumber = _waveNumber;
        Phase = _phase;
        Payload = _payload;
    }
}

public static class LogJson
{
    public static string Serialize(LogRecord _record)
    {
        JObject envelope = new JObject
        {
            ["schemaVersion"] = _record.SchemaVersion,
            ["eventType"] = _record.EventType,
            ["sessionId"] = _record.SessionId,
            ["sequence"] = _record.Sequence,
            ["elapsedSeconds"] = _record.ElapsedSeconds,
            ["waveNumber"] = _record.WaveNumber.HasValue ? new JValue(_record.WaveNumber.Value) : JValue.CreateNull(),
            ["phase"] = _record.Phase,
            ["payload"] = _record.Payload ?? JValue.CreateNull()
        };
        ValidateNumbers(envelope);
        return JsonConvert.SerializeObject(envelope, Formatting.None, new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
            StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
        });
    }

    public static JToken CopyPayload(object _payload)
    {
        if (_payload == null)
        {
            return JValue.CreateNull();
        }
        JToken copy = JToken.FromObject(_payload, JsonSerializer.Create(new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Include,
            TypeNameHandling = TypeNameHandling.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Error
        }));
        ValidateNumbers(copy);
        return copy;
    }

    public static string Utc(DateTimeOffset _time)
    {
        return _time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static void ValidateNumbers(JToken _token)
    {
        if (_token.Type == JTokenType.Float)
        {
            double value = _token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Non-finite telemetry number.");
            }
        }
        foreach (JToken child in _token.Children())
        {
            ValidateNumbers(child);
        }
    }
}
