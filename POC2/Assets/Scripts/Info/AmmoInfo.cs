using System;
using System.Collections.Generic;
using UnityEngine;

public static class AmmoInfo
{
    public static AmmoData.Entry Find(AmmoData _table, string _id)
    {
        if (_table == null || _table.ammoTypes == null || string.IsNullOrWhiteSpace(_id))
        {
            return null;
        }
        foreach (var entry in _table.ammoTypes)
        {
            if (entry != null && entry.id == _id)
            {
                return entry;
            }
        }
        return null;
    }

    public static void Validate(AmmoData _table)
    {
        if (_table == null || _table.ammoTypes == null || _table.ammoTypes.Length == 0)
        {
            Debug.LogError("탄약 테이블이 비어 있습니다.");
            return;
        }
        var pickup = _table.pickup;
        if (pickup == null || !IsFinite(pickup.dropChance) || pickup.dropChance < 0f || pickup.dropChance > 1f ||
            !IsFinite(pickup.pickupDistance) || pickup.pickupDistance <= 0f ||
            !IsFinite(pickup.magazineFraction) || pickup.magazineFraction <= 0f || pickup.magazineFraction > 1f)
        {
            Debug.LogError("탄약 드롭 확률·획득 거리·탄창 비율이 올바르지 않습니다.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _table.ammoTypes)
        {
            if (entry == null)
            {
                Debug.LogError("탄약 테이블에 빈 항목이 있습니다.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(entry.id) || !ids.Add(entry.id))
            {
                Debug.LogError($"탄약 ID가 비어 있거나 중복됩니다: {entry.id}");
            }
            if (!IsHexColor(entry.color))
            {
                Debug.LogError($"탄약 {entry.id}의 색상은 #RRGGBB 또는 #RRGGBBAA여야 합니다.");
            }
            if (entry.referenceMagazineSize <= 0 || entry.maxReserve < 0 || entry.startingReserve < 0 || entry.startingReserve > entry.maxReserve)
            {
                Debug.LogError($"탄약 {entry.id}의 기준 탄창·최대량·시작량이 올바르지 않습니다.");
            }
        }
    }

    public static int DropAmount(AmmoData.Entry _entry, AmmoData.PickupSettings _pickup)
    {
        if (_entry == null || _entry.referenceMagazineSize <= 0 || _pickup == null ||
            !IsFinite(_pickup.magazineFraction) || _pickup.magazineFraction <= 0f || _pickup.magazineFraction > 1f)
        {
            return 0;
        }
        return (int)Math.Ceiling((double)_entry.referenceMagazineSize * _pickup.magazineFraction);
    }

    public static bool ShouldDrop(AmmoData.PickupSettings _pickup, float _roll)
    {
        return _pickup != null && IsFinite(_pickup.dropChance) && _pickup.dropChance > 0f && _pickup.dropChance <= 1f &&
            IsFinite(_roll) && _roll >= 0f && _roll <= 1f && (_pickup.dropChance >= 1f || _roll < _pickup.dropChance);
    }

    private static bool IsFinite(float _value)
    {
        return !float.IsNaN(_value) && !float.IsInfinity(_value);
    }

    private static bool IsHexColor(string _color)
    {
        if (string.IsNullOrEmpty(_color) || (_color.Length != 7 && _color.Length != 9) || _color[0] != '#')
        {
            return false;
        }
        for (int i = 1; i < _color.Length; i++)
        {
            char digit = _color[i];
            if (!((digit >= '0' && digit <= '9') || (digit >= 'a' && digit <= 'f') || (digit >= 'A' && digit <= 'F')))
            {
                return false;
            }
        }
        return true;
    }
}
