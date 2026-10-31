using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

// 정비 상점 화면 검사.
// RunLayoutChecks: 프리팹만 본다(플레이 모드 없음). 기준 배치·구역 겹침·글자 크기·줄 수.
// RunChecks: 저장된 Game을 실제 PlayerLoop에서 돌려 상점 버튼을 눌러 본다(중복 파츠 구매·슬롯별 장착/분리·보관·바닥 무기·아킴보·스크롤),
//            그리고 상점 Canvas를 1920×1080 / 1280×720 / 1024×768로 그려 Logs/PocShopUi에 PNG를 남긴다.
// 메뉴 POC/Verify Shop UI Layout, POC/Verify Shop UI (Play). 제품 씬·테이블은 저장하지 않는다.
[InitializeOnLoad]
public static class PocShopUiVerification
{
    private const string RUNNING = "PocShopUiVerification.running";
    private const string TAG = "[PocShopUiVerification]";
    // 이번 실행에서 LogKitStore로 넘어간 게임 기록 줄.
    private static readonly List<string> telemetryLines = new List<string>();
    // 기준 해상도에서 이보다 작은 글자는 1280×720에서 10px 밑으로 내려가 읽기 어렵다.
    private const float MIN_FONT_SIZE = 16f;
    private static readonly Vector2Int[] RESOLUTIONS = { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(1024, 768) };
    private static readonly Stack<IEnumerator> STEPS = new Stack<IEnumerator>();
    private static int passes;
    private static int failures;
    private static int clicks;
    private static int lastFrame = -1;
    private static bool initialized;
    private static float deadline;
    private static GameManager game;
    private static ShopView view;

    static PocShopUiVerification()
    {
        EditorApplication.update += Tick;
        LogKitStore.LineAppended += _line => telemetryLines.Add(_line);
    }

    public static string ShotDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/PocShopUi"));

