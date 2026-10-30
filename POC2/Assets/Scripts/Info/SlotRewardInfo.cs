using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 슬롯 현물 보상 계획·지급·문구(v4). 계획(Plan)은 상태를 바꾸지 않는 순수 계산이고, 지급(Apply)만 보유 상태를 바꾼다.
// 난수는 호출자가 넘기는 _roll(0 이상 1 미만)로만 받아 테스트에서 결정적으로 돌린다. 롤은 필요한 순서대로 소비한다.
// 다음 시대 파츠는 다음 시대 풀에서만 뽑는다(휴대 파츠 70% 규칙을 적용하지 않는다). 다음 시대가 없거나 파츠가 비어 있으면
// 현 시대 파츠(EraPartIds)에서 직접 뽑고, nextEraFallback(다음 시대 부재) 또는 lastEraFallback(마지막 시대)으로 표시한다.
// 현 시대 파츠도 없으면 $fullAmmoCash로 대체하고 nextEraFallbackCash로 표시한다.
public static class SlotRewardInfo
{
    // 회전 결과 한 건의 현물 계획. 탄약은 ammoId가 null이면 없고, 파츠는 보관함에 들어갈 파츠 id들이다(중복 가능).
    public sealed class Reward
    {
        public int cash;
        public string ammoId;
        public int ammoAmount;
        // 탄약상자 3줄(전량 충전)인지. 문구에만 쓴다.
        public bool ammoRefill;
        public readonly List<string> partIds = new List<string>();
        // 다음 시대 파츠 테이블이 비어 현 시대 파츠(또는 현금)로 대신 준 경우.
        public bool nextEraFallback;
        // 마지막 시대라 다음 시대가 없어 현 시대 파츠(또는 현금)로 대신 준 경우.
        public bool lastEraFallback;
        // 대체할 현 시대 파츠도 없어 현금으로 대신 준 경우.
        public bool nextEraFallbackCash;

        public bool IsEmpty => cash <= 0 && ammoId == null && partIds.Count == 0;
    }

    // 계획에 필요한 상태. wave는 회전이 시작된 웨이브(스냅샷)이며, 정비 중 지급이라도 이 값을 쓴다.
    public sealed class Context
    {
        public int wave;
        public ShopData shop;
        public WeaponData weapons;
        public PartData parts;
        public AmmoData ammoTable;
        public AmmoInventory ammo;
        // 휴대 무기(슬롯 순서)의 무기 id. 바닥에 놓인 무기는 넣지 않는다.
        public List<string> carriedWeaponIds = new List<string>();
    }

    // 회전 결과를 현물 계획으로 바꾼다.
    //  - 탄약상자 3줄: 휴대 무기 탄종 중 예비탄이 덜 찬 것, 없으면 현 시대 탄종 중 덜 찬 것 → 예비탄 전량 충전. 모두 가득이면 현금.
    //  - 꽝(isMiss): 기준 탄창 1/8(올림, 최소 1)을 줄 탄종 선택 — 롤 < heldRewardChance면 휴대 탄종, 아니면 현 시대 탄종 풀. 가득이면 현금.
    //  - 파츠: 현 시대 파츠 partCount개(휴대 70%/시대 30%), 다음 시대 파츠 nextEraPartCount개(다음 시대 풀 전용, 대체 시 현 시대 풀 전용).
    //  - 현금(배수 지급)은 여기서 다루지 않는다. 호출자가 배수 지급을 따로 한다.
    public static Reward Plan(SlotMachineData _slot, SlotOutcome _outcome, Context _ctx, Func<float> _roll)
    {
        var reward = new Reward();
        if (_slot == null || _ctx == null || _roll == null)
        {
            return reward;
        }

        string eraId = ShopInfo.EraForWave(_ctx.shop, _ctx.wave)?.id;
        if (_outcome.hasAmmo)
        {
            PlanTripleAmmo(reward, _slot, _ctx, eraId, _roll);
        }
        else if (_outcome.isMiss)
        {
            PlanMissAmmo(reward, _slot, _ctx, eraId, _roll);
        }

        for (int i = 0; i < _outcome.partCount; i++)
        {
            AddPart(reward, _slot, _ctx, eraId, _roll);
        }
        for (int i = 0; i < _outcome.nextEraPartCount; i++)
        {
            AddNextEraPart(reward, _slot, _ctx, eraId, _roll);
        }
        return reward;
    }

    // 계획을 실제 보유 상태에 반영한다. 탄약은 한도(최대 예비탄)를 넘기지 않고, 파츠는 보관함에 개별로 넣는다(장착하지 않는다).
    public static void Apply(Reward _reward, AmmoInventory _ammo, PartInventory _parts, PlayerWallet _wallet, string _causeId = null)
    {
        if (_reward == null)
        {
            return;
        }
        if (_reward.ammoId != null && _ammo != null)
        {
            _ammo.Add(_reward.ammoId, _reward.ammoAmount, "slot_reserve", _causeId);
        }
        if (_parts != null)
        {
            foreach (string partId in _reward.partIds)
            {
                _parts.Add(partId);
            }
        }
        if (_reward.cash > 0 && _wallet != null)
        {
            _wallet.Add(_reward.cash);
        }
    }

