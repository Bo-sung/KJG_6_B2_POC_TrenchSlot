using System.Text.Json;

internal static class Program
{
    private static int CHECKS;

    private static void Main(string[] _args)
    {
        string directory = Path.Combine(Path.GetFullPath(_args[0]), "POC2/Assets/Resources");
        var options = new JsonSerializerOptions { IncludeFields = true };
        var ammo = JsonSerializer.Deserialize<AmmoData>(File.ReadAllText(Path.Combine(directory, "AmmoData.json")), options);
        var weapons = JsonSerializer.Deserialize<EnemyWeaponData>(File.ReadAllText(Path.Combine(directory, "EnemyWeaponData.json")), options);
        Check(ammo.pickup.dropChance == 1f && ammo.pickup.pickupDistance == 1.5f && ammo.pickup.magazineFraction == 0.5f, "approved settings");
        foreach (var pair in new Dictionary<string, int> { { "pistol", 3 }, { "sniper", 3 }, { "smg", 15 }, { "shotgun", 1 } })
        {
            Check(AmmoInfo.DropAmount(AmmoInfo.Find(ammo, pair.Key), ammo.pickup) == pair.Value, pair.Key + " rounds up half magazine");
        }
        foreach (var weapon in weapons.weapons)
        {
            string expected = weapon.id == "musket" ? "pistol" : "sniper";
            Check(weapon.dropAmmoId == expected && AmmoInfo.Find(ammo, weapon.dropAmmoId) != null, weapon.id + " ammo reference");
        }
        Check(AmmoInfo.ShouldDrop(ammo.pickup, 0f), "100 percent drops at zero roll");
        Check(AmmoInfo.ShouldDrop(ammo.pickup, 1f), "100 percent drops at inclusive Random.value upper bound");
        Check(!AmmoInfo.ShouldDrop(new AmmoData.PickupSettings { dropChance = 0f }, 0f), "zero chance never drops");
        var half = new AmmoData.PickupSettings { dropChance = 0.5f };
        Check(AmmoInfo.ShouldDrop(half, 0.499f), "below probability threshold");
        Check(!AmmoInfo.ShouldDrop(half, 0.5f), "at probability threshold");
        Check(!AmmoInfo.ShouldDrop(half, float.NaN), "invalid roll rejected");
        Check(!AmmoInfo.ShouldDrop(new AmmoData.PickupSettings { dropChance = float.NaN }, 0f), "invalid probability rejected");
        Check(!AmmoInfo.ShouldDrop(null, 0f), "missing settings rejected");
        Check(AmmoInfo.DropAmount(null, ammo.pickup) == 0, "unarmed or unknown ammo has no amount");
        Check(AmmoInfo.DropAmount(ammo.ammoTypes[0], new AmmoData.PickupSettings { magazineFraction = float.NaN }) == 0, "invalid fraction rejected");
        var inventory = new AmmoInventory(ammo);
        Check(inventory.Add("pistol", 3) == 3 && inventory.GetReserve("pistol") == 39, "normal reserve addition");
        inventory.Add("pistol", 20);
        Check(inventory.Add("pistol", 3) == 1 && inventory.GetReserve("pistol") == 60, "partial addition caps at maximum");
        Check(inventory.Add("pistol", 3) == 0, "full reserve refuses addition");
        Check(inventory.GetReserve("sniper") == 30, "other ammo untouched");
        Console.WriteLine($"PASS: {CHECKS} pure C# data/rule/inventory checks; no Unity API execution.");
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
