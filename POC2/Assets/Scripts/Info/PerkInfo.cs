using UnityEngine;

// 퍽 테이블 조회.
public static class PerkInfo
{
    public static PerkData.Entry Find(PerkData _table, string _id)
    {
        if (_table == null || _table.perks == null)
        {
            return null;
        }
        foreach (var perk in _table.perks)
        {
            if (perk != null && perk.id == _id)
            {
                return perk;
            }
        }
        return null;
    }

    public static void Validate(PerkData _table)
    {
        if (_table == null || _table.perks == null || _table.perks.Length == 0)
        {
            Debug.LogError("퍽 테이블이 비어 있습니다.");
            return;
        }
        foreach (var perk in _table.perks)
        {
            if (perk != null && perk.maxStack <= 0)
            {
                Debug.LogError($"퍽 {perk.id}의 maxStack은 1 이상이어야 합니다.");
            }
        }
    }
}
