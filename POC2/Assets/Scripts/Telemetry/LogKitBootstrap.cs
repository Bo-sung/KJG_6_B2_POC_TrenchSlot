using System;
using LogKit;
using UnityEngine;

public static class LogKitBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureLogger()
    {
        try
        {
            TelemetryData settings = TableManager.Instance.Telemetry;
            if (!TelemetryInfo.Enabled(settings))
            {
                GameLogger.Configure(captureUnityLogs: false, uploadOnQuit: false, retryPendingOnStartup: false, uploadIntervalSeconds: 0f);
                return;
            }
            bool upload = TelemetryInfo.UploadEnabled(settings);
            GameLogger.Configure(
                developmentServerUrl: TelemetryInfo.OrNull(settings.developmentServerUrl),
                releaseServerUrl: TelemetryInfo.OrNull(settings.releaseServerUrl),
                apiKey: TelemetryInfo.OrNull(settings.apiKey),
                captureUnityLogs: settings.captureUnityLogs,
                flushIntervalSeconds: TelemetryInfo.FlushIntervalSeconds(settings),
                uploadOnQuit: upload,
                retryPendingOnStartup: upload && settings.retryPendingOnStartup,
                requestTimeoutSeconds: TelemetryInfo.RequestTimeoutSeconds(settings),
                uploadIntervalSeconds: upload ? TelemetryInfo.UploadIntervalSeconds(settings) : 0f);
        }
        catch (Exception)
        {
        }
    }
}
