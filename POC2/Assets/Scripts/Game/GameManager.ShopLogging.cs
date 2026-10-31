using System;
using System.Collections.Generic;
using System.Globalization;

public sealed partial class GameManager
{
    private LogShopCollector logShop;
    public LogShopCollector ShopLog => logShop;

    private string shopCloseReason;
    private int equipmentLogDepth;

    public void ObserveShopExposure(bool _visible, Func<LogShopOffer[]> _capture, string _weapon, string _part, string _hideReason = null)
    {
        ObserveLogging(() => logShop?.Present(_visible, wallet?.Balance, _weapon, _part, _visible ? _capture() : null,
            _visible ? CaptureLogInventory() : null, _hideReason));
    }

    // 상점 UI 조작 기록. 기록 실패·비활성이면 null을 돌려줄 뿐 게임 동작에는 영향이 없다.
    public string BeginShopInput(string _action, Func<LogShopControl> _control, int? _slot, string _weapon, string _part)
    {
        string id = null;
        ObserveLogging(() => id = logShop?.BeginInput(_action, _control?.Invoke(), _slot, wallet?.Balance, _weapon, _part));
        return id;
    }

    public void EndShopInput(string _interactionId, string _ignoredReason)
    {
        ObserveLogging(() => logShop?.EndInput(_interactionId, _ignoredReason));
    }

    public void ObserveShopSelection(string _weapon, string _part, string _source)
    {
        ObserveLogging(() => logShop?.ObserveSelection(_weapon, _part, _source, true));
    }

    public void ObserveShopFocus(Func<LogShopControl> _from, Func<LogShopControl> _to, bool _programmatic)
    {
        ObserveLogging(() => logShop?.ObserveFocus(_from(), _to(), _programmatic ? "programmatic" : "player"));
    }

    public void ObserveShopPointerEnter(Func<LogShopControl> _control)
    {
        ObserveLogging(() => logShop?.ObservePointerEnter(_control?.Invoke()));
    }

    public void ObserveShopPointerExit(Func<LogShopControl> _control)
    {
        ObserveLogging(() => logShop?.ObservePointerExit(_control?.Invoke()));
    }

    public void ObserveShopScroll(string _view, double? _previous, double _new, bool _programmatic)
    {
        ObserveLogging(() => logShop?.ObserveScroll(_view, _previous, _new, _programmatic ? "programmatic" : "player"));
    }

    private void ObserveShopPhase(GamePhase _previous, GamePhase _next)
    {
        if (_previous == GamePhase.Shop && _next != GamePhase.Shop)
        {
            string reason = shopCloseReason ?? "phase_changed";
            ObserveLogging(() => logShop?.Closed(reason, intermissionLeft, CaptureLogShop(), null, LogPhase(_next)));
        }
        shopCloseReason = null;
    }

    private void ObserveShopOpened()
    {
        ObserveLogging(() => logShop?.Opened(ShopWave, intermissionLeft, ShopDuration, CaptureLogShop()));
    }

    // 세션이 상점 안에서 끝나면(타이틀 복귀·종료) 마지막 상태와 함께 방문을 닫는다.
    private void ObserveShopSessionEnd(string _reason)
    {
        if (phase == GamePhase.Shop)
        {
            ObserveLogging(() => logShop?.Closed("session_end", intermissionLeft, CaptureLogShop(), _reason));
        }
    }

    // 장착·분리·아킴보는 공개 오버로드끼리 서로 부르므로 가장 바깥 호출만 기록한다.
    private string RunLoggedEquipment(Func<LogShopEquipmentRequest> _request, Func<string> _run)
    {
        if (equipmentLogDepth > 0 || logShop == null || !logShop.IsRecording)
        {
            return _run();
        }
        LogShopEquipmentRequest request = null;
        LogShopSnapshot before = null;
        ObserveLogging(() =>
        {
            request = _request();
            before = CaptureLogShop();
        });
        bool returned = false;
        string failure = null;
        equipmentLogDepth++;
        try
        {
            failure = _run();
            returned = true;
            return failure;
        }
        finally
        {
            equipmentLogDepth--;
            ObserveLogging(() => logShop?.RecordEquipment(request, before, returned, failure, CaptureLogShop()));
        }
    }

    private LogShopEquipmentRequest LogEquipment(string _action, PartInstance _part, string _partId, int? _slot)
    {
        WeaponInstance target = _slot.HasValue ? loadout.Get(_slot.Value) : null;
        return new LogShopEquipmentRequest { action = _action, partId = _part != null ? _part.PartId : _partId,
            partInstanceId = LogPartId(_part), targetSlot = _slot, targetWeaponInstanceId = telemetry.Session.GetWeaponId(target),
            sourceWeaponInstanceId = _part != null ? telemetry.Session.GetWeaponId(_part.AttachedTo) : null };
    }

    private string LogPartId(PartInstance _part)
    {
        return _part != null ? telemetry.Session.SessionId + ":part:" + _part.Uid.ToString(CultureInfo.InvariantCulture) : null;
    }

