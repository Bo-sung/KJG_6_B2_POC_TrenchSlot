using UnityEngine;

public static class LogUnityAdapter
{
    public static LogSession CreateSession()
    {
        // 새 판 파일을 만들기 전에 프로세스당 한 번 오래된 로컬 사본을 정리한다.
        TelemetryCleanup.TrimOnceForThisRun();
        LogData settings = LogInfo.Copy(TableManager.Instance.Log);
        double initialFixedDeltaTime = Time.fixedDeltaTime;
        return new LogSession(settings, _id => new TeeLogStore(new FileLogStore(_id), new LogKitStore()),
            _warning: _message => Debug.LogWarning(_message), _initialFixedDeltaTime: initialFixedDeltaTime);
    }

    public static void AdvanceFixedUpdate(LogSession _session)
    {
        _session?.AdvanceFixedUpdate(Time.fixedDeltaTime);
    }
}