    // 화면 문구. 탄약·파츠(이름과 상점가)·대체 안내·현금을 한 줄로 잇는다. 아무것도 없으면 빈 문자열이다.
    public static string Describe(Reward _reward, ShopData _shop, PartData _parts, AmmoData _ammoTable)
    {
        if (_reward == null || _reward.IsEmpty)
        {
            return "";
        }
        var text = new StringBuilder();
        if (_reward.ammoId != null)
        {
            var entry = AmmoInfo.Find(_ammoTable, _reward.ammoId);
            string name = entry != null ? entry.name : _reward.ammoId;
            text.Append(_reward.ammoRefill ? $"탄약 {name} 전량 충전" : $"탄약 {name} +{_reward.ammoAmount}발");
        }
        foreach (string partId in _reward.partIds)
        {
            var part = PartInfo.Find(_parts, partId);
            if (text.Length > 0)
            {
                text.Append("  ");
            }
            text.Append(part != null ? $"파츠 {part.name} (${ShopInfo.PartPrice(_shop, part):N0} 상당)" : $"파츠 {partId}");
        }
        if (_reward.lastEraFallback)
        {
            if (text.Length > 0)
            {
                text.Append("  ");
            }
            text.Append(_reward.nextEraFallbackCash
                ? "(마지막 시대: 현금으로 대체)"
                : "(마지막 시대: 현 시대 파츠로 대체)");
        }
        else if (_reward.nextEraFallback)
        {
            if (text.Length > 0)
            {
                text.Append("  ");
            }
            text.Append(_reward.nextEraFallbackCash
                ? "(다음 시대 파츠 없음: 현금으로 대체)"
                : "(다음 시대 파츠 없음: 현 시대 파츠로 대체)");
        }
        if (_reward.cash > 0)
        {
            if (text.Length > 0)
            {
                text.Append("  ");
            }
            text.Append($"+${_reward.cash:N0}");
        }
        return text.ToString();
    }

    private static void PlanTripleAmmo(Reward _reward, SlotMachineData _slot, Context _ctx, string _eraId, Func<float> _roll)
    {
        var notFullCarried = new List<string>();
        foreach (string id in CarriedAmmo(_ctx))
        {
            if (CanRefill(_ctx.ammo, id))
            {
                notFullCarried.Add(id);
            }
        }
        List<string> pool;
        if (notFullCarried.Count > 0)
        {
            pool = notFullCarried;
        }
        else
        {
            var notFullEra = new List<string>();
            foreach (string id in EraAmmo(_ctx, _eraId))
            {
                if (CanRefill(_ctx.ammo, id))
                {
                    notFullEra.Add(id);
                }
            }
            pool = notFullEra;
        }
        string picked = Pick(pool, _roll());
        if (picked == null || !CanRefill(_ctx.ammo, picked))
        {
            _reward.cash = _slot.fullAmmoCash;
            return;
        }
        _reward.ammoId = picked;
        _reward.ammoAmount = _ctx.ammo.GetMaximum(picked) - _ctx.ammo.GetReserve(picked);
        _reward.ammoRefill = true;
    }

    private static void PlanMissAmmo(Reward _reward, SlotMachineData _slot, Context _ctx, string _eraId, Func<float> _roll)
    {
        var pool = ChoosePool(CarriedAmmo(_ctx), EraAmmo(_ctx, _eraId), _roll(), _slot.heldRewardChance);
        string picked = Pick(pool, _roll());
        var entry = picked != null ? AmmoInfo.Find(_ctx.ammoTable, picked) : null;
        if (entry == null || _ctx.ammo == null || !_ctx.ammo.Contains(picked) || !CanRefill(_ctx.ammo, picked))
        {
            _reward.cash = _slot.fullAmmoCash;
            return;
        }
        int minSize = entry.referenceMagazineSize;
        int size = Mathf.Max(minSize, Mathf.CeilToInt(entry.referenceMagazineSize * _slot.missAmmoFraction));
        _reward.ammoId = picked;
        _reward.ammoAmount = Mathf.Min(size, _ctx.ammo.GetMaximum(picked) - _ctx.ammo.GetReserve(picked));
    }

    private static void AddPart(Reward _reward, SlotMachineData _slot, Context _ctx, string _eraId, Func<float> _roll)
    {
        var pool = ChoosePool(CarriedPartIds(_ctx), EraPartIds(_ctx, _eraId), _roll(), _slot.heldRewardChance);
        string picked = Pick(pool, _roll());
        if (picked != null)
        {
            _reward.partIds.Add(picked);
        }
        else
        {
            // 파츠를 낼 풀이 전혀 없다(테이블 누락 등): 부품 자리는 현금 대체로 채운다.
            _reward.cash += _slot.fullAmmoCash;
        }
    }

