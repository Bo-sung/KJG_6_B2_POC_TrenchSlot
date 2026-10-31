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
    PurchaseAttempt,
    AmmoTransaction,
    OfferSet,
    ShopVisit,
    ShopInput,
    ShopAction
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
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            Converters = { LogUnityValueConverter.Instance }
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

// Unity 값 타입은 computed property(normalized 등)가 자기 타입을 다시 돌려줘 순환 참조로 직렬화가 실패한다.
// JsonUtility와 같은 필드 이름으로 실제 성분만 기록한다.
public sealed class LogUnityValueConverter : JsonConverter
{
    public static readonly LogUnityValueConverter Instance = new LogUnityValueConverter();

    public override bool CanRead => false;

    public override bool CanConvert(Type _type)
    {
        return _type == typeof(UnityEngine.Vector2) || _type == typeof(UnityEngine.Vector3)
            || _type == typeof(UnityEngine.Vector4) || _type == typeof(UnityEngine.Quaternion)
            || _type == typeof(UnityEngine.Vector2Int) || _type == typeof(UnityEngine.Vector3Int)
            || _type == typeof(UnityEngine.Color) || _type == typeof(UnityEngine.Color32);
    }

    public override void WriteJson(JsonWriter _writer, object _value, JsonSerializer _serializer)
    {
        _writer.WriteStartObject();
        switch (_value)
        {
            case UnityEngine.Vector2 v: Write(_writer, "x", v.x, "y", v.y); break;
            case UnityEngine.Vector3 v: Write(_writer, "x", v.x, "y", v.y, "z", v.z); break;
            case UnityEngine.Vector4 v: Write(_writer, "x", v.x, "y", v.y, "z", v.z, "w", v.w); break;
            case UnityEngine.Quaternion q: Write(_writer, "x", q.x, "y", q.y, "z", q.z, "w", q.w); break;
            case UnityEngine.Color c: Write(_writer, "r", c.r, "g", c.g, "b", c.b, "a", c.a); break;
            case UnityEngine.Vector2Int v:
                _writer.WritePropertyName("x"); _writer.WriteValue(v.x);
                _writer.WritePropertyName("y"); _writer.WriteValue(v.y);
                break;
            case UnityEngine.Vector3Int v:
                _writer.WritePropertyName("x"); _writer.WriteValue(v.x);
                _writer.WritePropertyName("y"); _writer.WriteValue(v.y);
                _writer.WritePropertyName("z"); _writer.WriteValue(v.z);
                break;
            case UnityEngine.Color32 c:
                _writer.WritePropertyName("r"); _writer.WriteValue(c.r);
                _writer.WritePropertyName("g"); _writer.WriteValue(c.g);
                _writer.WritePropertyName("b"); _writer.WriteValue(c.b);
                _writer.WritePropertyName("a"); _writer.WriteValue(c.a);
                break;
        }
        _writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader _reader, Type _type, object _existing, JsonSerializer _serializer)
    {
        throw new NotSupportedException();
    }

    private static void Write(JsonWriter _writer, params object[] _pairs)
    {
        for (int i = 0; i < _pairs.Length; i += 2)
        {
            _writer.WritePropertyName((string)_pairs[i]);
            _writer.WriteValue((float)_pairs[i + 1]);
        }
    }
}
