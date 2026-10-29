using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 게임 오버 화면(View).
public sealed class GameOverView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private Button titleButton;

    public event Action TitleClicked;

    private void Awake()
    {
        if (titleButton != null)
        {
            titleButton.onClick.AddListener(() => TitleClicked?.Invoke());
        }
        SetVisible(false);
    }

    public void SetVisible(bool _visible)
    {
        if (panel != null)
        {
            panel.SetActive(_visible);
            if (EventSystem.current != null && _visible && titleButton != null)
                EventSystem.current.SetSelectedGameObject(titleButton.gameObject);
        }
    }

    public void SetSummary(int _wave, int _kills)
    {
        summaryText.text = $"도달 웨이브 {_wave}\n처치 {_kills}";
    }
}
