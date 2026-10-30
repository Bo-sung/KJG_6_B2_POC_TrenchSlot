using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 구역형 정비 상점 프리팹과 무기 슬롯 연결을 에디터에서 굽는다. 런타임 UI 생성은 하지 않는다.
// 메뉴: POC/Build Shop UI (상점 프리팹만), POC/Build Shop And Weapons (상점 + 무기 외형 프리팹 + Game 씬 연결).
// CLI: -executeMethod PocShopAuthoring.AuthorAll
public static class PocShopAuthoring
{
    public const string SHOP_PREFAB_PATH = PocAssetBuilder.UI_PREFAB_DIR + "/Shop.prefab";
    private const string GAME_SCENE = "Assets/Scenes/Game.unity";

    // 줄 수 하한. 테이블이 더 길면 테이블 길이를 쓴다.
    private const int PART_ROWS = 5;
    private const int UPGRADE_ROWS = 2;

    // 1920×1080 기준 배치. 화면이 작거나 비율이 달라도 Expand 배율로 전체가 화면 안에 들어온다.
    private const float WIDTH = 1920f;
    private const float HEIGHT = 1080f;
    private const float MARGIN = 24f;
    private const float GAP = 14f;
    private const float LEFT_WIDTH = 1236f;
    private const float RIGHT_X = MARGIN + LEFT_WIDTH + GAP;
    private const float RIGHT_WIDTH = WIDTH - MARGIN - RIGHT_X;
    private const float PAD = 12f;
    private const float SCROLLBAR_WIDTH = 12f;

    private static readonly Color BACKGROUND_COLOR = new Color(0.063f, 0.09f, 0.133f, 0.98f);
    private static readonly Color SECTION_COLOR = new Color(0.11f, 0.161f, 0.22f, 1f);
    private static readonly Color INNER_COLOR = new Color(0.078f, 0.125f, 0.176f, 1f);
    private static readonly Color BORDER_COLOR = new Color(0.224f, 0.29f, 0.361f, 1f);
    private static readonly Color ROW_COLOR = new Color(0.188f, 0.275f, 0.353f, 1f);
    private static readonly Color PRIMARY_COLOR = new Color(0.157f, 0.427f, 0.384f, 1f);
    private static readonly Color HEADER_COLOR = new Color(0.843f, 0.898f, 0.961f, 1f);
    private static readonly Color MUTED_COLOR = new Color(0.682f, 0.737f, 0.804f, 1f);
    private static readonly Color PRICE_COLOR = new Color(1f, 0.863f, 0.549f, 1f);

    [MenuItem("POC/Build Shop UI")]
    public static void BuildShopOnly()
    {
        PocAssetBuilder.EnsureFolders();
        PocAssetBuilder.LoadFonts();
        BuildShopPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[PocShopAuthoring] Shop prefab rebuilt");
    }

    [MenuItem("POC/Build Shop And Weapons")]
    public static void AuthorAll()
    {
        PocAssetBuilder.EnsureFolders();
        PocAssetBuilder.LoadFonts();
        PocWeaponDropAuthoring.AuthorAll();
        PocAssetBuilder.InputRef("Player/WeaponSlot1", "WeaponSlot1Ref");
        PocAssetBuilder.InputRef("Player/WeaponSlot2", "WeaponSlot2Ref");
        BuildShopPrefab();
        AssetDatabase.SaveAssets();
        WireGameScene();
        Debug.Log("[PocShopAuthoring] AuthorAll complete");
    }

