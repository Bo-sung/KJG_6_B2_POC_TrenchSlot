using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 정비 상점 화면(View). 2열 구성:
// 왼쪽 = 무기 판매(목록·상세·구매) / 선택 무기의 파츠(칩·상세·구매·슬롯별 장착·분리) / 스탯 상시 판매,
// 오른쪽 = 유저 정보(휴대 무기 개체·아킴보 토글·보관품·바닥 무기) / 보급 / 슬롯 업그레이드.
// 줄 수와 배치는 프리팹에서 정하고(PocShopAuthoring), 내용과 활성 상태는 ShopPresenter가 넣는다.
public sealed class ShopView : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text walletText;
    [SerializeField] private TMP_Text intermissionText;
    [SerializeField] private Image intermissionFill;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text userInfoText;
    [SerializeField] private TMP_Text inventoryText;
    [SerializeField] private TMP_Text akimboNoteText;
    [SerializeField] private TMP_Text weaponDetailText;
    [SerializeField] private TMP_Text partDetailText;
    [SerializeField] private Button nextWaveButton;
    [SerializeField] private ScrollRect[] scrolls = Array.Empty<ScrollRect>();
    [SerializeField] private ShopRowView[] weaponRows = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView[] weaponBuyButtons = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView[] partChips = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView partBuyButton;
    [SerializeField] private ShopRowView[] partAttachButtons = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView[] partDetachButtons = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView[] slotCards = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView akimboButton;
    [SerializeField] private ShopRowView[] statRows = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView[] supplyRows = Array.Empty<ShopRowView>();
    [SerializeField] private ShopRowView[] upgradeRows = Array.Empty<ShopRowView>();

    public event Action<int> WeaponSelected;
    // 구매해 넣을 휴대 슬롯(0부터).
    public event Action<int> WeaponBuyClicked;
    public event Action<int> PartSelected;
    public event Action PartBuyClicked;
    // 장착·분리 대상 휴대 슬롯(0부터).
    public event Action<int> PartAttachClicked;
    public event Action<int> PartDetachClicked;
    // 휴대 슬롯 카드. 그 무기를 선택해 파츠를 본다.
    public event Action<int> SlotCardClicked;
    public event Action AkimboClicked;
    public event Action<int> StatClicked;
    public event Action<int> SupplyClicked;
    public event Action<int> UpgradeClicked;
    public event Action NextWaveClicked;

    public int WeaponRowCount => weaponRows.Length;
    public int WeaponBuyCount => weaponBuyButtons.Length;
    public int PartChipCount => partChips.Length;
    public int PartSlotButtonCount => Mathf.Min(partAttachButtons.Length, partDetachButtons.Length);
    public int SlotCardCount => slotCards.Length;
    public int StatRowCount => statRows.Length;
    public int SupplyRowCount => supplyRows.Length;
    public int UpgradeRowCount => upgradeRows.Length;

    private GameObject lastSelected;

    private void Awake()
    {
        Bind(weaponRows, _i => WeaponSelected?.Invoke(_i));
        Bind(weaponBuyButtons, _i => WeaponBuyClicked?.Invoke(_i));
        Bind(partChips, _i => PartSelected?.Invoke(_i));
        Bind(partAttachButtons, _i => PartAttachClicked?.Invoke(_i));
        Bind(partDetachButtons, _i => PartDetachClicked?.Invoke(_i));
        Bind(slotCards, _i => SlotCardClicked?.Invoke(_i));
        Bind(statRows, _i => StatClicked?.Invoke(_i));
        Bind(supplyRows, _i => SupplyClicked?.Invoke(_i));
        Bind(upgradeRows, _i => UpgradeClicked?.Invoke(_i));
        if (partBuyButton != null)
        {
            partBuyButton.Clicked += () => PartBuyClicked?.Invoke();
        }
        if (akimboButton != null)
        {
            akimboButton.Clicked += () => AkimboClicked?.Invoke();
        }
        if (nextWaveButton != null)
        {
            nextWaveButton.onClick.AddListener(() => NextWaveClicked?.Invoke());
        }
    }

    // 게임패드·키보드로 스크롤 목록 밖의 줄을 고르면 그 줄이 보이도록 목록을 옮긴다.
    private void LateUpdate()
    {
        if (panel == null || !panel.activeInHierarchy || EventSystem.current == null)
        {
            return;
        }
        var selected = EventSystem.current.currentSelectedGameObject;
        if (selected == lastSelected)
        {
            return;
        }
        lastSelected = selected;
        if (selected == null)
        {
            return;
        }
        foreach (var scroll in scrolls)
        {
            if (scroll != null && scroll.content != null && selected.transform.IsChildOf(scroll.content))
            {
                ScrollIntoView(scroll, selected.GetComponent<RectTransform>());
                return;
            }
        }
    }

    private static void ScrollIntoView(ScrollRect _scroll, RectTransform _item)
    {
        var viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
        var content = _scroll.content;
        float overflow = content.rect.height - viewport.rect.height;
        if (_item == null || overflow <= 0f)
        {
            return;
        }
        Vector3[] corners = new Vector3[4];
        _item.GetWorldCorners(corners);
        float top = viewport.InverseTransformPoint(corners[1]).y;
        float bottom = viewport.InverseTransformPoint(corners[0]).y;
        float shift = 0f;
        if (top > viewport.rect.yMax)
        {
            shift = top - viewport.rect.yMax;
        }
        else if (bottom < viewport.rect.yMin)
        {
            shift = bottom - viewport.rect.yMin;
        }
        if (shift != 0f)
        {
            // 위로 넘친 줄은 내용을 내리고, 아래로 넘친 줄은 올린다.
            var position = content.anchoredPosition;
            position.y = Mathf.Clamp(position.y - shift, 0f, overflow);
            content.anchoredPosition = position;
        }
    }

    private static void Bind(ShopRowView[] _rows, Action<int> _handler)
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            int index = i;
            if (_rows[i] != null)
            {
                _rows[i].Clicked += () => _handler(index);
            }
        }
    }

    public void SetVisible(bool _visible)
    {
        if (panel == null)
        {
            return;
        }
        panel.SetActive(_visible);
        if (_visible)
        {
            foreach (var scroll in scrolls)
            {
                if (scroll != null)
                {
                    scroll.verticalNormalizedPosition = 1f;
                }
            }
        }
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

    public void SetTitle(string _text)
    {
        SetText(titleText, _text);
    }

    public void SetWallet(int _amount)
    {
        SetText(walletText, $"소지금 ${_amount:N0}");
    }

    public void SetIntermission(float _seconds)
    {
        SetIntermission(_seconds, 0f);
    }

    public void SetIntermission(float _seconds, float _total)
    {
        if (_seconds <= 0f)
        {
            SetText(intermissionText, "");
            if (intermissionFill != null) intermissionFill.fillAmount = 0f;
            return;
        }
        int sec = Mathf.CeilToInt(_seconds);
        int m = sec / 60;
        int s = sec % 60;
        string timeStr = m > 0 ? $"{m:00}:{s:00}" : $"{sec}초";
        SetText(intermissionText, $"정비 시간: {timeStr} (스킵 가능)");
        if (intermissionFill != null)
        {
            float ratio = _total > 0f ? Mathf.Clamp01(_seconds / _total) : 1f;
            intermissionFill.fillAmount = ratio;
        }
    }

    public void SetMessage(string _message)
    {
        SetText(messageText, _message);
    }

    public void SetUserInfo(string _text)
    {
        SetText(userInfoText, _text);
    }

    // 예비탄·보관 파츠·바닥 무기.
    public void SetInventory(string _text)
    {
        SetText(inventoryText, _text);
    }

    public void SetWeaponDetail(string _text)
    {
        SetText(weaponDetailText, _text);
    }

    public void SetWeaponRow(int _index, string _label, string _price, bool _interactable, bool _selected)
    {
        SetRow(weaponRows, _index, _label, _price, _interactable, _selected);
    }

    public void SetWeaponBuy(int _slot, string _label, string _price, bool _interactable)
    {
        SetRow(weaponBuyButtons, _slot, _label, _price, _interactable, false);
    }

    public void SetPartChip(int _index, string _label, string _price, bool _selected)
    {
        SetRow(partChips, _index, _label, _price, !string.IsNullOrEmpty(_label), _selected);
    }

    public void SetPartDetail(string _text)
    {
        SetText(partDetailText, _text);
    }

    public void SetPartBuy(string _label, string _price, bool _interactable)
    {
        if (partBuyButton != null)
        {
            partBuyButton.Set(_label, _price, _interactable);
        }
    }

    public void SetPartAttach(int _slot, string _label, bool _interactable)
    {
        SetRow(partAttachButtons, _slot, _label, "", _interactable, false);
    }

    public void SetPartDetach(int _slot, string _label, bool _interactable)
    {
        SetRow(partDetachButtons, _slot, _label, "", _interactable, false);
    }

    // _selected = 사용 중 슬롯.
    public void SetSlotCard(int _slot, string _label, string _status, bool _interactable, bool _selected)
    {
        SetRow(slotCards, _slot, _label, _status, _interactable, _selected);
    }

    public void SetAkimbo(string _label, string _note, bool _interactable, bool _on)
    {
        if (akimboButton != null)
        {
            akimboButton.Set(_label, "", _interactable, _on);
        }
        SetText(akimboNoteText, _note);
    }

    public void SetStatRow(int _index, string _label, string _price, bool _interactable)
    {
        SetRow(statRows, _index, _label, _price, _interactable, false);
    }

    public void SetSupplyRow(int _index, string _label, string _price, bool _interactable)
    {
        SetRow(supplyRows, _index, _label, _price, _interactable, false);
    }

    public void SetUpgradeRow(int _index, string _label, string _price, bool _interactable)
    {
        SetRow(upgradeRows, _index, _label, _price, _interactable, false);
    }

    // 포커스가 비활성·숨김 버튼에 남아 있으면 다음 웨이브 버튼으로 옮긴다(게임패드 탐색 유지).
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

    private static void SetRow(ShopRowView[] _rows, int _index, string _label, string _price, bool _interactable, bool _selected)
    {
        if (_index >= 0 && _index < _rows.Length && _rows[_index] != null)
        {
            _rows[_index].Set(_label, _price, _interactable, _selected);
        }
    }

    private static void SetText(TMP_Text _target, string _text)
    {
        if (_target != null)
        {
            _target.text = _text;
        }
    }
}