    private static void AddNextEraPart(Reward _reward, SlotMachineData _slot, Context _ctx, string _eraId, Func<float> _roll)
    {
        string nextEraId = ShopInfo.NextEra(_ctx.shop, _eraId)?.id;
        var nextEraPool = nextEraId != null ? EraPartIds(_ctx, nextEraId) : null;
        if (nextEraPool != null && nextEraPool.Count > 0)
        {
            string picked = Pick(nextEraPool, _roll());
            if (picked != null)
            {
                _reward.partIds.Add(picked);
                return;
            }
        }

        bool isLastEra = string.IsNullOrEmpty(nextEraId);
        if (isLastEra)
        {
            _reward.lastEraFallback = true;
        }
        else
        {
            _reward.nextEraFallback = true;
        }

        var currentEraPool = EraPartIds(_ctx, _eraId);
        if (currentEraPool != null && currentEraPool.Count > 0)
        {
            string picked = Pick(currentEraPool, _roll());
            if (picked != null)
            {
                _reward.partIds.Add(picked);
                return;
            }
        }

        _reward.nextEraFallbackCash = true;
        _reward.cash += _slot.fullAmmoCash;
    }

    // 주 풀과 보조 풀 중 하나를 고른다. 한쪽이 비면 다른 쪽을 쓰고, 둘 다 비면 빈 목록이다.
    private static List<string> ChoosePool(List<string> _primary, List<string> _secondary, float _roll, float _primaryChance)
    {
        if (_primary.Count == 0)
        {
            return _secondary;
        }
        if (_secondary.Count == 0)
        {
            return _primary;
        }
        return _roll < _primaryChance ? _primary : _secondary;
    }

    private static string Pick(List<string> _pool, float _roll)
    {
        if (_pool == null || _pool.Count == 0)
        {
            return null;
        }
        int index = Mathf.Clamp((int)(Mathf.Clamp01(_roll) * _pool.Count), 0, _pool.Count - 1);
        return _pool[index];
    }

    private static bool CanRefill(AmmoInventory _ammo, string _id)
    {
        return _ammo != null && _id != null && _ammo.Contains(_id) && _ammo.GetReserve(_id) < _ammo.GetMaximum(_id);
    }

    // 휴대 무기의 탄종(중복 제거, 슬롯 순서).
    private static List<string> CarriedAmmo(Context _ctx)
    {
        var result = new List<string>();
        foreach (string weaponId in _ctx.carriedWeaponIds)
        {
            var weapon = WeaponInfo.Find(_ctx.weapons, weaponId);
            if (weapon != null && !string.IsNullOrEmpty(weapon.ammoId) && !result.Contains(weapon.ammoId))
            {
                result.Add(weapon.ammoId);
            }
        }
        return result;
    }

    // 지금까지 해금된 무기(시대 누적)의 탄종.
    private static List<string> UnlockedAmmo(Context _ctx)
    {
        var result = new List<string>();
        foreach (var weapon in WeaponEntries(_ctx))
        {
            if (weapon != null && ShopInfo.IsWeaponUnlocked(_ctx.shop, weapon, _ctx.wave) && !result.Contains(weapon.ammoId))
            {
                result.Add(weapon.ammoId);
            }
        }
        return result;
    }

    private static List<string> EraAmmo(Context _ctx, string _eraId)
    {
        var result = new List<string>();
        foreach (var weapon in WeaponEntries(_ctx))
        {
            if (weapon != null && weapon.era == _eraId && !result.Contains(weapon.ammoId))
            {
                result.Add(weapon.ammoId);
            }
        }
        return result;
    }

    private static IEnumerable<WeaponData.Entry> WeaponEntries(Context _ctx)
    {
        return _ctx.weapons != null && _ctx.weapons.weapons != null ? _ctx.weapons.weapons : Array.Empty<WeaponData.Entry>();
    }

    private static List<string> CarriedPartIds(Context _ctx)
    {
        var result = new List<string>();
        if (_ctx.parts == null)
        {
            return result;
        }
        foreach (var part in _ctx.parts.parts)
        {
            if (part != null && _ctx.carriedWeaponIds.Contains(part.weaponId))
            {
                result.Add(part.id);
            }
        }
        return result;
    }

    private static List<string> EraPartIds(Context _ctx, string _eraId)
    {
        var result = new List<string>();
        if (_ctx.parts == null || _eraId == null)
        {
            return result;
        }
        foreach (var part in _ctx.parts.parts)
        {
            var weapon = part != null ? WeaponInfo.Find(_ctx.weapons, part.weaponId) : null;
            if (weapon != null && weapon.era == _eraId)
            {
                result.Add(part.id);
            }
        }
        return result;
    }

    private static int CountPartsOf(Context _ctx, string _eraId)
    {
        return EraPartIds(_ctx, _eraId).Count;
    }
}