    public static void BuildShopPrefab()
    {
        TableManager.ReloadAfterTableEdit();
        var tables = TableManager.Instance;
        var shop = tables.Shop;
        int slotCount = GameConfigInfo.WeaponSlotCount(tables.GameConfig);

        var canvas = PocAssetBuilder.CanvasRoot("Shop", 10);
        // 기준 해상도 전체가 항상 화면 안에 들어오도록 한다(4:3·작은 창에서도 잘리지 않음).
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var view = canvas.gameObject.AddComponent<ShopView>();
        var panel = PocAssetBuilder.Panel(canvas.transform, "Panel", BACKGROUND_COLOR);
        var layout = PocAssetBuilder.UiObject("Layout", panel.transform);
        PocAssetBuilder.Place(layout.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(WIDTH, HEIGHT));
        var root = layout.transform;

        // 머리글: 제목·남은 정비 시간 / 소지금.
        var title = Text(root, "Title", "정비 시간", 34, MARGIN, -16f, 1200f, 48f, Color.white);
        var intermission = Text(root, "Intermission", "", 22, MARGIN, -58f, 900f, 26f, MUTED_COLOR, false);
        var barBg = PocAssetBuilder.Panel(root, "IntermissionBarBg", new Color(0.12f, 0.14f, 0.18f, 0.85f));
        PocAssetBuilder.Place(barBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(MARGIN, -86f), new Vector2(500f, 10f));
        var fillObject = PocAssetBuilder.UiObject("IntermissionFill", barBg.transform);
        var fill = fillObject.AddComponent<Image>();
        fill.color = new Color(0.96f, 0.68f, 0.22f);
        fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        PocAssetBuilder.Stretch(fill.rectTransform);
        var wallet = PocAssetBuilder.Label(root, "Wallet", "소지금", 34, TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-MARGIN, -22f), new Vector2(600f, 50f));
        wallet.color = PRICE_COLOR;
        var scrolls = new System.Collections.Generic.List<ScrollRect>();

        // 왼쪽 위: 무기 판매(목록 | 상세·구매) + 선택 무기의 파츠.
        var weaponSection = Region(root, "Weapons", "무기 판매", MARGIN, -104f, LEFT_WIDTH, 660f);
        float half = (LEFT_WIDTH - PAD * 3f) / 2f;
        var weaponList = ScrollList(weaponSection, "WeaponList", PAD, -52f, half, 360f, 1, 60f, 6f, scrolls);
        var weaponRows = new ShopRowView[shop.weapons.Length];
        for (int i = 0; i < weaponRows.Length; i++)
        {
            weaponRows[i] = Row(weaponList, $"Weapon{i}", 0f, 0f, half - SCROLLBAR_WIDTH - 4f, 60f, 20, 20);
        }

        var detailBox = Box(weaponSection, "WeaponDetail", PAD * 2f + half, -52f, half, 360f);
        var detail = Text(detailBox, "Detail", "", 20, PAD, -10f, half - PAD * 2f, 218f, Color.white, false);
        detail.textWrappingMode = TextWrappingModes.Normal;
        detail.overflowMode = TextOverflowModes.Ellipsis;
        var buyButtons = new ShopRowView[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            buyButtons[i] = Row(detailBox, $"Buy{i}", PAD, -236f - i * 62f, half - PAD * 2f, 56f, 20, 22, -1f, PRIMARY_COLOR);
        }

        float innerWidth = LEFT_WIDTH - PAD * 2f;
        Text(weaponSection, "PartsHeader", "선택 무기의 파츠", 22, PAD, -422f, innerWidth, 34f, HEADER_COLOR);
        int chipCount = Mathf.Max(PART_ROWS, MaxPartsPerWeapon(tables.Part));
        float chipWidth = (innerWidth - (chipCount - 1) * 8f) / chipCount;
        var partChips = new ShopRowView[chipCount];
        for (int i = 0; i < chipCount; i++)
        {
            partChips[i] = Row(weaponSection, $"PartChip{i}", PAD + i * (chipWidth + 8f), -460f, chipWidth, 52f, 18, 18);
        }
        var partBox = Box(weaponSection, "PartDetail", PAD, -520f, innerWidth, 128f);
        var partDetail = Text(partBox, "Detail", "", 19, PAD, -8f, 640f, 112f, Color.white, false);
        partDetail.textWrappingMode = TextWrappingModes.Normal;
        partDetail.overflowMode = TextOverflowModes.Ellipsis;
        float actionX = 670f;
        float actionWidth = innerWidth - actionX - PAD;
        var partBuy = Row(partBox, "Buy", actionX, -8f, actionWidth, 34f, 18, 20, -1f, PRIMARY_COLOR);
        float slotButtonWidth = (actionWidth - (slotCount - 1) * 8f) / slotCount;
        var attachButtons = new ShopRowView[slotCount];
        var detachButtons = new ShopRowView[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            float x = actionX + i * (slotButtonWidth + 8f);
            attachButtons[i] = Row(partBox, $"Attach{i}", x, -48f, slotButtonWidth, 34f, 17, 17, 0f);
            detachButtons[i] = Row(partBox, $"Detach{i}", x, -88f, slotButtonWidth, 34f, 17, 17, 0f);
        }