    [MenuItem("POC/Verify Shop UI Layout")]
    public static void RunLayoutChecks()
    {
        passes = 0;
        failures = 0;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PocShopAuthoring.SHOP_PREFAB_PATH);
        Check(asset != null, "Shop 프리팹이 있다");
        if (asset != null)
        {
            var root = PrefabUtility.LoadPrefabContents(PocShopAuthoring.SHOP_PREFAB_PATH);
            try
            {
                CheckLayout(root);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        Debug.Log($"{TAG} layout checks finished: passes={passes}, failures={failures}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    [MenuItem("POC/Verify Shop UI (Play)")]
    public static void RunChecks()
    {
        telemetryLines.Clear();
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
    }

    // ---------- 프리팹 배치 ----------

    private static void CheckLayout(GameObject _root)
    {
        var scaler = _root.GetComponent<CanvasScaler>();
        Check(scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && scaler.referenceResolution == new Vector2(1920f, 1080f) &&
            scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand, "Canvas는 1920×1080 기준 Expand 배율(작은 화면·4:3에서도 전체가 화면 안)");
        var layout = _root.transform.Find("Panel/Layout") as RectTransform;
        Check(layout != null && layout.sizeDelta == new Vector2(1920f, 1080f), "고정 1920×1080 배치 루트");
        if (layout == null)
        {
            return;
        }
        var bounds = new Rect(0f, -1080f, 1920f, 1080f);
        string[] sections = { "Weapons", "Stats", "UserInfoSection", "Supplies", "Upgrades" };
        var rects = new List<Rect>();
        foreach (string name in sections)
        {
            var section = layout.Find(name) as RectTransform;
            Check(section != null, $"구역 {name} 존재");
            if (section == null)
            {
                continue;
            }
            var rect = new Rect(section.anchoredPosition.x, section.anchoredPosition.y - section.sizeDelta.y, section.sizeDelta.x, section.sizeDelta.y);
            Check(bounds.Contains(rect.min) && bounds.Contains(rect.max), $"구역 {name}이 기준 화면 안");
            foreach (var other in rects)
            {
                Check(!other.Overlaps(rect), $"구역 {name}이 다른 구역과 겹치지 않음");
            }
            rects.Add(rect);
            CheckChildrenInside(section);
        }
        Check(rects.Count == 5 && rects[0].xMax < rects[2].xMin && Mathf.Approximately(rects[0].width, rects[1].width) && rects[1].yMax <= rects[0].yMin,
            "2열: 왼쪽 무기·파츠 위, 같은 너비의 스탯 아래 / 오른쪽 유저 정보·보급·슬롯 업그레이드");
        Check(rects.Count == 5 && rects[2].yMin >= rects[3].yMax && rects[3].yMin >= rects[4].yMax, "오른쪽 열 순서: 유저 정보 → 보급 → 슬롯 업그레이드");

        var small = new List<string>();
        foreach (var text in _root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.fontSize < MIN_FONT_SIZE || text.enableAutoSizing)
            {
                small.Add($"{text.transform.parent.name}/{text.name}={text.fontSize}");
            }
        }
        Check(small.Count == 0, $"모든 글자 {MIN_FONT_SIZE}pt 이상(1280×720에서 약 {MIN_FONT_SIZE * 2f / 3f:0.#}px) {string.Join(", ", small)}");

        var shopView = _root.GetComponent<ShopView>();
        var tables = TableManager.Instance;
        int slots = GameConfigInfo.WeaponSlotCount(tables.GameConfig);
        Check(shopView.WeaponRowCount == tables.Shop.weapons.Length && shopView.SlotCardCount == slots && shopView.PartSlotButtonCount == slots && shopView.WeaponBuyCount == slots,
            "무기 줄·휴대 슬롯 카드·슬롯별 장착/분리 버튼 수가 테이블과 맞음");
        var scrolls = (ScrollRect[])Get(shopView, "scrolls");
        Check(scrolls.Length == 3 && Array.TrueForAll(scrolls, _s => _s != null && _s.viewport != null && _s.content != null && _s.viewport.GetComponent<RectMask2D>() != null),
            "무기 목록·보관/바닥 목록·보급 목록은 잘리는 viewport가 있는 스크롤");
        Check(Get(shopView, "akimboButton") is ShopRowView akimbo && akimbo.transform.IsChildOf(layout.Find("UserInfoSection")), "아킴보 토글은 유저 정보 구역 안");
    }

    // 구역 바로 아래 요소(스크롤 내용 제외)가 구역 밖으로 나가지 않는다.
    private static void CheckChildrenInside(RectTransform _section)
    {
        var inside = new Rect(0f, -_section.sizeDelta.y, _section.sizeDelta.x, _section.sizeDelta.y);
        var outside = new List<string>();
        foreach (RectTransform child in _section)
        {
            if (child.anchorMin != new Vector2(0f, 1f) || child.anchorMax != new Vector2(0f, 1f))
            {
                continue;
            }
            var rect = new Rect(child.anchoredPosition.x, child.anchoredPosition.y - child.sizeDelta.y, child.sizeDelta.x, child.sizeDelta.y);
            if (rect.xMin < -0.5f || rect.xMax > inside.xMax + 0.5f || rect.yMin < inside.yMin - 0.5f || rect.yMax > 0.5f)
            {
                outside.Add(child.name);
            }
        }
        Check(outside.Count == 0, $"{_section.name} 안 요소가 구역 경계 안 {string.Join(", ", outside)}");
    }

    // ---------- 플레이 모드 ----------

    private static void Tick()
    {
        if (!SessionState.GetBool(RUNNING, false) || !EditorApplication.isPlaying || lastFrame == Time.frameCount)
        {
            return;
        }
        lastFrame = Time.frameCount;
        try
        {
            if (!initialized)
            {
                initialized = true;
                passes = 0;
                failures = 0;
                clicks = 0;
                deadline = Time.realtimeSinceStartup + 120f;
                Application.logMessageReceived += OnLog;
                STEPS.Push(Scenarios());
            }
            if (Time.realtimeSinceStartup > deadline)
            {
                throw new TimeoutException("Shop UI checks exceeded 120 seconds.");
            }
            while (STEPS.Count > 0)
            {
                IEnumerator step = STEPS.Peek();
                if (!step.MoveNext())
                {
                    STEPS.Pop();
                    continue;
                }
                if (step.Current is IEnumerator nested)
                {
                    STEPS.Push(nested);
                    continue;
                }
                return;
            }
            Finish();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogException(exception);
            Finish();
        }
    }

    private static IEnumerator Scenarios()
    {
        var tables = TableManager.Instance;
        game = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        view = UnityEngine.Object.FindFirstObjectByType<ShopView>(FindObjectsInactive.Include);
        Check(game != null && view != null, "Game 씬에 GameManager·ShopView");
        yield return Wait(() => game.WaveNumber == 1, 3f, "1웨이브 시작");
        game.Wallet.Add(5000);
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 3f, "웨이브 종료 후 정비 상점 열림");
        yield return Frames(2);
        CheckRuntimeLayout("열림");
        yield return Capture("01_open");

        // 같은 권총 두 자루: 시작 M1873 + 상점 M1873.
        var loadout = game.Loadout;
        Check(loadout.Get(0)?.WeaponId == "m1873" && loadout.Get(1) == null, "시작 무기 M1873 한 자루");
        yield return Click(Rows("weaponRows")[WeaponIndex("m1873")], "무기 목록에서 M1873 선택");
        yield return Click(Rows("weaponBuyButtons")[1], "M1873 → 2번 슬롯 구매");
        Check(loadout.Get(1)?.WeaponId == "m1873" && loadout.Get(0) != loadout.Get(1), "서로 다른 M1873 개체 두 자루");

        // 같은 파츠 중복 구매: 첫 두 개는 빈 칸에 자동 장착, 세 번째는 보관.
        var heavy = PartInfo.Find(tables.Part, "m1873_heavy_barrel");
        int chip = PartInfo.ForWeapon(tables.Part, "m1873").FindIndex(_p => _p.id == heavy.id);
        yield return Click(Rows("partChips")[chip], "파츠 칩에서 무거운 총열 선택");
        var buy = (ShopRowView)Get(view, "partBuyButton");
        yield return Click(buy, "무거운 총열 구매 1");
        yield return Click(buy, "무거운 총열 추가 구매 2");
        yield return Click(buy, "무거운 총열 추가 구매 3");
        var parts = game.Parts;
        Check(parts.OwnedCount(heavy.id) == 3 && parts.StoredCount(heavy.id) == 1, $"같은 파츠 3개 보유 · 보관 1 (보유 {parts.OwnedCount(heavy.id)}, 보관 {parts.StoredCount(heavy.id)})");
        Check(loadout.Get(0).HasPart(heavy.id) && loadout.Get(1).HasPart(heavy.id) && loadout.Get(0).PartInstanceIn(heavy.slot) != loadout.Get(1).PartInstanceIn(heavy.slot),
            "두 무기 개체에 서로 다른 파츠 개체가 장착");
        Check(Text("partDetailText").Contains("보유 3개") && Text("partDetailText").Contains("보관") && Text("partDetailText").Contains("2번 장착"),
            "파츠 상세에 개체별 위치(보관·1번·2번) 표시");
        Check(Text("inventoryText").Contains("보관 파츠") && Text("inventoryText").Contains(heavy.name), "유저 정보에 보관 파츠 표시");

        // 슬롯별 분리·장착.
        var detach = Rows("partDetachButtons");
        var attach = Rows("partAttachButtons");
        var secondCopy = loadout.Get(1).PartInstanceIn(heavy.slot);
        yield return Click(detach[1], "2번 무기에서 분리");
        Check(!loadout.Get(1).HasPart(heavy.id) && loadout.Get(0).HasPart(heavy.id) && secondCopy.IsStored && parts.StoredCount(heavy.id) == 2,
            "2번만 분리되고 그 개체가 보관함으로");
        Check(!detach[1].Button.interactable && attach[1].Button.interactable && !attach[0].Button.interactable, "분리 후 2번 장착만 가능, 1번은 이미 장착");
        yield return Click(attach[1], "2번 무기에 다시 장착");
        Check(loadout.Get(1).HasPart(heavy.id) && parts.StoredCount(heavy.id) == 1, "보관 개체를 2번에 장착");
        Check(SlotCardLabel(0).Contains("#") && SlotCardLabel(1).Contains(heavy.name), "휴대 슬롯 카드에 장착 파츠 개체 표시");

        // 아킴보(유저 정보 구역의 토글).
        var akimbo = (ShopRowView)Get(view, "akimboButton");
        Check(game.CanAkimbo && akimbo.Button.interactable, "같은 권총 두 자루면 아킴보 토글 활성");
        yield return Click(akimbo, "아킴보 ON");
        Check(game.IsAkimbo && SlotCardStatus(loadout.ActiveIndex).Contains("오른손") && SlotCardStatus(loadout.OffHandIndex).Contains("왼손"),
            "아킴보 ON · 슬롯 카드에 양손 표시");
        yield return Capture("02_akimbo");
        yield return Click(akimbo, "아킴보 OFF");
        Check(!game.IsAkimbo, "아킴보 OFF");

        // 바닥 무기: 2번 M1873(파츠 장착)을 Winchester로 교체하면 그 무기는 파츠를 단 채 바닥에 남는다.
        var dropped = loadout.Get(1);
        yield return Click(Rows("weaponRows")[WeaponIndex("win1886")], "Winchester 1886 선택");
        var weaponImage = (Image)Get(view, "weaponImage");
        Check(weaponImage.gameObject.activeSelf && weaponImage.sprite != null, "선택한 무기의 총기 이미지가 상세에 보인다");
        yield return Click(Rows("weaponBuyButtons")[1], "2번 슬롯 교체 구매");
        Check(loadout.Get(1)?.WeaponId == "win1886" && !loadout.Contains(dropped) && dropped.HasPart(heavy.id), "교체된 M1873은 파츠를 단 채 바닥");
        Check(Text("inventoryText").Contains("바닥 무기") && Text("inventoryText").Contains(heavy.name), "유저 정보에 바닥 무기와 그 파츠 표시");
        yield return Click(SlotCard(0), "1번 슬롯 카드로 M1873 파츠 보기");
        yield return Click(Rows("partChips")[chip], "무거운 총열 다시 선택");
        Check(Text("partDetailText").Contains("바닥 무기"), "파츠 상세에 바닥 무기 개체 표시");
        Check(!detach[1].Button.interactable && detach[0].Button.interactable, "바닥 무기 개체는 분리 불가, 휴대 1번만 분리 가능");
        yield return Capture("03_dropped");

        // 스크롤: 목록 마지막 무기를 키보드/게임패드로 고르면 보이는 곳까지 내려간다.
        var weaponRows = Rows("weaponRows");
        Check(weaponRows.Count(_r => _r.gameObject.activeSelf) == VisibleWeapons().Length, "해금된 무기 줄만 보인다");
        var last = weaponRows.Last(_r => _r.gameObject.activeSelf);
        EventSystem.current.SetSelectedGameObject(last.gameObject);
        yield return Frames(3);
        var viewport = ((ScrollRect[])Get(view, "scrolls"))[0].viewport;
        Check(Inside(viewport, (RectTransform)last.transform), "선택한 마지막 무기 줄이 스크롤되어 보임");
        CheckRuntimeLayout("조작 후");

        // 상점 로그: 다음 웨이브 버튼(skip)으로 나간 뒤, 다음 정비 시간은 시간 만료(timeout)로 나간다.
        var nextWave = (Button)Get(view, "nextWaveButton");
        Check(nextWave != null && nextWave.IsInteractable(), "다음 웨이브 버튼 사용 가능");
        nextWave.onClick.Invoke();
        yield return Wait(() => game.Phase == GameManager.GamePhase.Wave, 3f, "다음 웨이브 버튼으로 상점 퇴장");
        Set(game, "waveTimeLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Shop, 3f, "2웨이브 종료 후 상점 재진입");
        yield return Frames(2);
        Set(game, "intermissionLeft", 0.001f);
        yield return Wait(() => game.Phase == GameManager.GamePhase.Wave, 3f, "정비 시간 만료로 상점 퇴장");
        CheckShopTelemetry();
    }

    // 지금 세션 파일에서 상점 이벤트를 읽어 실제 버튼 경로가 남긴 기록을 확인한다.
    private static void CheckShopTelemetry()
    {
        var session = game.Telemetry?.Session;
        Check(session != null && session.IsRecording, "텔레메트리 세션 기록 중");
        if (session == null || !session.IsRecording)
        {
            return;
        }
        session.Flush();
        // 기록은 LogKitStore를 거쳐 나온 줄을 그대로 모은다(로컬 파일은 LogKit이 전송 후 정리한다).
        var rows = telemetryLines
            .Where(_line => !string.IsNullOrWhiteSpace(_line))
            .Select(JObject.Parse)
            .Where(_row => (string)_row["sessionId"] == session.SessionId)
            .ToList();
        JObject[] Of(string _type) => rows.Where(_row => (string)_row["eventType"] == _type).ToArray();
        var opened = Of("shop_opened");
        var closed = Of("shop_closed");
        Check(opened.Length == 2 && closed.Length == 2 && opened.Select(_r => (string)_r["payload"]["visitId"]).SequenceEqual(closed.Select(_r => (string)_r["payload"]["visitId"])),
            $"상점 방문 2회가 열림·닫힘 쌍으로 기록 (열림 {opened.Length}, 닫힘 {closed.Length})");
        var inputs = Of("shop_ui_input");
        var skip = closed.FirstOrDefault(_r => (string)_r["payload"]["reason"] == "skip");
        var skipInput = skip != null ? inputs.FirstOrDefault(_r => (string)_r["payload"]["interactionId"] == (string)skip["payload"]["interactionId"]) : null;
        Check(skipInput != null && (string)skipInput["payload"]["action"] == "next_wave", "skip 퇴장이 다음 웨이브 버튼 입력과 연결");
        var timeout = closed.FirstOrDefault(_r => (string)_r["payload"]["reason"] == "timeout");
        Check(timeout != null && timeout["payload"]["interactionId"].Type == JTokenType.Null && (string)timeout["payload"]["nextPhase"] == "wave",
            "시간 만료 퇴장은 timeout이며 입력과 연결되지 않음");
        Check(inputs.Length == clicks + 1, $"누른 상점 버튼마다 입력 기록 1개 (클릭 {clicks + 1}, 기록 {inputs.Length})");
        var linked = new HashSet<string>(rows.Where(_r => (string)_r["eventType"] != "shop_ui_input" && _r["payload"]?["interactionId"] != null &&
            _r["payload"]["interactionId"].Type == JTokenType.String).Select(_r => (string)_r["payload"]["interactionId"]));
        Check(inputs.All(_r => linked.Contains((string)_r["payload"]["interactionId"])), "모든 입력이 결과 이벤트 또는 무시 사유와 연결");
        var attempts = Of("shop_purchase_attempt");
        Check(attempts.Length >= 5 && attempts.All(_r => _r["payload"]["interactionId"].Type == JTokenType.String && _r["payload"]["offer"].Type == JTokenType.Object),
            $"UI 구매 시도는 입력·진열 기록과 연결 ({attempts.Length})");
        Check(Of("shop_purchase_result").Length == attempts.Length && Of("shop_purchase_result").All(_r => (bool)_r["payload"]["success"]),
            "UI 구매 결과가 시도와 같은 수이며 성공으로만 기록");
        var equipment = Of("shop_equipment_result");
        Check(equipment.Select(_r => (string)_r["payload"]["request"]["action"]).SequenceEqual(new[] { "part_detach", "part_attach", "akimbo_set", "akimbo_set" }) &&
            equipment.All(_r => (bool)_r["payload"]["success"] && (bool)_r["payload"]["changed"]), "분리·장착·아킴보 ON/OFF 결과가 변화와 함께 기록");
        var sources = new HashSet<string>(Of("shop_selection_changed").Select(_r => (string)_r["payload"]["source"]));
        Check(sources.Contains("weapon_catalog") && sources.Contains("part_catalog") && sources.Contains("slot_card"), "무기 목록·파츠 칩·슬롯 카드 선택 기록");
        Check(Of("shop_view_shown").Length == 2 && Of("shop_view_hidden").Length == 2 && Of("shop_offers_presented").Length > 0, "화면 표시·숨김이 방문마다 한 번");
        var focus = Of("shop_focus_changed");
        Check(focus.Any(_r => (string)_r["payload"]["cause"] == "programmatic") && focus.Any(_r => (string)_r["payload"]["to"]?["placement"]?.ToString() == "weapon_catalog"),
            "열 때 자동 포커스와 목록 탐색 포커스 기록");
        Check(rows.Where(_r => ((string)_r["eventType"]).StartsWith("shop_", StringComparison.Ordinal)).All(_r => (string)_r["phase"] == "shop"),
            "상점 이벤트는 모두 shop 단계 문맥");
    }

    // 실제 Canvas 배율 기준. 기준 배치가 화면 안에 들어오고, 핵심 글이 잘리지 않는다.
    private static void CheckRuntimeLayout(string _label)
    {
        Canvas.ForceUpdateCanvases();
        var canvas = view.GetComponent<Canvas>();
        var layout = (RectTransform)view.transform.Find("Panel/Layout");
        var canvasRect = (RectTransform)canvas.transform;
        Check(layout.rect.width <= canvasRect.rect.width + 0.5f && layout.rect.height <= canvasRect.rect.height + 0.5f,
            $"{_label}: 배치 {layout.rect.size}가 현재 화면 {Screen.width}×{Screen.height}(Canvas {canvasRect.rect.size}) 안");
        var truncated = new List<string>();
        foreach (string field in new[] { "weaponDetailText", "partDetailText", "userInfoText", "akimboNoteText" })
        {
            var text = (TMP_Text)Get(view, field);
            text.ForceMeshUpdate();
            if (text.isTextTruncated)
            {
                truncated.Add(field);
            }
        }
        foreach (string field in new[] { "weaponRows", "partChips", "slotCards", "statRows", "supplyRows", "upgradeRows", "weaponBuyButtons", "partAttachButtons", "partDetachButtons" })
        {
            foreach (var row in Rows(field))
            {
                if (!row.gameObject.activeInHierarchy)
                {
                    continue;
                }
                foreach (var text in row.GetComponentsInChildren<TMP_Text>())
                {
                    text.ForceMeshUpdate();
                    if (text.isTextTruncated || (text.overflowMode == TextOverflowModes.Overflow && text.preferredWidth > text.rectTransform.rect.width + 1f && text.textWrappingMode == TextWrappingModes.NoWrap))
                    {
                        truncated.Add($"{row.name}/{text.name}");
                    }
                }
            }
        }
        Check(truncated.Count == 0, $"{_label}: 잘린 글 없음 {string.Join(", ", truncated)}");

        // 진열은 무작위라 지금 보이는 두 상품만으로는 부족하다. 풀의 모든 슬롯 업그레이드 문구를 카드에 넣어 본다.
        var presenter = UnityEngine.Object.FindFirstObjectByType<ShopPresenter>();
        var label = (TMP_Text)Get(Rows("upgradeRows")[0], "label");
        var format = presenter.GetType().GetMethod("UpgradeLabel", BindingFlags.Static | BindingFlags.NonPublic);
        var tooLong = new List<string>();
        foreach (var upgrade in TableManager.Instance.Shop.slotUpgrades.pool)
        {
            var perk = PerkInfo.Find(TableManager.Instance.Perk, upgrade.perkId);
            label.text = (string)format.Invoke(null, new object[] { perk.name, perk.description });
            label.ForceMeshUpdate();
            if (label.isTextTruncated)
            {
                tooLong.Add(perk.id);
            }
        }
        presenter.GetType().GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, null);
        Check(tooLong.Count == 0, $"{_label}: 모든 슬롯 업그레이드 설명이 카드에 들어감 {string.Join(", ", tooLong)}");
    }

    // 상점 Canvas를 임시 카메라로 해상도별 텍스처에 그린다. ScreenSpaceOverlay는 텍스처로 못 그리므로 잠시 ScreenSpaceCamera로 바꾼다.
    private static IEnumerator Capture(string _name)
    {
        Directory.CreateDirectory(ShotDirectory);
        ScreenCapture.CaptureScreenshot(Path.Combine(ShotDirectory, $"{_name}_gameview_{Screen.width}x{Screen.height}.png"));
        yield return Frames(2);
        var canvas = view.GetComponent<Canvas>();
        var go = new GameObject("PocShopUiCamera");
        go.transform.position = new Vector3(0f, -10000f, 0f);
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << canvas.gameObject.layer;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        var mode = canvas.renderMode;
        foreach (var size in RESOLUTIONS)
        {
            var target = new RenderTexture(size.x, size.y, 24);
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            // CanvasScaler는 Update에서 새 화면 크기 배율을 잡는다.
            yield return Frames(2);
            Canvas.ForceUpdateCanvases();
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                camera.Render();
            }
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            string path = Path.Combine(ShotDirectory, $"{_name}_{size.x}x{size.y}.png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            Check(!Blank(image), $"{_name} {size.x}×{size.y} 캡처에 화면이 그려짐 → {path}");
            UnityEngine.Object.Destroy(image);
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
        canvas.renderMode = mode;
        canvas.worldCamera = null;
        UnityEngine.Object.Destroy(go);
        yield return Frames(2);
    }

    private static bool Blank(Texture2D _image)
    {
        var pixels = _image.GetPixels32();
        var first = pixels[0];
        for (int i = 0; i < pixels.Length; i += 97)
        {
            if (Mathf.Abs(pixels[i].r - first.r) + Mathf.Abs(pixels[i].g - first.g) + Mathf.Abs(pixels[i].b - first.b) > 30)
            {
                return false;
            }
        }
        return true;
    }

    private static bool Inside(RectTransform _viewport, RectTransform _item)
    {
        var corners = new Vector3[4];
        _item.GetWorldCorners(corners);
        var top = _viewport.InverseTransformPoint(corners[1]);
        var bottom = _viewport.InverseTransformPoint(corners[0]);
        return top.y <= _viewport.rect.yMax + 1f && bottom.y >= _viewport.rect.yMin - 1f;
    }

    // 실제 버튼을 누른다(비활성·숨김 버튼이면 실패로 기록하고 누르지 않는다).
    private static IEnumerator Click(ShopRowView _row, string _label)
    {
        bool usable = _row != null && _row.gameObject.activeInHierarchy && _row.Button.interactable;
        Check(usable, $"버튼 사용 가능: {_label}");
        if (usable)
        {
            clicks++;
            _row.Button.onClick.Invoke();
        }
        yield return Frames(2);
        Debug.Log($"{TAG} 메시지: {Text("messageText")}");
    }

    // 무기 목록은 지금 해금된 무기만 판매 순서대로 채운다. 줄 번호는 이 목록 안의 순서다.
    private static ShopData.WeaponOffer[] VisibleWeapons()
    {
        var shop = TableManager.Instance.Shop;
        var weapons = TableManager.Instance.Weapon;
        return shop.weapons.Where(_o => ShopInfo.IsWeaponUnlocked(shop, WeaponInfo.Find(weapons, _o.weaponId), game.ShopWave)).ToArray();
    }

    private static int WeaponIndex(string _weaponId) => Array.FindIndex(VisibleWeapons(), _o => _o.weaponId == _weaponId);

    private static ShopRowView[] Rows(string _field) => (ShopRowView[])Get(view, _field);

    private static ShopRowView SlotCard(int _slot) => Rows("slotCards")[_slot];

    private static string SlotCardLabel(int _slot) => ((TMP_Text)Get(SlotCard(_slot), "label")).text;

    private static string SlotCardStatus(int _slot) => _slot >= 0 ? ((TMP_Text)Get(SlotCard(_slot), "price")).text : "";

    private static string Text(string _field) => ((TMP_Text)Get(view, _field)).text;

    private static object Get(object _owner, string _name) => _owner.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_owner);

    private static void Set(object _owner, string _name, object _value)
    {
        _owner.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_owner, _value);
    }

