using LogKit;
using UnityEngine;
using UnityEngine.SceneManagement;

// 타이틀로 돌아가는 유일한 경로(게임 오버·일시정지 메뉴·Preload 공통).
// 게임 기록을 먼저 닫아 LogKit 큐로 넘기고, 큐를 서버 전송까지 비운 뒤 Title 씬을 연다.
public static class TitleReturn
{
    public static void Go(GameManager _gameManager)
    {
        Time.timeScale = 1f;
        _gameManager?.PrepareForTitleReturn();
        GameLogger.FlushNow();
        SceneManager.LoadScene(SceneNames.TITLE);
    }
}
