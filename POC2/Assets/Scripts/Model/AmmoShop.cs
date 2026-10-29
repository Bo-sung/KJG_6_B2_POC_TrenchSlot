public sealed class AmmoShop
{
    private readonly ShopData table;
    private readonly AmmoInventory ammo;
    private readonly PlayerWallet wallet;
    private bool buying;

    public AmmoShop(ShopData _table, AmmoInventory _ammo, PlayerWallet _wallet)
    {
        table = _table;
        ammo = _ammo;
        wallet = _wallet;
    }

    public string TryBuy(string _itemId)
    {
        if (buying)
        {
            return "탄약 구매 처리 중입니다.";
        }
        var item = ShopInfo.Find(table, _itemId);
        if (item == null || item.type != ShopInfo.TYPE_AMMO || ammo == null || !ammo.Contains(item.target) || wallet == null)
        {
            return "탄약 상품을 찾지 못했습니다.";
        }
        if (!ShopInfo.CanRefill(ammo, item.target))
        {
            return "이미 최대 예비탄입니다.";
        }
        if (!wallet.CanAfford(item.price))
        {
            return "소지금이 부족합니다.";
        }
        buying = true;
        try
        {
            if (!wallet.TrySpend(item.price))
            {
                return "소지금이 부족합니다.";
            }
            int added = ammo.Add(item.target, ammo.GetMaximum(item.target) - ammo.GetReserve(item.target));
            if (added <= 0)
            {
                wallet.Add(item.price);
                return "보충할 예비탄이 없습니다.";
            }
            return "";
        }
        finally
        {
            buying = false;
        }
    }
}