    private static IEnumerator Frames(int _count)
    {
        for (int i = 0; i < _count; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator Wait(Func<bool> _condition, float _seconds, string _label)
    {
        float end = Time.realtimeSinceStartup + _seconds;
        while (!_condition() && Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
        Check(_condition(), _label);
    }

    private static void OnLog(string _message, string _stack, LogType _type)
    {
        if ((_type == LogType.Error && !_message.StartsWith(TAG, StringComparison.Ordinal)) || _type == LogType.Exception)
        {
            failures++;
        }
    }

    private static void Check(bool _condition, string _label)
    {
        if (_condition)
        {
            passes++;
            Debug.Log($"{TAG} PASS: {_label}");
        }
        else
        {
            failures++;
            Debug.LogError($"{TAG} FAIL: {_label}");
        }
    }

    private static void Finish()
    {
        SessionState.SetBool(RUNNING, false);
        initialized = false;
        Application.logMessageReceived -= OnLog;
        STEPS.Clear();
        Debug.Log($"{TAG} finished play-mode frames={Time.frameCount}, passes={passes}, failures={failures}, shots={ShotDirectory}");
        if (Application.isBatchMode)
        {
            PocEditorExit.Exit(failures == 0 ? 0 : 1);
            return;
        }
        EditorApplication.ExitPlaymode();
    }
}
