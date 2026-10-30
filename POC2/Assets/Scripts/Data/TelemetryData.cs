using System;

// Resources/TelemetryData.json의 직렬화 자료. 로직 없음. 조회·병합은 TelemetryInfo가 담당한다.
// 서버 주소·키는 저장소에 올리지 않는다. 로컬 덮어쓰기(Resources/TelemetryLocalData.json, Git 제외)에만 둔다.
[Serializable]
public sealed class TelemetryData
{
    // false면 게임 이벤트 기록과 Unity 콘솔 자동 수집을 모두 끈다.
    public bool enabled = true;
    public bool captureUnityLogs = true;
    // 큐를 파일로 내보내는 주기(초).
    public float flushIntervalSeconds = 5f;
    // 방아쇠 1회마다 Combat/Shot을 남길지. 끄면 Attack 지표만 집계한다.
    public bool logShotEvents = true;
    // 적에게 실제 피해를 준 탄마다 Combat/Hit을 남길지. 끄면 AttackHit 지표만 집계한다.
    public bool logHitEvents = true;

    // 서버 전송. 기본은 꺼짐(로컬 JSONL만 남김).
    public bool uploadEnabled;
    // 빈 문자열이면 LogKit 기본값(http://localhost:5000/api/logs)을 쓴다.
    public string developmentServerUrl = "";
    public string releaseServerUrl = "";
    // 빈 문자열이면 LogKit 기본 개발 키를 쓴다.
    public string apiKey = "";
    public float uploadIntervalSeconds = 5f;
    public int requestTimeoutSeconds = 5;
    // 전송이 켜졌을 때 이전 세션의 미전송 파일을 기동 시 이어서 보낼지.
    public bool retryPendingOnStartup = true;
}
