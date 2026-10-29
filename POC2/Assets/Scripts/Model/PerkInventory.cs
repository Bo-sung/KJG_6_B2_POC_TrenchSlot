using System;
using System.Collections.Generic;
using UnityEngine;

// 보유 퍽과 스택 수. 효과 계산은 퍽 테이블의 value를 해석해 여기서 한다.
// value 의미: discountLever=스택당 비용 감소율, killStack=처치당 지급 보너스율, fastSpindle=회전 시간 감소율,
//             ammoSymbol=탄약 심볼 가중치 배수(보유 시 적용).
public sealed class PerkInventory
{
    public const string DISCOUNT_LEVER = "discountLever";
    public const string KILL_STACK = "killStack";
    public const string FAST_SPINDLE = "fastSpindle";
    public const string AMMO_SYMBOL = "ammoSymbol";

    private readonly PerkData table;
    private readonly Dictionary<string, int> stacks = new Dictionary<string, int>();

    public event Action Changed;

    public PerkInventory(PerkData _table)
    {
        table = _table;
    }

    public int StackOf(string _id)
    {
        return stacks.TryGetValue(_id, out int count) ? count : 0;
    }

    public bool CanBuy(string _id)
    {
        var entry = PerkInfo.Find(table, _id);
        return entry != null && StackOf(_id) < entry.maxStack;
    }

    public void Buy(string _id)
    {
        if (!CanBuy(_id))
        {
            return;
        }
        stacks[_id] = StackOf(_id) + 1;
        Changed?.Invoke();
    }

    // 회전 비용 = 기본 비용 x (1 - 스택당 감소율 x 스택). 0 이하로는 내려가지 않는다.
    public int ApplySpinCost(int _baseCost)
    {
        float reduction = Mathf.Clamp01(EffectValue(DISCOUNT_LEVER) * StackOf(DISCOUNT_LEVER));
        return Mathf.Max(0, Mathf.RoundToInt(_baseCost * (1f - reduction)));
    }

    public float ApplySpinSeconds(float _baseSeconds)
    {
        float reduction = Mathf.Clamp01(EffectValue(FAST_SPINDLE) * StackOf(FAST_SPINDLE));
        return _baseSeconds * (1f - reduction);
    }

    // 웨이브 중 처치 한 번당 지급 보너스 비율(예: 0.02 = +2%).
    public float KillBonusPerKill => EffectValue(KILL_STACK) * StackOf(KILL_STACK);

    public int AmmoWeightScale => StackOf(AMMO_SYMBOL) > 0 ? Mathf.RoundToInt(EffectValue(AMMO_SYMBOL)) : 1;

    private float EffectValue(string _id)
    {
        var entry = PerkInfo.Find(table, _id);
        return entry != null ? entry.value : 0f;
    }
}
