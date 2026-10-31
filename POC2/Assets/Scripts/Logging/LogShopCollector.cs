using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class LogShopRowState
{
    public bool interactable;
    public string label;
    public string priceText;
}

public static class LogShopVisibility
{
    public static bool Overlaps(double _minX, double _maxX, double _minY, double _maxY,
        double _clipMinX, double _clipMaxX, double _clipMinY, double _clipMaxY)
    {
        return _maxX > _minX && _maxY > _minY && _clipMaxX > _clipMinX && _clipMaxY > _clipMinY &&
            _maxX > _clipMinX && _minX < _clipMaxX && _maxY > _clipMinY && _minY < _clipMaxY;
    }
}

public sealed class LogShopProduct
{
    public string itemType;
    public string itemId;
    public string productId;
    public int? price;
    public int? targetSlot;
    public int? resolvedTargetSlot;
    public string targetWeaponInstanceId;
    public int? offerIndex;
    public bool valid;
}

public sealed class LogShopOffer
{
    public LogShopProduct product;
    public string placement;
    public int row;
    public string state;
    public bool purchasable;
    public string failureCode;
    public string displayReason;
    public string label;
    public string priceText;
    public bool selected;
}

public sealed class LogShopOfferReference
{
    public string offerSetId;
    public int revision;
    public string productId;
    public string placement;
    public int row;
}

public sealed class LogShopPart
{
    public string instanceId;
    public string partId;
    public string attachedWeaponInstanceId;
}

public sealed class LogShopSnapshot
{
    public LogPlayerSnapshot player;
    public double? maxHealth;
    public double? damageTakenMultiplier;
    public double? moveSpeedMultiplier;
    public double? reloadMultiplier;
    public int? ammoCapacityBonus;
    public LogShopPart[] parts;
    public Dictionary<string, int> stats;
    public Dictionary<string, int> perks;
    public LogShopUpgrade[] upgrades;
}

public sealed class LogShopUpgrade
{
    public int index;
    public string perkId;
    public bool bought;
    public bool soldOut;
}

public sealed class LogShopControl
{
    public string placement;
    public int? row;
    public string label;
    public string priceText;
    public bool? interactable;
}

public sealed class LogShopSelection
{
    public string weaponId;
    public string partId;
}

public sealed class LogShopInventory
{
    public string activeHand;
    public int? activeSlot;
    public LogWeaponSnapshot[] weapons;
    public LogReserveSnapshot[] reserves;
    public LogShopPart[] parts;
}

public sealed class LogShopEquipmentRequest
{
    public string action;
    public string partId;
    public string partInstanceId;
    public int? targetSlot;
    public string targetWeaponInstanceId;
    public string sourceWeaponInstanceId;
    public bool? akimbo;
}

public sealed class LogPurchaseContext
{
    public string AttemptId { get; }
    internal readonly LogShopCollector owner;
    internal readonly JToken product;
    internal readonly JToken before;
    internal readonly JToken offer;
    internal readonly List<JToken> ammo = new List<JToken>();
    internal bool closed;

    internal LogPurchaseContext(LogShopCollector _owner, string _id, JToken _product, JToken _before, JToken _offer)
    {
        owner = _owner;
        AttemptId = _id;
        product = _product;
        before = _before;
        offer = _offer;
    }
}

public sealed class LogShopCollector : ILogSessionEndObserver
{
    private readonly LogGameLifecycle lifecycle;
    private readonly HashSet<LogPurchaseContext> pending = new HashSet<LogPurchaseContext>();
    private JToken presentation;
    private string offerSetId;
    private int revision;
    private string visitId;
    private double visitStartedSeconds;
    private bool shown;
    private JToken selection;
    private string inputId;
    private bool inputLinked;
    public bool IsRecording => Active;
    public string VisitId => visitId;
    public string InputId => inputId;

    public LogShopCollector(LogGameLifecycle _lifecycle)
    {
        lifecycle = _lifecycle ?? throw new ArgumentNullException(nameof(_lifecycle));
        lifecycle.RegisterSessionEndObserver(this);
    }

    public static string ProductId(string _type, string _id)
    {
        return string.IsNullOrEmpty(_id) ? null : _type + ":" + Uri.EscapeDataString(_id);
    }

