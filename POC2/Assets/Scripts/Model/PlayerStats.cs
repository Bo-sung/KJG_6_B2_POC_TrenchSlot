using System;
using System.Collections.Generic;
using UnityEngine;

// 상점 스탯 구매 횟수와 효과. 상한 없이 쌓이며 가격만 오른다(ShopInfo.StatPrice).
// value 의미: maxHealth=회당 최대 체력 증가량, armor=회당 받는 피해 감소율(곱연산), moveSpeed=회당 이동속도 증가율(합연산),
//             reloadSpeed=회당 재장전 시간 감소율(곱연산), ammoCapacity=회당 탄종별 최대 예비탄 증가량.
public sealed class PlayerStats
{
    public const string MAX_HEALTH = "maxHealth";
    public const string ARMOR = "armor";
    public const string MOVE_SPEED = "moveSpeed";
    public const string RELOAD_SPEED = "reloadSpeed";
    public const string AMMO_CAPACITY = "ammoCapacity";

    private readonly ShopData table;
    private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

    // 구매된 스탯 id.
    public event Action<string> Changed;

    public PlayerStats(ShopData _table)
    {
        table = _table;
    }

    public int CountOf(string _id)
    {
        return _id != null && counts.TryGetValue(_id, out int count) ? count : 0;
    }

    public bool Buy(string _id)
    {
        if (ShopInfo.FindStat(table, _id) == null)
        {
            return false;
        }
        counts[_id] = CountOf(_id) + 1;
        Changed?.Invoke(_id);
        return true;
    }

    public float MaxHealthBonus => Value(MAX_HEALTH) * CountOf(MAX_HEALTH);

    public float DamageTakenMultiplier => Mathf.Pow(1f - Mathf.Clamp01(Value(ARMOR)), CountOf(ARMOR));

    public float MoveSpeedMultiplier => 1f + Value(MOVE_SPEED) * CountOf(MOVE_SPEED);

    public float ReloadMultiplier => Mathf.Pow(1f - Mathf.Clamp01(Value(RELOAD_SPEED)), CountOf(RELOAD_SPEED));

    public int AmmoCapacityBonus => Mathf.RoundToInt(Value(AMMO_CAPACITY) * CountOf(AMMO_CAPACITY));

    private float Value(string _id)
    {
        var stat = ShopInfo.FindStat(table, _id);
        return stat != null ? stat.value : 0f;
    }
}
