using System.Collections.Generic;

public sealed partial class ShopPresenter
{
    private void LateUpdate()
    {
        ObserveOffers();
    }

    private void OnDisable()
    {
        if (gameManager != null)
        {
            gameManager.ObserveShopExposure(false, null, null, null);
        }
    }

    private void ObserveOffers()
    {
        if (gameManager == null || view == null || shop == null || gameManager.ShopLog == null || !gameManager.ShopLog.IsRecording)
        {
            return;
        }
        gameManager.ObserveShopExposure(gameManager.Phase == GameManager.GamePhase.Shop && view.IsLogVisible,
            CaptureLogOffers, selectedWeaponId, selectedPartId);
    }

    private LogShopOfferReference LogOffer(string _type, string _id, string _placement, int? _slot = null)
    {
        ObserveOffers();
        return gameManager.ShopLog?.FindOffer(_type, _id, _placement, _slot);
    }

    private LogShopOffer[] CaptureLogOffers()
    {
        var offers = new List<LogShopOffer>();
        for (int i = 0; i < view.WeaponRowCount && i < shop.weapons.Length; i++)
        {
            var offer = shop.weapons[i];
            var entry = offer != null ? WeaponInfo.Find(weapons, offer.weaponId) : null;
            if (entry != null)
            {
                AddLogOffer(offers, "Weapon", entry.id, "weapon_catalog", i,
                    ShopInfo.IsWeaponUnlocked(shop, entry, gameManager.ShopWave), entry.id == selectedWeaponId);
            }
        }
        var selected = WeaponInfo.Find(weapons, selectedWeaponId);
        if (selected != null)
        {
            bool unlocked = ShopInfo.IsWeaponUnlocked(shop, selected, gameManager.ShopWave);
            AddLogOffer(offers, "Weapon", selected.id, "weapon_detail", -1, unlocked, true);
            for (int i = 0; i < view.WeaponBuyCount; i++)
            {
                AddLogOffer(offers, "Weapon", selected.id, "weapon_buy", i, unlocked, true, i);
            }
        }
        for (int i = 0; i < visibleParts.Count && i < view.PartChipCount; i++)
        {
            var part = visibleParts[i];
            AddLogOffer(offers, "Part", part.id, "part_catalog", i,
                selected != null && ShopInfo.IsWeaponUnlocked(shop, selected, gameManager.ShopWave), part.id == selectedPartId);
        }
        var selectedPart = SelectedPart();
        if (selectedPart != null)
        {
            bool unlocked = selected != null && ShopInfo.IsWeaponUnlocked(shop, selected, gameManager.ShopWave);
            AddLogOffer(offers, "Part", selectedPart.id, "part_detail", -1, unlocked, true);
            AddLogOffer(offers, "Part", selectedPart.id, "part_buy", 0, unlocked, true);
        }
        for (int i = 0; i < view.StatRowCount && i < shop.stats.Length; i++)
        {
            if (shop.stats[i] != null)
            {
                AddLogOffer(offers, "Stat", shop.stats[i].id, "stat", i);
            }
        }
        for (int i = 0; i < view.SupplyRowCount && i < shop.ammoPacks.Length; i++)
        {
            if (shop.ammoPacks[i] != null)
            {
                AddLogOffer(offers, "Ammo", shop.ammoPacks[i].ammoId, "ammo", i);
            }
        }
        for (int i = 0; i < view.UpgradeRowCount && i < gameManager.SlotOffers.Count; i++)
        {
            string state = gameManager.SlotOffers.IsSoldOut(i) ? "sold_out" : gameManager.SlotOffers.IsBought(i) ? "already_purchased" : null;
            AddLogOffer(offers, "SlotUpgrade", gameManager.SlotOffers.Get(i), "upgrade", i, true, false, null, i, state);
        }
        return offers.ToArray();
    }

    private void AddLogOffer(List<LogShopOffer> _offers, string _type, string _id, string _placement, int _row,
        bool _unlocked = true, bool _selected = false, int? _targetSlot = null, int? _offerIndex = null, string _state = null)
    {
        LogShopRowState shown = view.CaptureLogRow(_placement, _row);
        if (shown == null)
        {
            return;
        }
        LogShopProduct product = gameManager.QuoteLogShopProduct(_type, _id, _targetSlot, _offerIndex);
        string code = _state;
        if (code == null)
        {
            code = !product.valid ? "invalid_product" : !_unlocked ? "locked" :
                _type == "Ammo" && !ShopInfo.CanAddAmmo(gameManager.Ammo, _id) ? "maximum_reserve" :
                _type == "SlotUpgrade" && !gameManager.SlotOffers.CanBuy(_offerIndex ?? -1) ? "purchase_limit" :
                product.price.HasValue && !gameManager.Wallet.CanAfford(product.price.Value) ? "insufficient_funds" : null;
        }
        bool purchaseButton = _placement == "weapon_buy" || _placement == "part_buy" ||
            _placement == "stat" || _placement == "ammo" || _placement == "upgrade";
        if (code == null && purchaseButton && !shown.interactable)
        {
            code = "ui_disabled";
        }
        _offers.Add(new LogShopOffer { product = product, placement = _placement, row = _row,
            state = code ?? "available", purchasable = code == null, failureCode = code, displayReason = shown.label,
            label = shown.label, priceText = shown.priceText, selected = _selected });
    }
}
