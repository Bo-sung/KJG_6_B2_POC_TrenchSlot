using System;
using UnityEngine;

// 파츠 한 줄. 본 버튼(구매·분리)과 휴대 슬롯별 장착 버튼으로 이뤄진다.
public sealed class ShopPartRowView : MonoBehaviour
{
    [SerializeField] private ShopRowView main;
    [SerializeField] private ShopRowView[] attachButtons = Array.Empty<ShopRowView>();

    public event Action MainClicked;
    // 장착 대상 휴대 슬롯(0부터).
    public event Action<int> AttachClicked;

    public int AttachCount => attachButtons.Length;

    private void Awake()
    {
        if (main != null)
        {
            main.Clicked += () => MainClicked?.Invoke();
        }
        for (int i = 0; i < attachButtons.Length; i++)
        {
            int slot = i;
            if (attachButtons[i] != null)
            {
                attachButtons[i].Clicked += () => AttachClicked?.Invoke(slot);
            }
        }
    }

    public void SetVisible(bool _visible)
    {
        gameObject.SetActive(_visible);
    }

    public void SetMain(string _label, string _price, bool _interactable)
    {
        if (main != null)
        {
            main.Set(_label, _price, _interactable);
        }
    }

    public void SetAttach(int _slot, string _label, bool _interactable)
    {
        if (_slot >= 0 && _slot < attachButtons.Length && attachButtons[_slot] != null)
        {
            attachButtons[_slot].Set(_label, "", _interactable);
        }
    }
}