    private LogShopInventory CaptureLogInventory()
    {
        LogPlayerSnapshot player = CaptureLogPlayer();
        return new LogShopInventory { activeHand = player.activeHand, activeSlot = player.activeSlot, weapons = player.weapons,
            reserves = player.reserves, parts = CaptureLogParts() };
    }

    public LogShopProduct QuoteLogShopProduct(string _type, string _id, int? _slot = null, int? _offerIndex = null)
    {
        var product = new LogShopProduct { itemType = _type, itemId = _id, productId = LogShopCollector.ProductId(_type, _id),
            targetSlot = _slot, offerIndex = _offerIndex };
        var table = TableManager.Instance;
        switch (_type)
        {
            case "Weapon":
                var weaponEntry = WeaponInfo.Find(table.Weapon, _id);
                var weaponOffer = ShopInfo.FindWeapon(table.Shop, _id);
                product.valid = weaponEntry != null && weaponOffer != null;
                product.price = product.valid ? weaponOffer.price : (int?)null;
                int empty = loadout.FirstEmptySlot();
                product.resolvedTargetSlot = empty >= 0 ? empty : _slot;
                product.targetWeaponInstanceId = telemetry?.Session.GetWeaponId(loadout.Get(product.resolvedTargetSlot ?? -1));
                break;
            case "Part":
                var part = PartInfo.Find(table.Part, _id);
                product.valid = part != null;
                product.price = part != null ? ShopInfo.PartPrice(table.Shop, part) : (int?)null;
                break;
            case "Stat":
                var stat = ShopInfo.FindStat(table.Shop, _id);
                product.valid = stat != null;
                product.price = stat != null ? ShopInfo.StatPrice(table.Shop, stat, stats.CountOf(_id)) : (int?)null;
                break;
            case "Ammo":
                var pack = ShopInfo.FindAmmoPack(table.Shop, _id);
                product.valid = pack != null && ammo.Contains(_id);
                product.price = product.valid ? ShopInfo.AmmoPackPrice(table.Shop, pack) : (int?)null;
                break;
            case "SlotUpgrade":
                var upgrade = ShopInfo.FindSlotUpgrade(table.Shop, _id);
                product.valid = upgrade != null;
                product.price = upgrade?.price;
                break;
        }
        return product;
    }

    private string RunLoggedPurchase(string _type, string _id, int? _slot, int? _offerIndex,
        LogShopOfferReference _reference, Func<string, string> _purchase)
    {
        LogPurchaseContext context = null;
        LogShopProduct product = null;
        ObserveLogging(() =>
        {
            product = QuoteLogShopProduct(_type, _id, _slot, _offerIndex);
            context = logShop?.Begin(product, CaptureLogShop(), _reference);
        });
        bool returned = false;
        string failure = null;
        try
        {
            failure = _purchase(context?.AttemptId);
            returned = true;
            return failure;
        }
        finally
        {
            ObserveLogging(() =>
            {
                string code = LogShopCollector.ClassifyFailure(failure, product, slotOffers.Count, slotOffers.IsBought(_offerIndex ?? -1));
                logShop?.Complete(context, returned, failure, code, CaptureLogShop());
            });
        }
    }

    private LogShopSnapshot CaptureLogShop()
    {
        var snapshot = new LogShopSnapshot { player = CaptureLogPlayer(),
            maxHealth = playerHealth != null ? playerHealth.MaxHealth : (double?)null,
            parts = CaptureLogParts(), stats = new Dictionary<string, int>(StringComparer.Ordinal),
            perks = new Dictionary<string, int>(StringComparer.Ordinal), damageTakenMultiplier = stats.DamageTakenMultiplier,
            moveSpeedMultiplier = stats.MoveSpeedMultiplier, reloadMultiplier = stats.ReloadMultiplier, ammoCapacityBonus = stats.AmmoCapacityBonus };
        foreach (var stat in TableManager.Instance.Shop.stats)
        {
            if (stat != null)
            {
                snapshot.stats[stat.id] = stats.CountOf(stat.id);
            }
        }
        foreach (var perk in TableManager.Instance.Perk.perks)
        {
            if (perk != null)
            {
                snapshot.perks[perk.id] = perks.StackOf(perk.id);
            }
        }
        var upgrades = new List<LogShopUpgrade>();
        for (int i = 0; i < slotOffers.Count; i++)
        {
            upgrades.Add(new LogShopUpgrade { index = i, perkId = slotOffers.Get(i), bought = slotOffers.IsBought(i), soldOut = slotOffers.IsSoldOut(i) });
        }
        snapshot.upgrades = upgrades.ToArray();
        return snapshot;
    }

    private LogShopPart[] CaptureLogParts()
    {
        var entries = new List<LogShopPart>();
        foreach (PartInstance part in parts.Instances)
        {
            entries.Add(new LogShopPart { instanceId = LogPartId(part),
                partId = part.PartId, attachedWeaponInstanceId = telemetry.Session.GetWeaponId(part.AttachedTo) });
        }
        return entries.ToArray();
    }
}
