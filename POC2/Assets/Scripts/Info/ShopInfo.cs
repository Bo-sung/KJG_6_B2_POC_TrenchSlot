using UnityEngine;

// 정비 상점 테이블 조회. 칸 순서는 ShopData.items 배열 순서다.
public static class ShopInfo
{
    public const string TYPE_WEAPON = "weapon";
    public const string TYPE_HEAL = "heal";
    public const string TYPE_PERK = "perk";

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
        foreach (var item in _table.items)
        {
            if (item == null)
            {
                continue;
            }
            if (item.type != TYPE_WEAPON && item.type != TYPE_HEAL && item.type != TYPE_PERK)
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
