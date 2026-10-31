using System;
using LogKit;
using UnityEngine;

public static class LogKitBootstrap
{
    // Preload 씬이 없거나 프로필을 적용하기 전에 실행되는 기본 설정.
    // PreloadController가 있으면 Awake에서 같은 Apply를 다시 호출해 프로필 값으로 덮어쓴다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureLogger()
    {
        Apply(TableManager.Instance.Telemetry);
    }

    public static void Apply(TelemetryData _settings)
    {
        try
        {
            if (!TelemetryInfo.Enabled(_settings))
            {
                GameLogger.Configure(captureUnityLogs: false, uploadOnQuit: false, retryPendingOnStartup: false, uploadIntervalSeconds: 0f);
                return;
            }
            bool upload = TelemetryInfo.UploadEnabled(_settings);
            GameLogger.Configure(
                developmentServerUrl: TelemetryInfo.OrNull(_settings.developmentServerUrl),
                releaseServerUrl: TelemetryInfo.OrNull(_settings.releaseServerUrl),
                apiKey: TelemetryInfo.OrNull(_settings.apiKey),
                captureUnityLogs: _settings.captureUnityLogs,
                flushIntervalSeconds: TelemetryInfo.FlushIntervalSeconds(_settings),
                uploadOnQuit: upload,
                retryPendingOnStartup: upload && _settings.retryPendingOnStartup,
                requestTimeoutSeconds: TelemetryInfo.RequestTimeoutSeconds(_settings),
                uploadIntervalSeconds: upload ? TelemetryInfo.UploadIntervalSeconds(_settings) : 0f);
        }
        catch (Exception)
        {
        }
    }
}
