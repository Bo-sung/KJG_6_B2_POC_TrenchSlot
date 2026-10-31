using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

// 상점 로그(LogShopCollector) 결정적 검사. Unity API 없이 메모리 저장소로 이벤트 순서·연결·실패 처리를 본다.
// 메뉴 POC/Verify Shop Telemetry. 같은 파일을 Unity 밖(dotnet)에서도 컴파일해 돌릴 수 있도록 Unity 부분은 UNITY_EDITOR로 감싼다.
public static class PocShopLogVerification
{
    private static int checks;
    private static LogData settings;

    private sealed class Rig : IDisposable
    {
        public readonly MemoryLogStore store = new MemoryLogStore(10000);
        public readonly LogSession session;
        public readonly LogGameLifecycle life;
        public readonly LogShopCollector shop;
        public int money = 1000;

        public Rig(LogData _settings = null, Func<string, ILogStore> _factory = null)
        {
            session = new LogSession(_settings ?? settings, _factory ?? (_id => store), _initialFixedDeltaTime: 0.02);
            life = new LogGameLifecycle(session);
            shop = new LogShopCollector(life);
            life.Start("synthetic-build", null, null, "synthetic-map");
            life.BeginWave(new LogWavePlan { waveNumber = 1, durationSeconds = 10 }, null);
            life.CompleteWave(null, null, 0, 0);
            life.SetPhase("shop");
        }

        public LogShopSnapshot State(string _hand = "right", params string[] _parts)
        {
            return new LogShopSnapshot
            {
                player = new LogPlayerSnapshot { money = money, health = 50, activeHand = _hand,
                    weapons = new[] { new LogWeaponSnapshot { instanceId = "w1", weaponTypeId = "m1873", slot = 0 } } },
                maxHealth = 100,
                parts = _parts.Select(_id => new LogShopPart { instanceId = _id, partId = "barrel", attachedWeaponInstanceId = "w1" }).ToArray()
            };
        }

        public JObject[] Rows()
        {
            session.Flush();
            return store.Lines.Select(JObject.Parse).ToArray();
        }

        public JObject[] Events(string _type) => Rows().Where(_row => (string)_row["eventType"] == _type).ToArray();

        public string[] Types() => Rows().Select(_row => (string)_row["eventType"]).ToArray();

        public void Dispose()
        {
            if (life.IsActive)
            {
                life.End("return_to_title", null, null);
            }
        }
    }

    private sealed class ThrowingStore : ILogStore
    {
        public void Append(string _line) => throw new IOException("synthetic append failure");
        public void Flush() => throw new IOException("synthetic flush failure");
        public void Dispose() { }
    }

#if UNITY_EDITOR
    [MenuItem("POC/Verify Shop Telemetry")]
    public static void RunFromMenu()
    {
        int code = 0;
        try
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Debug.Log("[PocShopLogVerification] PASS: " + Run(File.ReadAllText(Path.Combine(Application.dataPath, "Resources/LogData.json")), root) + " checks");
        }
        catch (Exception exception)
        {
            code = 1;
            Debug.LogError("[PocShopLogVerification] FAIL: " + exception);
        }
        PocEditorExit.Exit(code);
    }
