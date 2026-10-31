using System;
using LogKit;

// 게임 기록(LogSession)의 한 줄을 LogKit으로 넘긴다. 로컬 보관·서버 전송은 LogKit이 맡는다.
// 줄(JSON 한 건)은 LogKit의 dataJson에 그대로 들어가고(문자열은 재직렬화하지 않는다), eventType은 message로 올린다.
// 서버 전송은 player_position을 0.5초 간격으로만 보낸다. 로컬 JSONL에는 모든 줄이 남는다.
public sealed class LogKitStore : ILogStore
{
    private const string LEVEL = "Info";
    private const string CATEGORY = "Telemetry";
    private const string EVENT_TYPE_KEY = "\"eventType\":\"";
    private const string ELAPSED_KEY = "\"elapsedSeconds\":";
    private const string PLAYER_POSITION = "player_position";
    // 분석은 0.5초 표본을 쓰므로 LogKit 전송도 이 간격으로 줄인다. 부동소수 오차용 여유를 둔다.
    private const double PLAYER_POSITION_INTERVAL_SECONDS = 0.5;
    private const double INTERVAL_EPSILON = 1e-6;

    // 검증용 구독자. 기록된 줄을 그대로 받는다.
    public static event Action<string> LineAppended;

    private bool disposed;
    private double lastPlayerPositionSentSeconds = double.NegativeInfinity;

    public void Append(string _line)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(LogKitStore));
        }
        // 검증용 구독자는 전송 여부와 상관없이 기록된 모든 줄을 받는다.
        LineAppended?.Invoke(_line);
        string eventType = EventType(_line);
        if (eventType == PLAYER_POSITION && !ShouldSendPlayerPosition(_line))
        {
            return;
        }
        GameLogger.Data(LEVEL, CATEGORY, eventType, _line);
    }

    // LogKit이 주기 전송과 세션 종료 때 보내므로 여기서 할 일은 없다.
    public void Flush()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(LogKitStore));
        }
    }

    public void Dispose()
    {
        disposed = true;
    }

    private bool ShouldSendPlayerPosition(string _line)
    {
        if (!TryReadElapsed(_line, out double elapsed))
        {
            return true;
        }
        if (elapsed - lastPlayerPositionSentSeconds < PLAYER_POSITION_INTERVAL_SECONDS - INTERVAL_EPSILON)
        {
            return false;
        }
        lastPlayerPositionSentSeconds = elapsed;
        return true;
    }

    private static bool TryReadElapsed(string _line, out double _elapsed)
    {
        _elapsed = 0;
        int start = _line.IndexOf(ELAPSED_KEY, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }
        start += ELAPSED_KEY.Length;
        int end = start;
        while (end < _line.Length && _line[end] != ',' && _line[end] != '}')
        {
            end++;
        }
        return double.TryParse(_line.Substring(start, end - start), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _elapsed);
    }

    private static string EventType(string _line)
    {
        int start = _line.IndexOf(EVENT_TYPE_KEY, StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }
        start += EVENT_TYPE_KEY.Length;
        int end = _line.IndexOf('"', start);
        return end < 0 ? "" : _line.Substring(start, end - start);
    }
}
