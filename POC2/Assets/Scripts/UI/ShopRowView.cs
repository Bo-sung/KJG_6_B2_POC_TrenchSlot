using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 상점 한 줄(버튼 + 이름 + 가격). 선택·사용 중 줄은 밝은 청색으로 칠한다. 구조는 프리팹에서 정하고 내용은 ShopPresenter가 ShopView를 거쳐 넣는다.
public sealed class ShopRowView : MonoBehaviour
{
    private static readonly Color SELECTED_COLOR = new Color(0.212f, 0.373f, 0.475f, 1f);

    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text price;
    // 선택되지 않은 줄의 바탕색. 구매 버튼·휴대 슬롯 카드처럼 줄마다 다르다.
    [SerializeField] private Color normalColor = new Color(0.188f, 0.275f, 0.353f, 1f);

    public event Action Clicked;

    public Button Button => button;

    private void Awake()
    {
        if (button != null)
        {
            button.onClick.AddListener(() => Clicked?.Invoke());
        }
    }

    // 빈 줄은 숨긴다.
    public void Set(string _label, string _price, bool _interactable, bool _selected = false)
    {
        bool visible = !string.IsNullOrEmpty(_label);
        gameObject.SetActive(visible);
        if (!visible)
        {
            return;
        }
        if (label != null)
        {
            label.text = _label;
        }
        if (price != null)
        {
            price.text = _price;
        }
        if (button != null)
        {
            button.interactable = _interactable;
        }
        if (background != null)
        {
            background.color = _selected ? SELECTED_COLOR : normalColor;
        }
    }
}
