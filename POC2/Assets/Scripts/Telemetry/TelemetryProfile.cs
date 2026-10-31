using System;
using UnityEngine;

// Preload 씬에서 빌드 프로필별 LogKit 접속·전송 값을 설정한다.
// 값은 TableManager.Telemetry에 덮어쓴 뒤 LogKitBootstrap.Apply로 LogKit에 전달된다.
[Serializable]
public sealed class TelemetryProfile
{
    [Tooltip("Inspector에서 프로필을 구분하기 위한 이름.")]
    public string profileName = "Local";

    [Header("수집")]
    public bool enabled = true;
    public bool captureUnityLogs = true;
    public float flushIntervalSeconds = 5f;

    [Header("전송")]
    public bool uploadEnabled = true;
    public string developmentServerUrl = "http://localhost:5000/api/logs";
    public string releaseServerUrl = "http://localhost:5000/api/logs";
    public string apiKey = "";
    public float uploadIntervalSeconds = 5f;
    public bool retryPendingOnStartup = true;
    public int requestTimeoutSeconds = 5;

    public void Apply(TelemetryData _telemetry)
    {
        if (_telemetry == null)
        {
            return;
        }
        _telemetry.enabled = enabled;
        _telemetry.captureUnityLogs = captureUnityLogs;
        _telemetry.flushIntervalSeconds = flushIntervalSeconds;
        _telemetry.uploadEnabled = uploadEnabled;
        _telemetry.developmentServerUrl = developmentServerUrl;
        _telemetry.releaseServerUrl = releaseServerUrl;
        _telemetry.apiKey = apiKey;
        _telemetry.uploadIntervalSeconds = uploadIntervalSeconds;
        _telemetry.retryPendingOnStartup = retryPendingOnStartup;
        _telemetry.requestTimeoutSeconds = requestTimeoutSeconds;
    }
}