        // 왼쪽 아래: 스탯 상시 판매(가로 카드).
        var statSection = Region(root, "Stats", "스탯 상시 판매", MARGIN, -776f, LEFT_WIDTH, 212f);
        var statRows = new ShopRowView[shop.stats.Length];
        float statWidth = (innerWidth - (statRows.Length - 1) * 8f) / Mathf.Max(1, statRows.Length);
        for (int i = 0; i < statRows.Length; i++)
        {
            statRows[i] = Card(statSection, $"Stat{i}", PAD + i * (statWidth + 8f), -52f, statWidth, 148f);
        }

        // 오른쪽 위: 유저 정보(요약 / 휴대 무기 개체 / 아킴보 / 예비탄·보관 파츠·바닥 무기).
        float rightInner = RIGHT_WIDTH - PAD * 2f;
        var infoSection = Region(root, "UserInfoSection", "유저 정보", RIGHT_X, -104f, RIGHT_WIDTH, 500f);
        var userInfo = Text(infoSection, "UserInfo", "", 19, PAD, -48f, rightInner, 52f, MUTED_COLOR, false);
        userInfo.textWrappingMode = TextWrappingModes.Normal;
        var slotCards = new ShopRowView[slotCount];
        float cardHeight = Mathf.Min(92f, (200f - (slotCount - 1) * 6f) / slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            slotCards[i] = Row(infoSection, $"Slot{i}", PAD, -104f - i * (cardHeight + 6f), rightInner, cardHeight, 18, 18, 170f, INNER_COLOR);
        }
        var akimbo = Row(infoSection, "Akimbo", PAD, -310f, 250f, 44f, 19, 19, 0f);
        var akimboNote = Text(infoSection, "AkimboNote", "", 16, PAD + 262f, -310f, rightInner - 262f, 44f, MUTED_COLOR, false);
        akimboNote.textWrappingMode = TextWrappingModes.Normal;
        akimboNote.alignment = TextAlignmentOptions.MidlineLeft;
        var inventory = TextScroll(infoSection, "Inventory", PAD, -362f, rightInner, 126f, 18, scrolls);

        // 오른쪽 가운데: 보급(체력 회복 + 탄약 팩, 2열 스크롤).
        var supplySection = Region(root, "Supplies", "보급", RIGHT_X, -616f, RIGHT_WIDTH, 176f);
        float supplyWidth = (rightInner - SCROLLBAR_WIDTH - 4f - 8f) / 2f;
        var supplyList = ScrollList(supplySection, "SupplyList", PAD, -50f, rightInner, 114f, 2, 52f, 6f, scrolls, supplyWidth);
        var supplyRows = new ShopRowView[shop.ammoPacks.Length];
        for (int i = 0; i < supplyRows.Length; i++)
        {
            supplyRows[i] = Row(supplyList, $"Supply{i}", 0f, 0f, supplyWidth, 52f, 17, 18, 80f);
        }

        // 오른쪽 아래: 슬롯 업그레이드.
        var upgradeSection = Region(root, "Upgrades", "슬롯 업그레이드", RIGHT_X, -804f, RIGHT_WIDTH, 184f);
        var upgradeRows = new ShopRowView[Mathf.Max(UPGRADE_ROWS, ShopInfo.SlotOfferCount(shop))];
        float upgradeWidth = (rightInner - (upgradeRows.Length - 1) * 8f) / upgradeRows.Length;
        for (int i = 0; i < upgradeRows.Length; i++)
        {
            upgradeRows[i] = Card(upgradeSection, $"Upgrade{i}", PAD + i * (upgradeWidth + 8f), -52f, upgradeWidth, 124f);
        }