    public void Present(bool _visible, int? _balance, string _weapon, string _part, LogShopOffer[] _offers,
        LogShopInventory _inventory = null, string _hideReason = null)
    {
        if (!_visible)
        {
            Observe(() => Hide(_hideReason ?? "not_visible"));
            shown = false;
            ResetPresentation();
            return;
        }
        Observe(() =>
        {
            if (!shown)
            {
                JToken initial = LogJson.CopyPayload(new LogShopSelection { weaponId = _weapon, partId = _part });
                if (!lifecycle.Session.TryRecord("shop_view_shown", new { visitId, selection = initial, inventory = _inventory }))
                {
                    return;
                }
                shown = true;
                selection = initial;
            }
            ObserveSelection(_weapon, _part, "refresh", false);
            JToken next = LogJson.CopyPayload(new { balance = _balance, selectedWeaponId = _weapon, selectedPartId = _part,
                offers = _offers ?? Array.Empty<LogShopOffer>() });
            if (_inventory != null)
            {
                next["inventory"] = LogJson.CopyPayload(_inventory);
            }
            if (JToken.DeepEquals(next, presentation))
            {
                return;
            }
            string nextSet = offerSetId ?? lifecycle.Session.NewId(LogIdKind.OfferSet);
            int nextRevision = offerSetId == null ? 0 : revision + 1;
            if (lifecycle.Session.TryRecord("shop_offers_presented", new { offerSetId = nextSet, revision = nextRevision, presentation = next, visitId }))
            {
                offerSetId = nextSet;
                revision = nextRevision;
                presentation = next;
            }
            else
            {
                ResetPresentation();
            }
        });
    }

    // 정비 시간 진입. 화면 표시(shop_view_shown)와 별개로 게임 상태가 상점으로 바뀐 순간이다.
    public void Opened(int? _nextWave, double? _intermissionSeconds, double? _duration, LogShopSnapshot _state)
    {
        Observe(() =>
        {
            if (visitId != null)
            {
                CloseVisit("reopened", null, null, null, null);
            }
            string id = lifecycle.Session.NewId(LogIdKind.ShopVisit);
            if (lifecycle.Session.TryRecord("shop_opened", new { visitId = id, nextWaveNumber = _nextWave,
                intermissionSeconds = _intermissionSeconds, durationSeconds = _duration, utc = lifecycle.Session.ReadUtc(),
                state = LogJson.CopyPayload(_state) }))
            {
                visitId = id;
                visitStartedSeconds = lifecycle.Session.ElapsedSeconds;
                selection = null;
            }
        });
    }

    // _reason: skip(다음 웨이브 버튼·직접 호출) / timeout(정비 시간 만료) / phase_changed / session_end.
    public void Closed(string _reason, double? _intermissionLeft, LogShopSnapshot _state, string _detail = null, string _nextPhase = null)
    {
        Observe(() => CloseVisit(_reason, _intermissionLeft, _state, _detail, _nextPhase));
    }

    // 플레이어 조작 한 번. 이후 같은 조작으로 생긴 선택·구매·장착·퇴장 이벤트가 interactionId로 이어진다.
    public string BeginInput(string _action, LogShopControl _control, int? _targetSlot, int? _balance, string _weapon, string _part)
    {
        string id = null;
        Observe(() =>
        {
            if (inputId != null)
            {
                EndInput(inputId, "superseded");
            }
            string next = lifecycle.Session.NewId(LogIdKind.ShopInput);
            if (lifecycle.Session.TryRecord("shop_ui_input", new { interactionId = next, visitId, action = _action, control = _control,
                targetSlot = _targetSlot, balance = _balance, selection = new LogShopSelection { weaponId = _weapon, partId = _part },
                visitElapsedSeconds = VisitElapsed() }))
            {
                id = next;
                inputId = next;
                inputLinked = false;
            }
        });
        return id;
    }

    // 조작이 아무 게임 이벤트도 만들지 않았으면 그 사유를 남긴다.
    public void EndInput(string _interactionId, string _ignoredReason)
    {
        if (_interactionId == null || _interactionId != inputId)
        {
            return;
        }
        bool linked = inputLinked;
        inputId = null;
        inputLinked = false;
        if (!linked)
        {
            Observe(() => lifecycle.Session.TryRecord("shop_ui_input_ignored", new { interactionId = _interactionId, visitId,
                reason = _ignoredReason ?? "no_effect" }));
        }
    }

