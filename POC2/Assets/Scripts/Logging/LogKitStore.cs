using System;
using LogKit;

// 게임 기록(LogSession)의 한 줄을 LogKit으로 넘긴다. 로컬 보관·서버 전송은 LogKit이 맡는다.
// 줄(JSON 한 건)은 LogKit의 dataJson에 그대로 들어가고, eventType은 message로 올린다.
public sealed class LogKitStore : ILogStore
{
    private const string LEVEL = "Info";
    private const string CATEGORY = "GameEvent";
    private const string EVENT_TYPE_KEY = "\"eventType\":\"";

    // 검증용 구독자. 기록된 줄을 그대로 받는다.
    public static event Action<string> LineAppended;

    private bool disposed;

    public void Append(string _line)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(LogKitStore));
        }
        GameLogger.Data(LEVEL, CATEGORY, EventType(_line), _line);
        LineAppended?.Invoke(_line);
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
