using UnityEngine;
using UnityEngine.SceneManagement;

// 게임 오버 규칙(Presenter). 플레이어 사망 시 결과를 보여 주고 타이틀 씬으로 돌아간다.
public sealed class GameOverPresenter : MonoBehaviour
{
    [SerializeField] private GameOverView view;
    [SerializeField] private GameManager gameManager;

    private void Start()
    {
        gameManager.GameOver += OnGameOver;
        view.TitleClicked += OnTitleClicked;
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.GameOver -= OnGameOver;
        }
        if (view != null)
        {
            view.TitleClicked -= OnTitleClicked;
        }
    }

    private void OnGameOver(int _wave, int _kills)
    {
        view.SetSummary(_wave, _kills);
        view.SetVisible(true);
        InputGate.PlayerEnabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnTitleClicked()
    {
        SoundPlayer.Play(SfxType.Click);
        gameManager.PrepareForTitleReturn();
        // 메인 메뉴로 넘어가기 전에 쌓인 로그를 서버로 보낸다.
        LogKit.GameLogger.FlushNow();
        SceneManager.LoadScene(SceneNames.TITLE);
    }
}
