using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 타이틀 화면(View). 버튼 이벤트만 바깥으로 전달한다.
public sealed class TitleView : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    public event Action StartClicked;
    public event Action QuitClicked;

    private void Awake()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(() => StartClicked?.Invoke());
        }
        if (quitButton != null)
        {
            quitButton.onClick.AddListener(() => QuitClicked?.Invoke());
        }
    }

    private void Start()
    {
        if (EventSystem.current != null && startButton != null)
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }
}
