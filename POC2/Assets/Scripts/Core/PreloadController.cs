using UnityEngine;
using UnityEngine.SceneManagement;

// Preload 씬(빌드 순서 0). 테이블을 읽은 뒤 빌드 프로필의 로그 설정을 적용하고 Title 씬으로 넘긴다.
public sealed class PreloadController : MonoBehaviour
{
    [Header("로그 / LogKit 프로필")]
    [SerializeField] private TelemetryProfile telemetryProfile = new TelemetryProfile();

    private void Awake()
    {
        // LogKit AutoBootstrap(AfterSceneLoad)보다 먼저 실행되어야 주소·키가 로거 생성 전에 적용된다.
        TableManager.Initialize();
        telemetryProfile?.Apply(TableManager.Instance.Telemetry);
        TelemetryInfo.Validate(TableManager.Instance.Telemetry);
        LogKitBootstrap.Apply(TableManager.Instance.Telemetry);
    }

    private void Start()
    {
        // 메인 메뉴로 넘어가기 전에 쌓인 로그를 서버로 보낸다.
        LogKit.GameLogger.FlushNow();
        SceneManager.LoadScene(SceneNames.TITLE);
    }
}
