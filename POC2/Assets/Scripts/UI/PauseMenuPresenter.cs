using UnityEngine;
using UnityEngine.InputSystem;

// ESC 일시정지 메뉴(Presenter). ESC로 열고 닫으며, 열린 동안 시간을 멈추고 플레이어 입력을 막는다.
// 게임 오버 중에는 열지 않는다(게임 오버 화면의 타이틀 버튼을 쓴다).
public sealed class PauseMenuPresenter : MonoBehaviour
{
    [SerializeField] private PauseMenuView view;
    [SerializeField] private GameManager gameManager;

    private bool paused;
    private bool inputWasEnabled;
    private CursorLockMode cursorLock;
    private bool cursorVisible;

    private void Start()
    {
        view.ResumeClicked += Resume;
        view.TitleClicked += ReturnToTitle;
    }

    private void OnDestroy()
    {
        if (view != null)
        {
            view.ResumeClicked -= Resume;
            view.TitleClicked -= ReturnToTitle;
        }
        if (paused)
        {
            Time.timeScale = 1f;
        }
    }

    private void Update()
    {
        if (gameManager == null || gameManager.Phase == GameManager.GamePhase.GameOver)
        {
            return;
        }
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return;
        }
        if (paused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    private void Pause()
    {
        paused = true;
        inputWasEnabled = InputGate.PlayerEnabled;
        InputGate.PlayerEnabled = false;
        cursorLock = Cursor.lockState;
        cursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
        view.SetVisible(true);
    }

    private void Resume()
    {
        paused = false;
        Time.timeScale = 1f;
        InputGate.PlayerEnabled = inputWasEnabled;
        Cursor.lockState = cursorLock;
        Cursor.visible = cursorVisible;
        view.SetVisible(false);
    }

    private void ReturnToTitle()
    {
        paused = false;
        view.SetVisible(false);
        TitleReturn.Go(gameManager);
    }
}
