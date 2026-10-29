using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 정비 상점 화면(View). 칸 수와 순서는 프리팹에서 정하고, 내용은 ShopPresenter가 넣는다.
public sealed class ShopView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text walletText;
    [SerializeField] private TMP_Text intermissionText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text ammoSummaryText;
    [SerializeField] private Button nextWaveButton;
    [SerializeField] private Button[] slotButtons;
    [SerializeField] private TMP_Text[] slotLabels;
    [SerializeField] private TMP_Text[] slotPrices;

    public event Action<int> SlotClicked;
    public event Action NextWaveClicked;

    private void Awake()
    {
        for (int i = 0; i < slotButtons.Length; i++)
        {
            int index = i;
            slotButtons[i].onClick.AddListener(() => SlotClicked?.Invoke(index));
        }
        if (nextWaveButton != null)
        {
            nextWaveButton.onClick.AddListener(() => NextWaveClicked?.Invoke());
        }
    }

    public int SlotCount => slotButtons != null ? slotButtons.Length : 0;

    public void SetVisible(bool _visible)
    {
        if (panel != null)
        {
            panel.SetActive(_visible);
            if (EventSystem.current == null)
            {
                return;
            }
            // 열 때는 다음 웨이브 버튼에 포커스를 준다. 닫을 때는 상점 안에 있던 포커스만 푼다(다른 화면이 잡은 포커스는 건드리지 않는다).
            if (_visible)
            {
                if (nextWaveButton != null)
                {
                    EventSystem.current.SetSelectedGameObject(nextWaveButton.gameObject);
                }
                return;
            }
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(panel.transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }

    public void SetWallet(int _amount)
    {
        walletText.text = $"소지금 ${_amount:N0}";
    }

    public void SetIntermission(float _seconds)
    {
        intermissionText.text = _seconds > 0f ? $"다음 웨이브까지 {Mathf.CeilToInt(_seconds)}초" : "";
    }

    public void SetMessage(string _message)
    {
        messageText.text = _message;
    }

    public void SetAmmoSummary(string _text)
    {
        if (ammoSummaryText != null)
        {
            ammoSummaryText.text = _text;
        }
    }

    public void EnsureValidSelection()
    {
        if (panel == null || !panel.activeInHierarchy || EventSystem.current == null || nextWaveButton == null)
        {
            return;
        }
        var selected = EventSystem.current.currentSelectedGameObject;
        var control = selected != null ? selected.GetComponent<Selectable>() : null;
        if (selected == null || (selected.transform.IsChildOf(panel.transform) &&
            (control == null || !control.IsInteractable() || !control.IsActive())))
        {
            EventSystem.current.SetSelectedGameObject(nextWaveButton.gameObject);
        }
    }

    public void SetSlot(int _index, string _label, string _price, bool _interactable)
    {
        if (_index < 0 || _index >= SlotCount)
        {
            return;
        }
        slotLabels[_index].text = _label;
        slotPrices[_index].text = _price;
        slotButtons[_index].interactable = _interactable;
    }
}