    // _explicit: 선택 조작이면 값이 그대로여도 기록한다. 아니면 바뀐 경우만 기록한다(자동 선택 등).
    public void ObserveSelection(string _weapon, string _part, string _source, bool _explicit)
    {
        Observe(() =>
        {
            JToken next = LogJson.CopyPayload(new LogShopSelection { weaponId = _weapon, partId = _part });
            bool changed = !JToken.DeepEquals(next, selection);
            if ((!changed && !_explicit) || (!changed && inputId == null))
            {
                return;
            }
            if (RecordLinked("shop_selection_changed", new { visitId, interactionId = inputId, source = _source, changed,
                before = selection, after = next }))
            {
                selection = next;
            }
        });
    }

    // 키보드·게임패드 탐색 또는 클릭으로 바뀐 UI 포커스. cause=programmatic은 화면 코드가 옮긴 경우다.
    public void ObserveFocus(LogShopControl _from, LogShopControl _to, string _cause)
    {
        Observe(() => lifecycle.Session.TryRecord("shop_focus_changed", new { visitId, interactionId = inputId, cause = _cause,
            from = _from, to = _to }));
    }

    // 마우스 포인터 진입 (호버).
    public void ObservePointerEnter(LogShopControl _control)
    {
        Observe(() => lifecycle.Session.TryRecord("shop_pointer_enter", new { visitId, interactionId = inputId, control = _control }));
    }

    // 마우스 포인터 이탈.
    public void ObservePointerExit(LogShopControl _control)
    {
        Observe(() => lifecycle.Session.TryRecord("shop_pointer_exit", new { visitId, interactionId = inputId, control = _control }));
    }

    // 스크롤 위치 변화 (뷰 이름, 이전 정규화 위치, 새 정규화 위치, 발생 원인).
    public void ObserveScroll(string _view, double? _previousPosition, double _newPosition, string _cause)
    {
        if (_previousPosition.HasValue && Math.Abs(_previousPosition.Value - _newPosition) < 1e-6)
        {
            return;
        }
        Observe(() => lifecycle.Session.TryRecord("shop_scroll_changed", new { visitId, interactionId = inputId,
            view = _view, previousPosition = _previousPosition, newPosition = _newPosition, before = _previousPosition, after = _newPosition, cause = _cause }));
    }

    // 파츠 장착·분리·아킴보 전환의 게임 결과. 무료 조작이라 구매(shop_purchase_*)와 나눈다.
    public void RecordEquipment(LogShopEquipmentRequest _request, LogShopSnapshot _before, bool _returned, string _failure, LogShopSnapshot _after)
    {
        Observe(() =>
        {
            JToken before = LogJson.CopyPayload(_before);
            JToken after = LogJson.CopyPayload(_after);
            bool success = _returned && string.IsNullOrEmpty(_failure);
            bool changed = !JToken.DeepEquals(Field(before, "parts"), Field(after, "parts")) ||
                !JToken.DeepEquals(Field(Field(before, "player"), "weapons"), Field(Field(after, "player"), "weapons")) ||
                !JToken.DeepEquals(Field(Field(before, "player"), "activeHand"), Field(Field(after, "player"), "activeHand"));
            RecordLinked("shop_equipment_result", new { actionId = lifecycle.Session.NewId(LogIdKind.ShopAction), visitId,
                interactionId = inputId, request = _request, success, changed,
                failureCode = success ? null : _returned ? FailureCode(_failure) : "exception",
                displayReason = _failure, before, after });
        });
    }

    private void CloseVisit(string _reason, double? _intermissionLeft, LogShopSnapshot _state, string _detail, string _nextPhase)
    {
        if (visitId == null)
        {
            return;
        }
        Hide("shop_closed");
        try
        {
            RecordLinked("shop_closed", new { visitId, reason = _reason, detail = _detail, nextPhase = _nextPhase, interactionId = inputId,
                intermissionLeft = _intermissionLeft, dwellSeconds = VisitElapsed(), utc = lifecycle.Session.ReadUtc(),
                state = LogJson.CopyPayload(_state) });
        }
        finally
        {
            visitId = null;
            selection = null;
            ResetPresentation();
        }
    }

    private void Hide(string _reason)
    {
        if (!shown)
        {
            return;
        }
        shown = false;
        lifecycle.Session.TryRecord("shop_view_hidden", new { visitId, reason = _reason, offerSetId, revision = offerSetId != null ? revision : (int?)null });
    }

    private void ResetPresentation()
    {
        offerSetId = null;
        presentation = null;
        revision = 0;
    }

    private bool RecordLinked(string _eventType, object _payload)
    {
        bool recorded = lifecycle.Session.TryRecord(_eventType, _payload);
        if (recorded && inputId != null)
        {
            inputLinked = true;
        }
        return recorded;
    }

