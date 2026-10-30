// 탄약 팩 구매. 팩 하나는 테이블 발수만큼 공유 예비탄에 더하며 중복 구매할 수 있다. 최대량을 넘는 몫은 버린다.
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

    public string TryBuy(string _ammoId)
    {
        if (buying)
        {
            return "탄약 구매 처리 중입니다.";
        }
        var pack = ShopInfo.FindAmmoPack(table, _ammoId);
        if (pack == null || ammo == null || !ammo.Contains(_ammoId) || wallet == null)
        {
            return "탄약 상품을 찾지 못했습니다.";
        }
        if (!ShopInfo.CanAddAmmo(ammo, _ammoId))
        {
            return "이미 최대 예비탄입니다.";
        }
        int price = ShopInfo.AmmoPackPrice(table, pack);
        if (!wallet.CanAfford(price))
        {
            return "소지금이 부족합니다.";
        }
        buying = true;
        try
        {
            if (!wallet.TrySpend(price))
            {
                return "소지금이 부족합니다.";
            }
            int added = ammo.Add(_ammoId, ShopInfo.AmmoPackRounds(table));
            if (added <= 0)
            {
                wallet.Add(price);
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
