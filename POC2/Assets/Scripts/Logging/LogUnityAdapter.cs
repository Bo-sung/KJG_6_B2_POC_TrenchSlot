using UnityEngine;

public static class LogUnityAdapter
{
    public static LogSession CreateSession()
    {
        LogData settings = LogInfo.Copy(TableManager.Instance.Log);
        string persistentDataPath = Application.persistentDataPath;
        double initialFixedDeltaTime = Time.fixedDeltaTime;
        return new LogSession(settings, _id => new FileLogStore(persistentDataPath, _id),
            _warning: _message => Debug.LogWarning(_message), _initialFixedDeltaTime: initialFixedDeltaTime);
    }

    public static void AdvanceFixedUpdate(LogSession _session)
    {
        _session?.AdvanceFixedUpdate(Time.fixedDeltaTime);
    }
}