#endif

    // _projectRoot: Assets 폴더가 있는 Unity 프로젝트 경로(정적 연결 검사용). 통과한 검사 수를 돌려준다.
    public static int Run(string _logDataJson, string _projectRoot)
    {
        checks = 0;
        settings = JsonConvert.DeserializeObject<LogData>(_logDataJson);
        Check(settings.enabled, "LogData.json enables telemetry");
        VisitSkip();
        VisitTimeoutAndReopen();
        VisibilityAndSelection();
        InputsAndPurchases();
        Equipment();
        Focus();
        PointerAndScroll();
        SessionEndInsideShop();
        DisabledAndFailures();
        Compatibility();
        if (_projectRoot != null)
        {
            StaticWiring(_projectRoot);
        }
        return checks;
    }

    private static void VisitSkip()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            var opened = rig.Events("shop_opened").Single();
            string visit = (string)opened["payload"]["visitId"];
            Check(visit != null && visit.Contains(":shopvisit:") && (int)opened["payload"]["nextWaveNumber"] == 2 &&
                (string)opened["phase"] == "shop" && (string)opened["payload"]["utc"] != null && (int)opened["payload"]["state"]["player"]["money"] == 1000,
                "shop_opened carries visit ID, next wave, UTC timestamp and before state");
            rig.shop.Present(true, rig.money, "m1873", "barrel", new LogShopOffer[0]);
            var shown = rig.Events("shop_view_shown").Single();
            Check((string)shown["payload"]["visitId"] == visit && (string)shown["payload"]["selection"]["weaponId"] == "m1873",
                "shop_view_shown links visit and initial selection");
            Check((string)rig.Events("shop_offers_presented").Single()["payload"]["visitId"] == visit, "Offer presentation links visit");
            for (int i = 0; i < 5; i++)
            {
                rig.session.AdvanceFixedUpdate(0.02);
            }
            string input = rig.shop.BeginInput("next_wave", new LogShopControl { placement = "next_wave", interactable = true }, null, rig.money, "m1873", "barrel");
            rig.shop.Closed("skip", 12.5, rig.State(), null, "wave");
            rig.shop.EndInput(input, null);
            var closed = rig.Events("shop_closed").Single();
            Check((string)closed["payload"]["reason"] == "skip" && (string)closed["payload"]["interactionId"] == input &&
                (string)closed["payload"]["nextPhase"] == "wave" && (double)closed["payload"]["intermissionLeft"] == 12.5 &&
                Math.Abs((double)closed["payload"]["dwellSeconds"] - 0.1) < 1e-9, "Skip close links UI input, remaining time and dwell");
            string[] types = rig.Types();
            Check(Array.IndexOf(types, "shop_view_hidden") < Array.IndexOf(types, "shop_closed") &&
                (string)rig.Events("shop_view_hidden").Single()["payload"]["reason"] == "shop_closed", "View hides before the visit closes");
            Check(rig.Events("shop_ui_input_ignored").Length == 0, "Linked input is not reported as ignored");
            rig.shop.Present(false, null, null, null, null, null, "phase_changed");
            rig.shop.Closed("phase_changed", 0, rig.State());
            Check(rig.Events("shop_view_hidden").Length == 1 && rig.Events("shop_closed").Length == 1, "Late hide/close after visit end is not duplicated");
        }
    }

    private static void VisitTimeoutAndReopen()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.Closed("timeout", 0, rig.State(), null, "wave");
            var closed = rig.Events("shop_closed").Single();
            Check((string)closed["payload"]["reason"] == "timeout" && closed["payload"]["interactionId"].Type == JTokenType.Null,
                "Timeout close is not attributed to a UI input");
            rig.shop.Opened(3, 30, 30, rig.State());
            rig.shop.Opened(4, 30, 30, rig.State());
            var all = rig.Events("shop_closed");
            var visits = rig.Events("shop_opened").Select(_row => (string)_row["payload"]["visitId"]).ToArray();
            Check(visits.Distinct().Count() == 3 && all.Length == 2 && (string)all[1]["payload"]["reason"] == "reopened" &&
                (string)all[1]["payload"]["visitId"] == visits[1], "Unique visit IDs; an unclosed visit closes as reopened");
        }
    }

    private static void VisibilityAndSelection()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            var offers = new[] { new LogShopOffer { product = new LogShopProduct { itemType = "Stat", itemId = "hp", productId = "Stat:hp", price = 10, valid = true },
                placement = "stat", row = 0, state = "available", purchasable = true } };
            for (int i = 0; i < 100; i++)
            {
                rig.shop.Present(true, rig.money, "m1873", "a", offers);
            }
            Check(rig.Events("shop_view_shown").Length == 1 && rig.Events("shop_offers_presented").Length == 1 &&
                rig.Events("shop_selection_changed").Length == 0, "Identical frames add no shown/offer/selection duplicates");
            rig.shop.Present(true, rig.money, "m1873", "b", offers);
            var auto = rig.Events("shop_selection_changed").Single();
            Check((string)auto["payload"]["source"] == "refresh" && (bool)auto["payload"]["changed"] &&
                (string)auto["payload"]["before"]["partId"] == "a" && (string)auto["payload"]["after"]["partId"] == "b",
                "Non-input selection change is logged once with before/after");
            string input = rig.shop.BeginInput("part_select", new LogShopControl { placement = "part_catalog", row = 1 }, null, rig.money, "m1873", "b");
            rig.shop.ObserveSelection("m1873", "b", "part_catalog", true);
            rig.shop.EndInput(input, null);
            var again = rig.Events("shop_selection_changed").Last();
            Check((string)again["payload"]["interactionId"] == input && !(bool)again["payload"]["changed"],
                "Explicit reselect is logged as unchanged and linked");
            rig.shop.ObserveSelection("m1873", "b", "part_catalog", true);
            Check(rig.Events("shop_selection_changed").Length == 2, "Explicit same selection without an input is not repeated");
            rig.shop.Present(false, null, null, null, null, null, "not_visible");
            rig.shop.Present(true, rig.money, "m1873", "b", offers);
            var hidden = rig.Events("shop_view_hidden").Single();
            Check((string)hidden["payload"]["reason"] == "not_visible" && rig.Events("shop_view_shown").Length == 2 &&
                rig.Events("shop_offers_presented").Length == 3, "Hide/show transitions are recorded and restart the offer set");
            var sets = rig.Events("shop_offers_presented").Select(_row => (string)_row["payload"]["offerSetId"]).ToArray();
            Check(sets[0] == sets[1] && sets[2] != sets[0] && (int)rig.Events("shop_offers_presented")[2]["payload"]["revision"] == 0,
                "Selection change revises the offer set; re-shown offers get a new one");
            rig.shop.Present(true, rig.money, "m1873", "b", offers, new LogShopInventory { activeSlot = 0, parts = new LogShopPart[0] });
            var withInventory = rig.Events("shop_offers_presented").Last();
            Check((int)withInventory["payload"]["revision"] == 1 && withInventory["payload"]["presentation"]["inventory"]["activeSlot"] != null,
                "Inventory view changes create a new revision of the same offer set");
        }
    }

    private static void InputsAndPurchases()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            string ignored = rig.shop.BeginInput("slot_card_select", new LogShopControl { placement = "slot_card", row = 1 }, 1, rig.money, "m1873", "");
            rig.shop.EndInput(ignored, "empty_slot");
            var row = rig.Events("shop_ui_input_ignored").Single();
            var inputRow = rig.Events("shop_ui_input").Single();
            Check((string)row["payload"]["interactionId"] == ignored && (string)row["payload"]["reason"] == "empty_slot" &&
                (int)inputRow["payload"]["targetSlot"] == 1 && (int)inputRow["payload"]["balance"] == 1000 &&
                inputRow["payload"]["visitElapsedSeconds"] != null, "No-op input records control, balance, timing and reason");
            rig.shop.EndInput(ignored, "empty_slot");
            Check(rig.Events("shop_ui_input_ignored").Length == 1, "Ending an input twice does not duplicate");

            var product = new LogShopProduct { itemType = "Stat", itemId = "hp", productId = "Stat:hp", price = 5000, valid = true };
            string buy = rig.shop.BeginInput("stat_buy", new LogShopControl { placement = "stat", row = 0 }, null, rig.money, "m1873", "");
            var failed = rig.shop.Begin(product, rig.State(), null);
            rig.shop.Complete(failed, true, "소지금이 부족합니다.", LogShopCollector.ClassifyFailure("소지금이 부족합니다.", product), rig.State());
            rig.shop.EndInput(buy, null);
            var attempt = rig.Events("shop_purchase_attempt").Single();
            var result = rig.Events("shop_purchase_result").Single();
            Check((string)attempt["payload"]["interactionId"] == buy && attempt["payload"]["visitId"] != null &&
                !(bool)result["payload"]["success"] && (string)result["payload"]["failureCode"] == "insufficient_funds" &&
                (long)result["payload"]["actualSpent"] == 0, "Rejected purchase is linked to its input and never reported as success");
            Check(rig.Events("shop_ui_input_ignored").Length == 1, "Purchase input is linked, not ignored");

            rig.money = 990;
            var ok = rig.shop.Begin(product, rig.State(), null);
            rig.money = 980;
            rig.shop.Complete(ok, true, "", null, rig.State());
            rig.shop.Complete(ok, true, "", null, rig.State());
            var results = rig.Events("shop_purchase_result");
            Check(results.Length == 2 && (bool)results[1]["payload"]["success"] && (long)results[1]["payload"]["actualSpent"] == 10 &&
                rig.Events("shop_purchase_attempt")[1]["payload"]["interactionId"].Type == JTokenType.Null,
                "Direct API purchase has no input link; a second completion is ignored");

            string crash = rig.shop.BeginInput("ammo_buy", null, null, rig.money, null, null);
            var thrown = rig.shop.Begin(product, rig.State(), null);
            rig.shop.Complete(thrown, false, null, null, rig.State());
            rig.shop.EndInput(crash, "exception");
            Check((string)rig.Events("shop_purchase_result").Last()["payload"]["failureCode"] == "exception", "Exception during purchase is a failure, not success");
        }
    }

    private static void Equipment()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            var request = new LogShopEquipmentRequest { action = "part_attach", partId = "barrel", partInstanceId = "p1", targetSlot = 0, targetWeaponInstanceId = "w1" };
            string input = rig.shop.BeginInput("part_attach", new LogShopControl { placement = "part_attach", row = 0 }, 0, rig.money, "m1873", "barrel");
            rig.shop.RecordEquipment(request, rig.State(), true, "", rig.State("right", "p1"));
            rig.shop.EndInput(input, null);
            var ok = rig.Events("shop_equipment_result").Single();
            Check((bool)ok["payload"]["success"] && (bool)ok["payload"]["changed"] && (string)ok["payload"]["interactionId"] == input &&
                ((string)ok["payload"]["actionId"]).Contains(":shopaction:") && (string)ok["payload"]["request"]["partInstanceId"] == "p1" &&
                ((JArray)ok["payload"]["after"]["parts"]).Count == 1, "Successful attach records request, before/after and link");
            rig.shop.RecordEquipment(request, rig.State(), true, "탄창이 줄어 남는 장전탄을 돌려받을 예비탄 칸이 없습니다.", rig.State());
            var blocked = rig.Events("shop_equipment_result").Last();
            Check(!(bool)blocked["payload"]["success"] && !(bool)blocked["payload"]["changed"] &&
                (string)blocked["payload"]["failureCode"] == "ammo_overflow", "Rejected attach records reason code without change");
            rig.shop.RecordEquipment(new LogShopEquipmentRequest { action = "akimbo_set", akimbo = true }, rig.State("right"), true, "", rig.State("right"));
            Check((bool)rig.Events("shop_equipment_result").Last()["payload"]["success"] &&
                !(bool)rig.Events("shop_equipment_result").Last()["payload"]["changed"], "No-op akimbo success is marked unchanged");
            rig.shop.RecordEquipment(request, rig.State(), false, null, rig.State());
            Check((string)rig.Events("shop_equipment_result").Last()["payload"]["failureCode"] == "exception", "Exception path is a failure");
            Check(LogShopCollector.FailureCode("같은 권총 두 자루가 필요합니다.") == "akimbo_needs_pair" &&
                LogShopCollector.FailureCode("휴대 무기에 장착된 파츠가 아닙니다.") == "part_not_attached" &&
                LogShopCollector.FailureCode("바닥에 놓인 무기에 붙어 있습니다. 그 무기를 주워야 옮길 수 있습니다.") == "attached_to_ground_weapon",
                "Equipment failure messages map to stable codes");
        }
    }

    private static void Focus()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.ObserveFocus(null, new LogShopControl { placement = "next_wave" }, "programmatic");
            rig.shop.ObserveFocus(new LogShopControl { placement = "next_wave" }, new LogShopControl { placement = "stat", row = 2 }, "player");
            var rows = rig.Events("shop_focus_changed");
            Check(rows.Length == 2 && (string)rows[0]["payload"]["cause"] == "programmatic" && (int)rows[1]["payload"]["to"]["row"] == 2 &&
                rows[1]["payload"]["visitId"] != null, "Focus navigation records cause and from/to controls");
        }
    }

    private static void PointerAndScroll()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.ObservePointerEnter(new LogShopControl { placement = "weapon_catalog", row = 0, label = "M1873" });
            rig.shop.ObservePointerExit(new LogShopControl { placement = "weapon_catalog", row = 0, label = "M1873" });
            var enters = rig.Events("shop_pointer_enter");
            var exits = rig.Events("shop_pointer_exit");
            Check(enters.Length == 1 && (string)enters[0]["payload"]["control"]["placement"] == "weapon_catalog" &&
                enters[0]["payload"]["visitId"] != null, "Pointer enter records control and visit");
            Check(exits.Length == 1 && (string)exits[0]["payload"]["control"]["label"] == "M1873", "Pointer exit records control");

            rig.shop.ObserveScroll("weapon_catalog", 1.0, 0.5, "player");
            rig.shop.ObserveScroll("weapon_catalog", 0.5, 0.5, "player");
            var scrolls = rig.Events("shop_scroll_changed");
            Check(scrolls.Length == 1 && (string)scrolls[0]["payload"]["view"] == "weapon_catalog" &&
                Math.Abs((double)scrolls[0]["payload"]["previousPosition"] - 1.0) < 1e-6 &&
                Math.Abs((double)scrolls[0]["payload"]["newPosition"] - 0.5) < 1e-6 &&
                (string)scrolls[0]["payload"]["cause"] == "player", "Scroll change records view, positions and cause without no-op duplication");
        }
    }

    private static void SessionEndInsideShop()
    {
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.Present(true, rig.money, "m1873", "", new LogShopOffer[0]);
            string input = rig.shop.BeginInput("stat_buy", null, null, rig.money, null, null);
            rig.shop.Begin(new LogShopProduct { itemType = "Stat", itemId = "hp", productId = "Stat:hp", valid = true }, rig.State(), null);
            rig.life.End("application_quit", null, null);
            string[] types = rig.Types();
            var closed = rig.Events("shop_closed").Single();
            Check((string)closed["payload"]["reason"] == "session_end" && (string)closed["payload"]["detail"] == "application_quit" &&
                rig.Events("shop_purchase_incomplete").Length == 1 && types.Last() == "session_end" &&
                Array.IndexOf(types, "shop_purchase_incomplete") < Array.IndexOf(types, "shop_closed"),
                "Interrupted session closes pending purchase, then visit, before session_end");
            Check(rig.Events("shop_view_hidden").Single()["payload"]["reason"].ToString() == "shop_closed", "Interrupted visit hides view once");
            rig.shop.EndInput(input, null);
            rig.shop.Closed("skip", 0, rig.State());
            rig.shop.ObserveFocus(null, null, "player");
            Check(rig.Types().Last() == "session_end", "No shop records after session end");
        }
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.Closed("session_end", 3, rig.State(), "return_to_title");
            rig.life.End("return_to_title", null, null);
            Check(rig.Events("shop_closed").Length == 1 && rig.Events("shop_closed")[0]["payload"]["state"].Type == JTokenType.Object,
                "GameManager close with final state is not duplicated by the session-end observer");
        }
        using (var rig = new Rig())
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            string input = rig.shop.BeginInput("akimbo_toggle", null, null, rig.money, null, null);
            rig.life.End("unknown", null, null);
            var ignored = rig.Events("shop_ui_input_ignored").SingleOrDefault();
            Check(ignored != null && (string)ignored["payload"]["interactionId"] == input && (string)ignored["payload"]["reason"] == "session_end" &&
                rig.Events("shop_closed").Single()["payload"]["interactionId"].Type == JTokenType.Null,
                "Unfinished input at session end is resolved as interrupted, not as the cause of the close");
        }
    }

    private static void DisabledAndFailures()
    {
        var disabled = JsonConvert.DeserializeObject<LogData>(JsonConvert.SerializeObject(settings));
        disabled.enabled = false;
        using (var rig = new Rig(disabled))
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.Present(true, 1, "m1873", "", new LogShopOffer[0]);
            string id = rig.shop.BeginInput("next_wave", null, null, 1, null, null);
            rig.shop.EndInput(id, null);
            rig.shop.RecordEquipment(null, null, true, "", null);
            rig.shop.Closed("skip", 0, null);
            Check(!rig.shop.IsRecording && id == null && rig.store.Lines.Count == 0, "Disabled telemetry writes nothing and returns no IDs");
        }
        using (var rig = new Rig(null, _id => throw new IOException("synthetic open failure")))
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.ObserveFocus(null, null, "player");
            Check(!rig.shop.IsRecording && rig.session.Diagnostics.StorageFailed, "Store open failure disables shop logging without throwing");
        }
        var tiny = JsonConvert.DeserializeObject<LogData>(JsonConvert.SerializeObject(settings));
        tiny.maxBufferedRecords = 1;
        using (var rig = new Rig(tiny, _id => new ThrowingStore()))
        {
            rig.shop.Opened(2, 30, 30, rig.State());
            rig.shop.Present(true, 1, "m1873", "", new LogShopOffer[0]);
            string id = rig.shop.BeginInput("next_wave", null, null, 1, null, null);
            rig.shop.EndInput(id, null);
            Check(!rig.shop.IsRecording && rig.session.Diagnostics.StorageFailed, "Write failure mid-session stops recording without throwing");
        }
        using (var rig = new Rig())
        {
            var bad = rig.State();
            bad.maxHealth = double.NaN;
            rig.shop.Opened(2, 30, 30, bad);
            rig.shop.RecordEquipment(new LogShopEquipmentRequest { action = "part_detach" }, bad, true, "", bad);
            Check(rig.session.Diagnostics.FailureCount == 2 && rig.Events("shop_opened").Length == 0 &&
                rig.Events("shop_equipment_result").Length == 0 && rig.shop.VisitId == null && rig.shop.IsRecording,
                "Collection failure is reported and skipped while the session keeps recording");
        }
    }

    private static void Compatibility()
    {
        using (var rig = new Rig())
        {
            rig.shop.Present(true, 7, "m1873", "p", new LogShopOffer[0]);
            var payload = rig.Events("shop_offers_presented").Single()["payload"];
            var keys = ((JObject)payload["presentation"]).Properties().Select(_p => _p.Name).ToArray();
            Check(keys.SequenceEqual(new[] { "balance", "selectedWeaponId", "selectedPartId", "offers" }) &&
                (int)payload["revision"] == 0 && payload["offerSetId"] != null, "Legacy offer presentation shape is unchanged without inventory");
            Check(LogShopCollector.FailureCode("소지금이 부족합니다.") == "insufficient_funds" && LogShopCollector.FailureCode("품절입니다.") == "sold_out" &&
                LogShopCollector.FailureCode("정비 시간에만 구매할 수 있습니다.") == "shop_closed" && LogShopCollector.FailureCode("??") == "unknown",
                "Existing failure codes unchanged");
        }
    }

    // 실제 경로가 수집기를 부르는지 원본에서 확인한다(플레이 없이 빠르게 잡는 회귀 검사).
    private static void StaticWiring(string _root)
    {
        string scripts = Path.Combine(_root, "Assets", "Scripts");
        string presenter = File.ReadAllText(Path.Combine(scripts, "UI", "ShopPresenter.cs"));
        foreach (string handler in new[] { "OnWeaponSelected", "OnWeaponBuyClicked", "OnPartSelected", "OnPartBuyClicked", "OnPartAttachClicked",
            "OnPartDetachClicked", "OnSlotCardClicked", "OnAkimboClicked", "OnStatClicked", "OnSupplyClicked", "OnUpgradeClicked", "OnNextWaveClicked" })
        {
            int start = presenter.IndexOf("private void " + handler + "(", StringComparison.Ordinal);
            int body = start >= 0 ? presenter.IndexOf('{', start) : -1;
            int next = body >= 0 ? presenter.IndexOf("\n    }", body, StringComparison.Ordinal) : -1;
            Check(next > body && presenter.Substring(body, next - body).Contains("using var input = LogInput("), handler + " records its UI input");
        }
        Check(presenter.Contains("view.PointerEntered +=") && presenter.Contains("view.PointerExited +=") && presenter.Contains("view.ScrollChanged +="),
            "ShopPresenter wires pointer and scroll events");
        string game = File.ReadAllText(Path.Combine(scripts, "Game", "GameManager.cs"));
        Check(game.Contains("StartNextWave(\"timeout\")") && game.Contains("StartNextWave(\"skip\")") && game.Contains("ObserveShopPhase(previous, _next)"),
            "Timeout and skip reach distinct close reasons");
        foreach (string method in new[] { "public string TrySetAkimbo(bool _on)", "public string TryAttachPart(PartInstance _part, int _slot)",
            "public string TryAttachPart(string _partId, int _slot)", "public string TryDetachPart(PartInstance _part)",
            "public string TryDetachPart(string _partId, int _slot)", "public string TryDetachPart(string _partId)" })
        {
            int at = game.IndexOf(method, StringComparison.Ordinal);
            int call = at >= 0 ? game.IndexOf("RunLoggedEquipment(", at, StringComparison.Ordinal) : -1;
            Check(call > at && call - at < 200, method + " logs its result");
        }
        string logging = File.ReadAllText(Path.Combine(scripts, "Game", "GameManager.Logging.cs"));
        Check(logging.Contains("ObserveShopSessionEnd("), "Session end inside the shop closes the visit with final state");
        string data = File.ReadAllText(Path.Combine(scripts, "Logging", "LogStore.cs")) + File.ReadAllText(Path.Combine(scripts, "Logging", "LogShopCollector.cs"));
        Check(!data.Contains("UnityWebRequest") && !data.Contains("HttpClient"), "Shop telemetry stays local (no upload client)");
    }

    private static void Check(bool _condition, string _message)
    {
        checks++;
        if (!_condition)
        {
            throw new InvalidDataException("Check failed: " + _message);
        }
    }
}
