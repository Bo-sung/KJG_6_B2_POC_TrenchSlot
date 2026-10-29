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
