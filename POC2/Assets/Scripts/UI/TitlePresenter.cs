using UnityEngine;
using UnityEngine.SceneManagement;

// 타이틀 규칙(Presenter). 시작은 Game 씬으로, 종료는 애플리케이션 종료로 보낸다.
public sealed class TitlePresenter : MonoBehaviour
{
    [SerializeField] private TitleView view;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        view.StartClicked += OnStartClicked;
        view.QuitClicked += OnQuitClicked;
    }

    private void OnDestroy()
    {
        if (view == null)
        {
            return;
        }
        view.StartClicked -= OnStartClicked;
        view.QuitClicked -= OnQuitClicked;
    }

    private void OnStartClicked()
    {
        SoundPlayer.Play(SfxType.Click);
        SceneManager.LoadScene(SceneNames.GAME);
    }

    private void OnQuitClicked()
    {
        Application.Quit();
    }
}
