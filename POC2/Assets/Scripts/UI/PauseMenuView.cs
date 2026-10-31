using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ESC 일시정지 메뉴(View). 계속하기·타이틀로 두 버튼을 가진다.
public sealed class PauseMenuView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button titleButton;

    public event Action ResumeClicked;
    public event Action TitleClicked;

    private void Awake()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.AddListener(() => ResumeClicked?.Invoke());
        }
        if (titleButton != null)
        {
            titleButton.onClick.AddListener(() => TitleClicked?.Invoke());
        }
        SetVisible(false);
    }

    public void SetVisible(bool _visible)
    {
        if (panel == null)
        {
            return;
        }
        panel.SetActive(_visible);
        if (EventSystem.current != null && _visible && resumeButton != null)
        {
            EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }
    }
}
