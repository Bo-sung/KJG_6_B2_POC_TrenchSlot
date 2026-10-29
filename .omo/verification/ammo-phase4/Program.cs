using System.Text.Json;

internal static class Program
{
    private static int CHECKS;

    private static void Main(string[] _args)
    {
        string directory = Path.Combine(Path.GetFullPath(_args[0]), "POC2/Assets/Resources");
        var options = new JsonSerializerOptions { IncludeFields = true };
        var ammoData = JsonSerializer.Deserialize<AmmoData>(File.ReadAllText(Path.Combine(directory, "AmmoData.json")), options);
        var shopData = JsonSerializer.Deserialize<ShopData>(File.ReadAllText(Path.Combine(directory, "ShopData.json")), options);
        Check(shopData.items.Length == 11, "seven existing and four ammo items");
        Check(shopData.items.Select(_item => _item.id).Distinct().Count() == 11, "unique product IDs");
        var items = shopData.items.Where(_item => _item.type == ShopInfo.TYPE_AMMO).ToArray();
        Check(items.Length == 4, "four ammo offers");
        foreach (var item in items)
        {
            Check(item.price == 200 && AmmoInfo.Find(ammoData, item.target) != null, item.id + " price and reference");
        }
        var ammo = new AmmoInventory(ammoData);
        var shortWallet = new PlayerWallet(199);
        var shortShop = new AmmoShop(shopData, ammo, shortWallet);
        Check(shortShop.TryBuy("ammo_pistol") != "" && shortWallet.Balance == 199 && ammo.GetReserve("pistol") == 36, "199 refuses without changes");
        var wallet = new PlayerWallet(200);
        var shop = new AmmoShop(shopData, ammo, wallet);
        Check(shop.TryBuy("ammo_pistol") == "" && wallet.Balance == 0 && ammo.GetReserve("pistol") == 60, "200 refills reserve");
        Check(shop.TryBuy("ammo_pistol") != "" && wallet.Balance == 0 && ammo.GetReserve("pistol") == 60, "repeat full purchase refuses");
        Check(ammo.GetReserve("sniper") == 30, "other reserve unchanged");
        var fullWallet = new PlayerWallet(400);
        var fullShop = new AmmoShop(shopData, ammo, fullWallet);
        Check(fullShop.TryBuy("ammo_pistol") != "" && fullWallet.Balance == 400, "full reserve does not charge affordable wallet");
        ammo.Consume("pistol", int.MaxValue);
        Check(fullShop.TryBuy("ammo_pistol") == "" && fullWallet.Balance == 200 && ammo.GetReserve("pistol") == 60, "empty reserve refills");
        ammo.SetMaximum("pistol", 100, false);
        Check(ShopInfo.CanRefill(ammo, "pistol"), "capacity change enables refill");
        Check(fullShop.TryBuy("ammo_pistol") == "" && fullWallet.Balance == 0 && ammo.GetReserve("pistol") == 100, "current runtime maximum used");
        var allAmmo = new AmmoInventory(ammoData);
        var allWallet = new PlayerWallet(800);
        var allShop = new AmmoShop(shopData, allAmmo, allWallet);
        foreach (var item in items)
        {
            Check(allShop.TryBuy(item.id) == "" && allAmmo.GetReserve(item.target) == allAmmo.GetMaximum(item.target), "refills " + item.target + " without weapon ownership dependency");
        }
        Check(allWallet.Balance == 0, "four purchases charge 800 total");
        Check(allShop.TryBuy("missing") != "" && allWallet.Balance == 0, "unknown product refused");
        Check(allShop.TryBuy("buy_bolt") != "" && allWallet.Balance == 0, "non-ammo product refused by ammo model");
        Check(!ShopInfo.CanRefill(allAmmo, "missing"), "unknown ammo cannot refill");
        var reentrantAmmo = new AmmoInventory(ammoData);
        var reentrantWallet = new PlayerWallet(400);
        var reentrantShop = new AmmoShop(shopData, reentrantAmmo, reentrantWallet);
        string nestedWalletResult = null;
        string nestedAmmoResult = null;
        int charges = 0;
        reentrantWallet.BalanceChanged += _balance =>
        {
            charges++;
            nestedWalletResult = reentrantShop.TryBuy("ammo_sniper");
        };
        reentrantAmmo.Changed += (_id, _reserve, _maximum) => nestedAmmoResult = reentrantShop.TryBuy("ammo_sniper");
        Check(reentrantShop.TryBuy("ammo_pistol") == "", "outer reentrant purchase succeeds");
        Check(nestedWalletResult != "" && nestedAmmoResult != "" && charges == 1 && reentrantWallet.Balance == 200, "wallet and ammo callback purchases blocked");
        Check(reentrantAmmo.GetReserve("pistol") == 60 && reentrantAmmo.GetReserve("sniper") == 30, "reentrant request cannot refill another ammo");
        var invalidTable = new ShopData { items = new[] { new ShopData.Item { id = "bad", type = ShopInfo.TYPE_AMMO, target = "missing", price = 200 } } };
        Check(new AmmoShop(invalidTable, ammo, fullWallet).TryBuy("bad") != "", "invalid ammo reference refused");
        Check(ShopInfo.Find(shopData, null) == null, "empty lookup safe");
        Console.WriteLine($"PASS: {CHECKS} ammo shop data/model checks; no Unity API execution.");
    }

    private static void Check(bool _condition, string _message)
    {
        if (!_condition)
        {
            throw new InvalidOperationException(_message);
        }
        CHECKS++;
    }
}
