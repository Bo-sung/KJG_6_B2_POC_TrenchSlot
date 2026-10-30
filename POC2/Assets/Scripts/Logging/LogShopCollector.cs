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
    public bool IsRecording => Active;

    public LogShopCollector(LogGameLifecycle _lifecycle)
    {
        lifecycle = _lifecycle ?? throw new ArgumentNullException(nameof(_lifecycle));
        lifecycle.RegisterSessionEndObserver(this);
    }

    public static string ProductId(string _type, string _id)
    {
        return string.IsNullOrEmpty(_id) ? null : _type + ":" + Uri.EscapeDataString(_id);
    }

    public void Present(bool _visible, int? _balance, string _weapon, string _part, LogShopOffer[] _offers)
    {
        if (!_visible)
        {
            offerSetId = null;
            presentation = null;
            revision = 0;
            return;
        }
        Observe(() =>
        {
            JToken next = LogJson.CopyPayload(new { balance = _balance, selectedWeaponId = _weapon, selectedPartId = _part,
                offers = _offers ?? Array.Empty<LogShopOffer>() });
            if (JToken.DeepEquals(next, presentation))
            {
                return;
            }
            string nextSet = offerSetId ?? lifecycle.Session.NewId(LogIdKind.OfferSet);
            int nextRevision = offerSetId == null ? 0 : revision + 1;
            if (lifecycle.Session.TryRecord("shop_offers_presented", new { offerSetId = nextSet, revision = nextRevision, presentation = next }))
            {
                offerSetId = nextSet;
                revision = nextRevision;
                presentation = next;
            }
            else
            {
                Present(false, null, null, null, null);
            }
        });
    }

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
            if (lifecycle.Session.TryRecord("shop_purchase_attempt", new { attemptId = candidate.AttemptId, product, offer, before }))
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
        Present(false, null, null, null, null);
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
