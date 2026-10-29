using UnityEngine;

// 정비 상점 테이블 조회. 칸 순서는 ShopData.items 배열 순서다.
public static class ShopInfo
{
    public const string TYPE_WEAPON = "weapon";
    public const string TYPE_HEAL = "heal";
    public const string TYPE_PERK = "perk";
    public const string TYPE_AMMO = "ammo";

    public static ShopData.Item Find(ShopData _table, string _id)
    {
        if (_table == null || _table.items == null || string.IsNullOrWhiteSpace(_id))
        {
            return null;
        }
        foreach (var item in _table.items)
        {
            if (item != null && item.id == _id)
            {
                return item;
            }
        }
        return null;
    }

    public static bool CanRefill(AmmoInventory _ammo, string _id)
    {
        return _ammo != null && _ammo.Contains(_id) && _ammo.GetReserve(_id) < _ammo.GetMaximum(_id);
    }

    public static void ValidateAmmo(ShopData _table, AmmoData _ammo)
    {
        if (_table == null || _table.items == null)
        {
            return;
        }
        foreach (var item in _table.items)
        {
            if (item != null && item.type == TYPE_AMMO && AmmoInfo.Find(_ammo, item.target) == null)
            {
                Debug.LogError($"상점 항목 {item.id}의 탄종 참조가 올바르지 않습니다: {item.target}");
            }
        }
    }

    // 체력 회복 칸을 살 수 있는지. 체력이 이미 최대면 회복할 것이 없으므로 살 수 없다.
    public static bool CanHeal(float _current, float _max)
    {
        return _max > 0f && _current < _max;
    }

    public static void Validate(ShopData _table)
    {
        if (_table == null || _table.items == null || _table.items.Length == 0)
        {
            Debug.LogError("상점 테이블이 비어 있습니다.");
            return;
        }
        var ids = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var item in _table.items)
        {
            if (item == null)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id))
            {
                Debug.LogError($"상점 항목 ID가 비어 있거나 중복됩니다: {item.id}");
            }
            if (item.type != TYPE_WEAPON && item.type != TYPE_HEAL && item.type != TYPE_PERK && item.type != TYPE_AMMO)
            {
                Debug.LogError($"상점 항목 {item.id}의 type({item.type})을 알 수 없습니다.");
            }
            if (item.price < 0)
            {
                Debug.LogError($"상점 항목 {item.id}의 가격이 음수입니다.");
            }
        }
    }
}
