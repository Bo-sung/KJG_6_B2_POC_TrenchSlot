using System;
using UnityEngine;

// 로그 설정 조회·검증. 저장소 기본값(TelemetryData.json) 위에 Git 제외 로컬 파일(TelemetryLocalData.json)의 항목만 덮어쓴다.
public static class TelemetryInfo
{
    // Resources 경로. 파일이 없으면 덮어쓰지 않으며 오류도 남기지 않는다(선택 사항).
    public const string LOCAL_OVERRIDE_PATH = "TelemetryLocalData";

    public static bool Enabled(TelemetryData _table) => _table != null && _table.enabled;

    public static bool LogShotEvents(TelemetryData _table) => Enabled(_table) && _table.logShotEvents;

    public static bool LogHitEvents(TelemetryData _table) => Enabled(_table) && _table.logHitEvents;

    public static bool UploadEnabled(TelemetryData _table) => Enabled(_table) && _table.uploadEnabled;

    // 빈 값은 null로 돌려 LogKit 기존 값(기본 주소·키)을 유지하게 한다.
    public static string OrNull(string _value) => string.IsNullOrWhiteSpace(_value) ? null : _value.Trim();

    public static float FlushIntervalSeconds(TelemetryData _table) => _table != null && _table.flushIntervalSeconds > 0f ? _table.flushIntervalSeconds : 5f;

    public static float UploadIntervalSeconds(TelemetryData _table) => _table != null ? Mathf.Max(0f, _table.uploadIntervalSeconds) : 0f;

    public static int RequestTimeoutSeconds(TelemetryData _table) => _table != null && _table.requestTimeoutSeconds > 0 ? _table.requestTimeoutSeconds : 5;

    // 로컬 파일에 적힌 필드만 덮어쓴다. 형식이 틀리면 저장소 기본값을 그대로 쓴다.
    public static void ApplyLocalOverride(TelemetryData _table)
    {
        if (_table == null)
        {
            return;
        }
        TextAsset local = Resources.Load<TextAsset>(LOCAL_OVERRIDE_PATH);
        if (local == null)
        {
            return;
        }
        try
        {
            JsonUtility.FromJsonOverwrite(local.text, _table);
        }
        catch (ArgumentException exception)
        {
            Debug.LogWarning($"로컬 로그 설정 {LOCAL_OVERRIDE_PATH}를 읽지 못해 기본값을 씁니다: {exception.Message}");
        }
        finally
        {
            Resources.UnloadAsset(local);
        }
    }

    public static void Validate(TelemetryData _table)
    {
        if (_table == null)
        {
            Debug.LogError("로그 설정 테이블이 없습니다.");
            return;
        }
        if (_table.flushIntervalSeconds <= 0f || _table.requestTimeoutSeconds <= 0 || _table.uploadIntervalSeconds < 0f)
        {
            Debug.LogError("flushIntervalSeconds·requestTimeoutSeconds는 0보다 크고 uploadIntervalSeconds는 0 이상이어야 합니다.");
        }
        if (!IsHttpUrlOrEmpty(_table.developmentServerUrl) || !IsHttpUrlOrEmpty(_table.releaseServerUrl))
        {
            Debug.LogError("로그 서버 주소는 비우거나 http(s):// 로 시작해야 합니다.");
        }
    }

    private static bool IsHttpUrlOrEmpty(string _url)
    {
        if (string.IsNullOrWhiteSpace(_url))
        {
            return true;
        }
        string trimmed = _url.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