        // 바닥글: 결과 메시지 / 다음 웨이브.
        var message = PocAssetBuilder.Label(root, "Message", "", 22, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(MARGIN, 20f), new Vector2(1400f, 56f), false);
        message.color = PRICE_COLOR;
        var nextWave = PocAssetBuilder.MakeButton(root, "NextWave", "다음 웨이브", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-MARGIN, 18f), new Vector2(340f, 62f));
        nextWave.targetGraphic.color = PRIMARY_COLOR;
        nextWave.GetComponentInChildren<TMP_Text>().fontSize = 30;

        var so = new SerializedObject(view);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("walletText").objectReferenceValue = wallet;
        so.FindProperty("intermissionText").objectReferenceValue = intermission;
        so.FindProperty("intermissionFill").objectReferenceValue = fill;
        so.FindProperty("messageText").objectReferenceValue = message;
        so.FindProperty("userInfoText").objectReferenceValue = userInfo;
        so.FindProperty("inventoryText").objectReferenceValue = inventory;
        so.FindProperty("akimboNoteText").objectReferenceValue = akimboNote;
        so.FindProperty("weaponDetailText").objectReferenceValue = detail;
        so.FindProperty("partDetailText").objectReferenceValue = partDetail;
        so.FindProperty("nextWaveButton").objectReferenceValue = nextWave;
        so.FindProperty("partBuyButton").objectReferenceValue = partBuy;
        so.FindProperty("akimboButton").objectReferenceValue = akimbo;
        AssignArray(so.FindProperty("scrolls"), scrolls.ToArray());
        AssignArray(so.FindProperty("weaponRows"), weaponRows);
        AssignArray(so.FindProperty("weaponBuyButtons"), buyButtons);
        AssignArray(so.FindProperty("partChips"), partChips);
        AssignArray(so.FindProperty("partAttachButtons"), attachButtons);
        AssignArray(so.FindProperty("partDetachButtons"), detachButtons);
        AssignArray(so.FindProperty("slotCards"), slotCards);
        AssignArray(so.FindProperty("statRows"), statRows);
        AssignArray(so.FindProperty("supplyRows"), supplyRows);
        AssignArray(so.FindProperty("upgradeRows"), upgradeRows);
        so.ApplyModifiedPropertiesWithoutUndo();

        PocAssetBuilder.SavePrefab(canvas.gameObject, SHOP_PREFAB_PATH);
    }

    // Game 씬의 상점 Presenter·GameManager·PlayerWeapon 참조를 다시 잇는다. 같은 씬에 여러 번 돌려도 결과가 같다.
    public static void WireGameScene()
    {
        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var presenter = Object.FindFirstObjectByType<ShopPresenter>(FindObjectsInactive.Include);
        var shopView = Object.FindFirstObjectByType<ShopView>(FindObjectsInactive.Include);
        var gameManager = Object.FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        var weapon = Object.FindFirstObjectByType<PlayerWeapon>(FindObjectsInactive.Include);
        var movement = Object.FindFirstObjectByType<PlayerMovement>(FindObjectsInactive.Include);
        if (presenter == null || shopView == null || gameManager == null || weapon == null)
        {
            Debug.LogError("[PocShopAuthoring] Game 씬에서 ShopPresenter/ShopView/GameManager/PlayerWeapon 중 찾지 못한 것이 있습니다.");
            return;
        }
        var presenterSo = new SerializedObject(presenter);
        presenterSo.FindProperty("view").objectReferenceValue = shopView;
        presenterSo.ApplyModifiedPropertiesWithoutUndo();

        var gmSo = new SerializedObject(gameManager);
        gmSo.FindProperty("movement").objectReferenceValue = movement;
        var catalog = Object.FindFirstObjectByType<WeaponPickupCatalog>(FindObjectsInactive.Include);
        var held = Object.FindFirstObjectByType<WeaponHeldModels>(FindObjectsInactive.Include);
        if (catalog != null)
        {
            gmSo.FindProperty("pickups").objectReferenceValue = catalog;
        }
        if (held != null)
        {
            gmSo.FindProperty("heldModels").objectReferenceValue = held;
        }
        gmSo.ApplyModifiedPropertiesWithoutUndo();

        var weaponSo = new SerializedObject(weapon);
        AssignArray(weaponSo.FindProperty("slotActions"), new[]
        {
            AssetDatabase.LoadAssetAtPath<InputActionReference>($"{PocAssetBuilder.INPUT_DIR}/WeaponSlot1Ref.asset"),
            AssetDatabase.LoadAssetAtPath<InputActionReference>($"{PocAssetBuilder.INPUT_DIR}/WeaponSlot2Ref.asset")
        });
        weaponSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PocShopAuthoring] Game 씬 상점·무기 슬롯 연결을 적용했습니다.");
    }

    private static int MaxPartsPerWeapon(PartData _parts)
    {
        int max = 0;
        foreach (var weapon in TableManager.Instance.Weapon.weapons)
        {
            if (weapon != null)
            {
                max = Mathf.Max(max, PartInfo.ForWeapon(_parts, weapon.id).Count);
            }
        }
        return max;
    }

    // 기준 배치 안의 구역(좌상단 기준). 테두리와 머리글을 둔다.
    private static Transform Region(Transform _parent, string _name, string _header, float _x, float _y, float _width, float _height)
    {
        var section = Box(_parent, _name, _x, _y, _width, _height, SECTION_COLOR);
        var header = Text(section, "Header", _header, 22, PAD + 4f, -10f, _width - PAD * 2f, 34f, HEADER_COLOR);
        header.characterSpacing = 4f;
        return section;
    }

    private static Transform Box(Transform _parent, string _name, float _x, float _y, float _width, float _height)
    {
        return Box(_parent, _name, _x, _y, _width, _height, INNER_COLOR);
    }

    private static Transform Box(Transform _parent, string _name, float _x, float _y, float _width, float _height, Color _color)
    {
        var box = PocAssetBuilder.UiObject(_name, _parent);
        var image = box.AddComponent<Image>();
        image.color = _color;
        image.raycastTarget = false;
        var outline = box.AddComponent<Outline>();
        outline.effectColor = BORDER_COLOR;
        outline.effectDistance = new Vector2(1f, -1f);
        PocAssetBuilder.Place(box.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_x, _y), new Vector2(_width, _height));
        return box.transform;
    }

    private static TextMeshProUGUI Text(Transform _parent, string _name, string _text, int _size, float _x, float _y, float _width, float _height, Color _color, bool _bold = true)
    {
        var label = PocAssetBuilder.Label(_parent, _name, _text, _size, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_x, _y), new Vector2(_width, _height), _bold);
        label.color = _color;
        label.raycastTarget = false;
        return label;
    }

    // 세로 스크롤 목록. 줄은 돌려준 content 아래에 만들고, 격자 배치가 크기와 위치를 정한다.
    private static Transform ScrollList(Transform _parent, string _name, float _x, float _y, float _width, float _height, int _columns, float _cellHeight, float _spacing,
        System.Collections.Generic.List<ScrollRect> _scrolls, float _cellWidth = -1f)
    {
        var scroll = ScrollFrame(_parent, _name, _x, _y, _width, _height, _scrolls);
        var content = scroll.content.gameObject;
        var grid = content.AddComponent<GridLayoutGroup>();
        float cellWidth = _cellWidth > 0f ? _cellWidth : _width - SCROLLBAR_WIDTH - 4f;
        grid.cellSize = new Vector2(cellWidth, _cellHeight);
        grid.spacing = new Vector2(8f, _spacing);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = _columns;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return content.transform;
    }

    // 글 길이만큼 늘어나는 세로 스크롤 글 상자.
    private static TextMeshProUGUI TextScroll(Transform _parent, string _name, float _x, float _y, float _width, float _height, int _size, System.Collections.Generic.List<ScrollRect> _scrolls)
    {
        var scroll = ScrollFrame(_parent, _name, _x, _y, _width, _height, _scrolls);
        var label = PocAssetBuilder.Label(scroll.content, "Text", "", _size, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(_width - SCROLLBAR_WIDTH - 4f, _height), false);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        var group = scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandHeight = false;
        group.padding = new RectOffset(8, 8, 6, 6);
        var fitter = scroll.content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return label;
    }

    // ScrollRect + 가려지는 viewport + 오른쪽 세로 스크롤바. 넘칠 때만 스크롤바가 보인다.
    private static ScrollRect ScrollFrame(Transform _parent, string _name, float _x, float _y, float _width, float _height, System.Collections.Generic.List<ScrollRect> _scrolls)
    {
        var go = PocAssetBuilder.UiObject(_name, _parent);
        PocAssetBuilder.Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_x, _y), new Vector2(_width, _height));
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        var viewport = PocAssetBuilder.UiObject("Viewport", go.transform);
        var viewportRect = viewport.GetComponent<RectTransform>();
        PocAssetBuilder.Stretch(viewportRect);
        viewportRect.offsetMax = new Vector2(-SCROLLBAR_WIDTH - 4f, 0f);
        viewport.AddComponent<RectMask2D>();

        var content = PocAssetBuilder.UiObject("Content", viewport.transform);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        var bar = PocAssetBuilder.UiObject("Scrollbar", go.transform);
        var barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(1f, 0f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(1f, 1f);
        barRect.anchoredPosition = Vector2.zero;
        barRect.sizeDelta = new Vector2(SCROLLBAR_WIDTH, 0f);
        bar.AddComponent<Image>().color = INNER_COLOR;
        var area = PocAssetBuilder.UiObject("SlidingArea", bar.transform);
        PocAssetBuilder.Stretch(area.GetComponent<RectTransform>());
        var handle = PocAssetBuilder.UiObject("Handle", area.transform);
        var handleRect = handle.GetComponent<RectTransform>();
        PocAssetBuilder.Stretch(handleRect);
        var handleImage = handle.AddComponent<Image>();
        handleImage.color = MUTED_COLOR;
        var scrollbar = bar.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        // 스크롤바는 마우스 끌기 전용. 게임패드 탐색은 줄 사이로만 움직인다.
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

        var scroll = go.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        _scrolls.Add(scroll);
        return scroll;
    }

    // _priceWidth가 음수면 줄 너비에 맞춰 정한다. 0이면 가격 칸 없이 이름이 줄 전체를 쓴다.
    private static ShopRowView Row(Transform _parent, string _name, float _x, float _y, float _width, float _height, int _labelSize, int _priceSize, float _priceWidth = -1f, Color? _color = null)
    {
        var go = PocAssetBuilder.UiObject(_name, _parent);
        var image = go.AddComponent<Image>();
        image.color = _color ?? ROW_COLOR;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        PocAssetBuilder.Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_x, _y), new Vector2(_width, _height));
        float priceWidth = _priceWidth >= 0f ? _priceWidth : Mathf.Min(150f, _width * 0.32f);
        var label = PocAssetBuilder.Label(go.transform, "Label", "", _labelSize, priceWidth > 0f ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(_width - priceWidth - 16f, _height - 4f), false);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var price = PocAssetBuilder.Label(go.transform, "Price", "", _priceSize, TextAlignmentOptions.MidlineRight, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(priceWidth, _height - 4f));
        price.color = PRICE_COLOR;
        price.textWrappingMode = TextWrappingModes.Normal;
        return Bind(go, button, image, label, price, _color ?? ROW_COLOR);
    }

    // 카드형 상품(스탯·슬롯 업그레이드): 위에 이름·설명, 아래 오른쪽에 가격.
    private static ShopRowView Card(Transform _parent, string _name, float _x, float _y, float _width, float _height)
    {
        var go = PocAssetBuilder.UiObject(_name, _parent);
        var image = go.AddComponent<Image>();
        image.color = ROW_COLOR;
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        PocAssetBuilder.Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(_x, _y), new Vector2(_width, _height));
        var label = PocAssetBuilder.Label(go.transform, "Label", "", 19, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -8f), new Vector2(_width - 20f, _height - 44f), false);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var price = PocAssetBuilder.Label(go.transform, "Price", "", 22, TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 8f), new Vector2(_width - 20f, 34f));
        price.color = PRICE_COLOR;
        return Bind(go, button, image, label, price, ROW_COLOR);
    }

    private static ShopRowView Bind(GameObject _go, Button _button, Image _image, TMP_Text _label, TMP_Text _price, Color _normal)
    {
        var row = _go.AddComponent<ShopRowView>();
        var so = new SerializedObject(row);
        so.FindProperty("button").objectReferenceValue = _button;
        so.FindProperty("background").objectReferenceValue = _image;
        so.FindProperty("label").objectReferenceValue = _label;
        so.FindProperty("price").objectReferenceValue = _price;
        so.FindProperty("normalColor").colorValue = _normal;
        so.ApplyModifiedPropertiesWithoutUndo();
        return row;
    }

    private static void AssignArray<T>(SerializedProperty _property, T[] _values) where T : Object
    {
        _property.arraySize = _values.Length;
        for (int i = 0; i < _values.Length; i++)
        {
            _property.GetArrayElementAtIndex(i).objectReferenceValue = _values[i];
        }
    }
}