    private double? VisitElapsed() => visitId != null ? lifecycle.Session.ElapsedSeconds - visitStartedSeconds : (double?)null;

    public LogShopOfferReference FindOffer(string _type, string _id, string _placement, int? _targetSlot = null)
    {
        if (offerSetId == null || presentation == null)
        {
            return null;
        }
        string id = ProductId(_type, _id);
        JToken row = presentation["offers"].FirstOrDefault(_offer => id != null &&
            (string)_offer["product"]["productId"] == id && (string)_offer["placement"] == _placement &&
            (!_targetSlot.HasValue || (int?)_offer["product"]["targetSlot"] == _targetSlot));
        return row == null ? null : new LogShopOfferReference { offerSetId = offerSetId, revision = revision,
            productId = id, placement = _placement, row = (int)row["row"] };
    }

    public LogPurchaseContext Begin(LogShopProduct _product, LogShopSnapshot _before, LogShopOfferReference _reference)
    {
        LogPurchaseContext context = null;
        Observe(() =>
        {
            if (_product == null)
            {
                throw new ArgumentNullException(nameof(_product));
            }
            JToken product = LogJson.CopyPayload(_product);
            JToken before = LogJson.CopyPayload(_before);
            JToken offer = JValue.CreateNull();
            if (_reference != null && _reference.offerSetId == offerSetId && _reference.revision == revision &&
                _reference.productId == _product?.productId && presentation?["offers"] != null &&
                presentation["offers"].Any(_row => (string)_row["product"]["productId"] == _reference.productId &&
                    (string)_row["placement"] == _reference.placement && (int)_row["row"] == _reference.row &&
                    (int?)_row["product"]["offerIndex"] == _product.offerIndex &&
                    ((int?)_row["product"]["targetSlot"] == _product.targetSlot || !_product.targetSlot.HasValue)))
            {
                offer = LogJson.CopyPayload(_reference);
            }
            var candidate = new LogPurchaseContext(this, lifecycle.Session.NewId(LogIdKind.PurchaseAttempt), product, before, offer);
            if (RecordLinked("shop_purchase_attempt", new { attemptId = candidate.AttemptId, product, offer, before, visitId, interactionId = inputId }))
            {
                context = candidate;
                pending.Add(context);
            }
        });
        return context;
    }

    public void ObserveAmmo(LogAmmoChange _change)
    {
        Observe(() =>
        {
            foreach (LogPurchaseContext context in pending)
            {
                if (context.AttemptId == _change?.causeId)
                {
                    context.ammo.Add(LogJson.CopyPayload(_change));
                    break;
                }
            }
        });
    }

    public void Complete(LogPurchaseContext _context, bool _returned, string _failure, string _failureCode, LogShopSnapshot _after)
    {
        if (!Owns(_context))
        {
            return;
        }
        bool recorded = false;
        try
        {
            Observe(() =>
            {
                JToken after = LogJson.CopyPayload(_after);
                bool success = _returned && string.IsNullOrEmpty(_failure);
                recorded = lifecycle.Session.TryRecord("shop_purchase_result", new
                {
                    attemptId = _context.AttemptId, product = _context.product, offer = _context.offer,
                    success, failureCode = success ? null : _returned ? _failureCode : "exception",
                    displayReason = _failure, before = _context.before, after,
                    actualSpent = Money(_context.before).HasValue && Money(after).HasValue
                        ? (long?)Money(_context.before).Value - Money(after).Value : null,
                    actualHealthRestored = Health(_context.before).HasValue && Health(after).HasValue
                        ? (double?)Math.Max(0, Health(after).Value - Health(_context.before).Value) : null,
                    createdWeaponIds = NewIds(Field(Field(_context.before, "player"), "weapons"), Field(Field(after, "player"), "weapons")),
                    createdPartIds = NewIds(Field(_context.before, "parts"), Field(after, "parts")),
                    ammoChanges = _context.ammo
                });
            });
        }
        finally
        {
            if (!recorded && Active)
            {
                Incomplete(_context, "result_collection_failed");
            }
            else
            {
                Close(_context);
            }
        }
    }

    public void SessionEnding(string _reason)
    {
        foreach (LogPurchaseContext context in pending.ToArray())
        {
            Incomplete(context, _reason);
        }
        EndInput(inputId, "session_end");
        Observe(() => CloseVisit("session_end", null, null, _reason, null));
        Present(false, null, null, null, null, null, "session_end");
    }

