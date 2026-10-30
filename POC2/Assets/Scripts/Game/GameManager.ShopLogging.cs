using System;
using System.Collections.Generic;
using System.Globalization;

public sealed partial class GameManager
{
    private LogShopCollector logShop;
    public LogShopCollector ShopLog => logShop;

    public void ObserveShopExposure(bool _visible, Func<LogShopOffer[]> _capture, string _weapon, string _part)
    {
        ObserveLogging(() => logShop?.Present(_visible, wallet?.Balance, _weapon, _part, _visible ? _capture() : null));
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
            entries.Add(new LogShopPart { instanceId = telemetry.Session.SessionId + ":part:" + part.Uid.ToString(CultureInfo.InvariantCulture),
                partId = part.PartId, attachedWeaponInstanceId = telemetry.Session.GetWeaponId(part.AttachedTo) });
        }
        return entries.ToArray();
    }
}
