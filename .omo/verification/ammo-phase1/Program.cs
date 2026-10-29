using System.Text.Json;

internal static class Program
{
    private static int CHECKS;

    private static void Main(string[] _args)
    {
        string root = Path.GetFullPath(_args[0]);
        var options = new JsonSerializerOptions { IncludeFields = true };
        var data = JsonSerializer.Deserialize<AmmoData>(File.ReadAllText(Path.Combine(root, "POC2/Assets/Resources/AmmoData.json")), options);
        var weapons = JsonSerializer.Deserialize<WeaponData>(File.ReadAllText(Path.Combine(root, "POC2/Assets/Resources/WeaponData.json")), options);
        var inventory = new AmmoInventory(data);
        var expected = new Dictionary<string, (int maximum, int reserve)>
        {
            { "pistol", (60, 36) }, { "sniper", (50, 30) },
            { "smg", (300, 180) }, { "shotgun", (20, 12) }
        };
        Check(data.ammoTypes.Length == 4, "four ammo types");
        Check(data.ammoTypes.Select(_entry => _entry.id).Distinct().Count() == 4, "unique IDs");
        foreach (var pair in expected)
        {
            Check(inventory.Contains(pair.Key), pair.Key + " exists");
            Check(inventory.GetReserve(pair.Key) == pair.Value.reserve, pair.Key + " starting reserve");
            Check(inventory.GetMaximum(pair.Key) == pair.Value.maximum, pair.Key + " maximum");
        }
        var mapping = new Dictionary<string, string> { { "revolver", "pistol" }, { "bolt", "sniper" }, { "smg", "smg" } };
        foreach (var weapon in weapons.weapons)
        {
            Check(weapon.ammoId == mapping[weapon.id] && inventory.Contains(weapon.ammoId), weapon.id + " ammo reference");
        }
        int events = 0;
        string changedId = null;
        int changedReserve = -1;
        int changedMaximum = -1;
        inventory.Changed += (_id, _reserve, _maximum) =>
        {
            events++;
            changedId = _id;
            changedReserve = _reserve;
            changedMaximum = _maximum;
        };
        Check(inventory.Add("pistol", int.MaxValue) == 24, "overflow-safe capped addition");
        Check(events == 1 && changedId == "pistol" && changedReserve == 60 && changedMaximum == 60, "event snapshot");
        Check(inventory.Add("pistol", 1) == 0 && events == 1, "full reserve is no-op");
        Check(inventory.Consume("pistol", int.MaxValue) == 60 && inventory.GetReserve("pistol") == 0, "partial consumption");
        Check(inventory.Consume("pistol", 1) == 0 && events == 2, "empty reserve is no-op");
        Check(inventory.Add("pistol", -1) == 0 && inventory.Consume("pistol", -1) == 0 && events == 2, "negative requests rejected");
        Check(inventory.Add(null, 1) == 0 && inventory.Consume("missing", 1) == 0 && !inventory.Contains(null), "unknown IDs rejected");
        Check(!inventory.SetMaximum("pistol", -1, true) && !inventory.SetMaximum("missing", 1, true), "invalid maximum rejected");
        Check(inventory.SetMaximum("pistol", 100, false) && inventory.GetMaximum("pistol") == 100 && events == 3, "maximum increase event");
        Check(inventory.Add("pistol", 80) == 80, "increased capacity usable");
        Check(!inventory.SetMaximum("pistol", 20, false) && inventory.GetReserve("pistol") == 80 && inventory.GetMaximum("pistol") == 100 && events == 4, "decrease preserves state when discard refused");
        Check(inventory.SetMaximum("pistol", 20, true) && inventory.GetReserve("pistol") == 20 && events == 5, "explicit discard on decrease");
        Check(inventory.SetMaximum("pistol", 20, true) && events == 5, "same maximum no-op");
        Check(inventory.SetMaximum("pistol", 0, true) && inventory.GetReserve("pistol") == 0, "zero capacity");
        Check(data.ammoTypes[0].maxReserve == 60 && data.ammoTypes[0].startingReserve == 36, "table not mutated");
        var restarted = new AmmoInventory(data);
        Check(restarted.GetReserve("pistol") == 36 && restarted.GetMaximum("pistol") == 60, "new run resets inventory");
        Check(restarted.GetReserve("sniper") == 30 && inventory.GetReserve("sniper") == 30, "ammo types independent");
        Check(new AmmoInventory(null).GetReserve("pistol") == 0, "missing table safe");
        var invalid = new AmmoData
        {
            ammoTypes = new[]
            {
                null,
                new AmmoData.Entry { id = "", referenceMagazineSize = 1 },
                new AmmoData.Entry { id = "bad", referenceMagazineSize = 1, maxReserve = 1, startingReserve = 2 },
                new AmmoData.Entry { id = "valid", referenceMagazineSize = 1, maxReserve = 2, startingReserve = 1 },
                new AmmoData.Entry { id = "valid", referenceMagazineSize = 1, maxReserve = 9, startingReserve = 9 }
            }
        };
        var defensive = new AmmoInventory(invalid);
        Check(!defensive.Contains("bad") && !defensive.Contains(""), "invalid entries skipped");
        Check(defensive.GetReserve("valid") == 1, "duplicate cannot overwrite stock");
        Console.WriteLine($"PASS: {CHECKS} checks; pure C# model/data only, no Unity execution.");
    }

    private static void Check(bool _condition, string _name)
    {
        if (!_condition)
        {
            throw new InvalidOperationException(_name);
        }
        CHECKS++;
    }
}