    public static string FailureCode(string _reason)
    {
        switch (_reason)
        {
            case "": return null;
            case "정비 시간에만 구매할 수 있습니다.": return "shop_closed";
            case "소지금이 부족합니다.": return "insufficient_funds";
            case "아직 판매하지 않는 시대의 무기입니다.":
            case "아직 판매하지 않는 파츠입니다.":
            case "아직 판매하지 않는 탄약입니다.": return "locked";
            case "무기 데이터를 찾지 못했습니다.":
            case "파츠 데이터를 찾지 못했습니다.":
            case "스탯 데이터를 찾지 못했습니다.":
            case "탄약 상품을 찾지 못했습니다.":
            case "업그레이드 데이터를 찾지 못했습니다.": return "invalid_product";
            case "교체할 슬롯을 선택하세요.": return "invalid_target_slot";
            case "무기 픽업 프리팹을 찾지 못했습니다.": return "missing_pickup_prefab";
            case "교체한 무기를 놓을 빈 바닥이 없습니다.": return "no_drop_space";
            case "탄약 구매 처리 중입니다.": return "purchase_in_progress";
            case "이미 최대 예비탄입니다.": return "maximum_reserve";
            case "보충할 예비탄이 없습니다.": return "no_effect";
            case "품절입니다.": return "sold_out";
            case "이미 구매했습니다.": return "already_purchased";
            case "보유하지 않은 파츠입니다.": return "part_not_owned";
            case "이 파츠를 붙일 수 없는 무기입니다.": return "incompatible_weapon";
            case "바닥에 놓인 무기에 붙어 있습니다. 그 무기를 주워야 옮길 수 있습니다.": return "attached_to_ground_weapon";
            case "이미 장착되어 있습니다.": return "already_attached";
            case "탄창이 줄어 남는 장전탄을 돌려받을 예비탄 칸이 없습니다.": return "ammo_overflow";
            case "휴대 무기에 장착된 파츠가 아닙니다.":
            case "그 무기에 장착된 파츠가 아닙니다.": return "part_not_attached";
            case "권총류만 아킴보로 들 수 있습니다.": return "akimbo_not_pistol";
            case "같은 권총 두 자루가 필요합니다.": return "akimbo_needs_pair";
            default: return "unknown";
        }
    }

    public static string ClassifyFailure(string _reason, LogShopProduct _product, int? _offerCount = null, bool? _bought = null)
    {
        string code = FailureCode(_reason);
        if (string.IsNullOrEmpty(_reason) || code == "shop_closed" || _product == null)
        {
            return code;
        }
        if (_product.itemType == "SlotUpgrade" && _offerCount.HasValue &&
            (!_product.offerIndex.HasValue || _product.offerIndex < 0 || _product.offerIndex >= _offerCount))
        {
            return "invalid_offer_index";
        }
        if (code != "sold_out" && !_product.valid)
        {
            return "invalid_product";
        }
        return _product.itemType == "SlotUpgrade" && code == "already_purchased" && _bought == false
            ? "purchase_limit" : code;
    }

    private void Incomplete(LogPurchaseContext _context, string _reason)
    {
        try
        {
            Observe(() => lifecycle.Session.TryRecord("shop_purchase_incomplete", new { attemptId = _context.AttemptId, reason = _reason }));
        }
        finally
        {
            Close(_context);
        }
    }

    private bool Active => lifecycle.IsActive && lifecycle.Session.IsRecording;
    private bool Owns(LogPurchaseContext _context) => _context != null && ReferenceEquals(_context.owner, this) && !_context.closed && pending.Contains(_context);

    private void Close(LogPurchaseContext _context)
    {
        pending.Remove(_context);
        _context.closed = true;
        _context.ammo.Clear();
    }

    private static JToken Field(JToken _state, string _key) => (_state as JObject)?[_key];
    private static int? Money(JToken _state) => (int?)Field(Field(_state, "player"), "money");
    private static double? Health(JToken _state) => (double?)Field(Field(_state, "player"), "health");

    private static string[] NewIds(JToken _before, JToken _after)
    {
        if (!(_before is JArray) || !(_after is JArray))
        {
            return null;
        }
        var previous = new HashSet<string>(_before.Select(_row => (string)_row["instanceId"]));
        return _after.Select(_row => (string)_row["instanceId"]).Where(_id => _id != null && !previous.Contains(_id)).ToArray();
    }

    private void Observe(Action _action)
    {
        if (!Active)
        {
            return;
        }
        try
        {
            _action();
        }
        catch (Exception exception)
        {
            lifecycle.Session.ReportCollectionFailure(exception);
        }
    }
}
