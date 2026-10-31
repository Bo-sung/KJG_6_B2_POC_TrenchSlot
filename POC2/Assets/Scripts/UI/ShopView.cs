using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ShopPointerTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public event Action Entered;
    public event Action Exited;

    public void OnPointerEnter(PointerEventData _eventData) => Entered?.Invoke();
    public void OnPointerExit(PointerEventData _eventData) => Exited?.Invoke();
}

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
    [SerializeField] private Image weaponImage;
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
    // 포커스(선택된 UI)가 바뀐 순간. (이전, 다음, 화면 코드가 옮겼는지). 같은 포커스가 유지되는 프레임에는 오지 않는다.
    public event Action<GameObject, GameObject, bool> FocusChanged;
    // 마우스 포인터 진입·이탈.
    public event Action<GameObject> PointerEntered;
    public event Action<GameObject> PointerExited;
    // 스크롤 위치 변화 (뷰 이름, 이전 정규화 위치, 새 정규화 위치, 화면 코드가 옮겼는지).
    public event Action<string, float?, float, bool> ScrollChanged;

    public int WeaponRowCount => weaponRows.Length;
    public int WeaponBuyCount => weaponBuyButtons.Length;
    public int PartChipCount => partChips.Length;
    public int PartSlotButtonCount => Mathf.Min(partAttachButtons.Length, partDetachButtons.Length);
    public int SlotCardCount => slotCards.Length;
    public int StatRowCount => statRows.Length;
    public int SupplyRowCount => supplyRows.Length;
    public int UpgradeRowCount => upgradeRows.Length;

    private GameObject lastSelected;
    private GameObject programmaticFocus;
    private GameObject logFocus;
    private GameObject currentHovered;
    private float[] lastScrollPositions = Array.Empty<float>();
    private float[] scrollStartPositions = Array.Empty<float>();
    private bool[] isScrolling = Array.Empty<bool>();
    private int[] stationaryFrames = Array.Empty<int>();
    private readonly Vector3[] logCorners = new Vector3[4];
    private readonly List<CanvasGroup> logGroups = new List<CanvasGroup>();
    private readonly List<RectMask2D> logRectMasks = new List<RectMask2D>();
    private readonly List<Mask> logMasks = new List<Mask>();

    public bool IsLogVisible => panel != null && IsLogRectVisible(panel.transform as RectTransform);

    public LogShopRowState CaptureLogRow(string _placement, int _index)
    {
        if (!IsLogVisible)
        {
            return null;
        }
        if (_placement == "next_wave")
        {
            return nextWaveButton != null && IsLogRectVisible(nextWaveButton.transform as RectTransform)
                ? new LogShopRowState { label = nextWaveButton.GetComponentInChildren<TMP_Text>()?.text, interactable = nextWaveButton.IsInteractable() } : null;
        }
        if (_placement == "weapon_detail" || _placement == "part_detail")
        {
            TMP_Text text = _placement == "weapon_detail" ? weaponDetailText : partDetailText;
            return text != null && IsLogRectVisible(text.rectTransform)
                ? new LogShopRowState { label = text.text } : null;
        }
        ShopRowView[] rows = null;
        switch (_placement)
        {
            case "weapon_catalog": rows = weaponRows; break;
            case "weapon_buy": rows = weaponBuyButtons; break;
            case "part_catalog": rows = partChips; break;
            case "stat": rows = statRows; break;
            case "ammo": rows = supplyRows; break;
            case "upgrade": rows = upgradeRows; break;
            case "part_attach": rows = partAttachButtons; break;
            case "part_detach": rows = partDetachButtons; break;
            case "slot_card": rows = slotCards; break;
        }
        ShopRowView row = _placement == "part_buy" ? partBuyButton : _placement == "akimbo" ? akimboButton :
            rows != null && _index >= 0 && _index < rows.Length ? rows[_index] : null;
        if (row == null || !IsLogRectVisible(row.transform as RectTransform))
        {
            return null;
        }
        return new LogShopRowState { label = row.LogLabel, priceText = row.LogPrice,
            interactable = row.Button != null && row.Button.IsInteractable() };
    }

    // 로그용 UI 위치. 상점 버튼이 아니면 placement=null을 돌려준다.
    public string LogPlacementOf(GameObject _target, out int _row)
    {
        _row = -1;
        if (_target == null)
        {
            return null;
        }
        if (nextWaveButton != null && _target == nextWaveButton.gameObject)
        {
            return "next_wave";
        }
        if (partBuyButton != null && (_target == partBuyButton.Button?.gameObject || _target == partBuyButton.gameObject))
        {
            return "part_buy";
        }
        if (akimboButton != null && (_target == akimboButton.Button?.gameObject || _target == akimboButton.gameObject))
        {
            return "akimbo";
        }
        string[] names = { "weapon_catalog", "weapon_buy", "part_catalog", "part_attach", "part_detach", "slot_card", "stat", "ammo", "upgrade" };
        ShopRowView[][] groups = { weaponRows, weaponBuyButtons, partChips, partAttachButtons, partDetachButtons, slotCards, statRows, supplyRows, upgradeRows };
        for (int g = 0; g < groups.Length; g++)
        {
            for (int i = 0; i < groups[g].Length; i++)
            {
                if (groups[g][i] != null && (_target == groups[g][i].Button?.gameObject || _target == groups[g][i].gameObject))
                {
                    _row = i;
                    return names[g];
                }
            }
        }
        return panel != null && _target.transform.IsChildOf(panel.transform) ? "shop_other" : null;
    }

    public string LogViewOf(ScrollRect _scroll)
    {
        if (_scroll == null)
        {
            return null;
        }
        string name = _scroll.gameObject.name;
        if (name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "weapon_catalog";
        }
        if (name.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "inventory";
        }
        if (name.IndexOf("Supply", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "ammo";
        }
        return name;
    }

    private bool IsLogRectVisible(RectTransform _rect)
    {
        if (_rect == null || !_rect.gameObject.activeInHierarchy || _rect.rect.width <= 0f || _rect.rect.height <= 0f)
        {
            return false;
        }
        _rect.GetComponentsInParent(false, logGroups);
        foreach (CanvasGroup group in logGroups)
        {
            if (group.alpha <= 0f)
            {
                return false;
            }
        }
        _rect.GetWorldCorners(logCorners);
        _rect.GetComponentsInParent(false, logRectMasks);
        foreach (RectMask2D mask in logRectMasks)
        {
            if (mask.isActiveAndEnabled && !LogOverlaps(mask.rectTransform))
            {
                return false;
            }
        }
        _rect.GetComponentsInParent(false, logMasks);
        foreach (Mask mask in logMasks)
        {
            if (mask.isActiveAndEnabled && !LogOverlaps(mask.transform as RectTransform))
            {
                return false;
            }
        }
        foreach (ScrollRect scroll in scrolls)
        {
            if (scroll != null && scroll.content != null && _rect.IsChildOf(scroll.content) &&
                !LogOverlaps(scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform))
            {
                return false;
            }
        }
        Canvas canvas = _rect.GetComponentInParent<Canvas>();
        return canvas != null && canvas.isActiveAndEnabled && LogOverlaps(canvas.rootCanvas.transform as RectTransform);
    }

    private bool LogOverlaps(RectTransform _clip)
    {
        if (_clip == null)
        {
            return true;
        }
        Vector3 first = _clip.InverseTransformPoint(logCorners[0]);
        float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
        for (int i = 1; i < logCorners.Length; i++)
        {
            Vector3 point = _clip.InverseTransformPoint(logCorners[i]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }
        return LogShopVisibility.Overlaps(minX, maxX, minY, maxY,
            _clip.rect.xMin, _clip.rect.xMax, _clip.rect.yMin, _clip.rect.yMax);
    }

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
            var trigger = nextWaveButton.gameObject.AddComponent<ShopPointerTrigger>();
            trigger.Entered += () => OnPointerEntered(nextWaveButton.gameObject);
            trigger.Exited += () => OnPointerExited(nextWaveButton.gameObject);
        }

        BindPointer(weaponRows);
        BindPointer(weaponBuyButtons);
        BindPointer(partChips);
        BindPointer(partAttachButtons);
        BindPointer(partDetachButtons);
        BindPointer(slotCards);
        BindPointer(statRows);
        BindPointer(supplyRows);
        BindPointer(upgradeRows);
        BindPointerRow(partBuyButton);
        BindPointerRow(akimboButton);

        lastScrollPositions = new float[scrolls.Length];
        scrollStartPositions = new float[scrolls.Length];
        isScrolling = new bool[scrolls.Length];
        stationaryFrames = new int[scrolls.Length];
        for (int i = 0; i < scrolls.Length; i++)
        {
            if (scrolls[i] != null)
            {
                lastScrollPositions[i] = scrolls[i].verticalNormalizedPosition;
                scrollStartPositions[i] = scrolls[i].verticalNormalizedPosition;
            }
        }
    }

    private void BindPointer(ShopRowView[] _rows)
    {
        if (_rows == null)
        {
            return;
        }
        foreach (var row in _rows)
        {
            BindPointerRow(row);
        }
    }

    private void BindPointerRow(ShopRowView _row)
    {
        if (_row != null)
        {
            _row.PointerEntered += () => OnPointerEntered(_row.gameObject);
            _row.PointerExited += () => OnPointerExited(_row.gameObject);
        }
    }

    private void OnPointerEntered(GameObject _target)
    {
        if (_target == null || !IsLogVisible)
        {
            return;
        }
        if (currentHovered == _target)
        {
            return;
        }
        currentHovered = _target;
        PointerEntered?.Invoke(_target);
    }

    private void OnPointerExited(GameObject _target)
    {
        if (currentHovered == _target)
        {
            currentHovered = null;
        }
        if (IsLogVisible)
        {
            PointerExited?.Invoke(_target);
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
        if (selected != logFocus)
        {
            var previous = logFocus;
            logFocus = selected;
            bool programmatic = selected != null && selected == programmaticFocus;
            programmaticFocus = null;
            FocusChanged?.Invoke(previous, selected, programmatic);
        }

        for (int i = 0; i < scrolls.Length; i++)
        {
            var scroll = scrolls[i];
            if (scroll == null)
            {
                continue;
            }
            float current = scroll.verticalNormalizedPosition;
            float last = lastScrollPositions[i];
            if (Mathf.Abs(current - last) > 1e-4f)
            {
                if (!isScrolling[i])
                {
                    isScrolling[i] = true;
                    scrollStartPositions[i] = last;
                }
                lastScrollPositions[i] = current;
                stationaryFrames[i] = 0;
            }
            else if (isScrolling[i])
            {
                stationaryFrames[i]++;
                if (stationaryFrames[i] >= 2)
                {
                    isScrolling[i] = false;
                    float start = scrollStartPositions[i];
                    if (Mathf.Abs(start - current) > 1e-4f)
                    {
                        ScrollChanged?.Invoke(LogViewOf(scroll), start, current, false);
                    }
                }
            }
        }

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

    private void ScrollIntoView(ScrollRect _scroll, RectTransform _item)
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
            float beforeNorm = _scroll.verticalNormalizedPosition;
            // 위로 넘친 줄은 내용을 내리고, 아래로 넘친 줄은 올린다.
            var position = content.anchoredPosition;
            position.y = Mathf.Clamp(position.y - shift, 0f, overflow);
            content.anchoredPosition = position;
            float afterNorm = _scroll.verticalNormalizedPosition;
            int idx = Array.IndexOf(scrolls, _scroll);
            if (idx >= 0 && idx < lastScrollPositions.Length)
            {
                lastScrollPositions[idx] = afterNorm;
                isScrolling[idx] = false;
            }
            if (Mathf.Abs(beforeNorm - afterNorm) > 1e-4f)
            {
                ScrollChanged?.Invoke(LogViewOf(_scroll), beforeNorm, afterNorm, true);
            }
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
        logFocus = null;
        programmaticFocus = null;
        if (_visible)
        {
            for (int i = 0; i < scrolls.Length; i++)
            {
                var scroll = scrolls[i];
                if (scroll != null)
                {
                    float before = scroll.verticalNormalizedPosition;
                    scroll.verticalNormalizedPosition = 1f;
                    if (i < lastScrollPositions.Length)
                    {
                        lastScrollPositions[i] = 1f;
                        isScrolling[i] = false;
                        stationaryFrames[i] = 0;
                    }
                    if (Mathf.Abs(before - 1f) > 1e-4f)
                    {
                        ScrollChanged?.Invoke(LogViewOf(scroll), before, 1f, true);
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < scrolls.Length; i++)
            {
                if (i < isScrolling.Length && isScrolling[i] && scrolls[i] != null)
                {
                    isScrolling[i] = false;
                    float start = scrollStartPositions[i];
                    float current = scrolls[i].verticalNormalizedPosition;
                    if (Mathf.Abs(start - current) > 1e-4f)
                    {
                        ScrollChanged?.Invoke(LogViewOf(scrolls[i]), start, current, false);
                    }
                }
            }
            if (currentHovered != null)
            {
                var prev = currentHovered;
                currentHovered = null;
                PointerExited?.Invoke(prev);
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
                programmaticFocus = nextWaveButton.gameObject;
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

    // 선택 무기 이미지. 이미지가 없으면 칸을 숨긴다.
    public void SetWeaponImage(Sprite _sprite)
    {
        if (weaponImage == null)
        {
            return;
        }
        weaponImage.sprite = _sprite;
        weaponImage.gameObject.SetActive(_sprite != null);
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
            programmaticFocus = nextWaveButton.gameObject;
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
