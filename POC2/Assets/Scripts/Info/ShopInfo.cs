using System;
using System.Collections.Generic;
using UnityEngine;

// 상점 테이블 조회와 가격 계산. 진열 순서는 각 배열 순서를 따른다.
public static class ShopInfo
{
    public static ShopData.WeaponOffer FindWeapon(ShopData _table, string _weaponId)
    {
        if (_table == null || _table.weapons == null || string.IsNullOrEmpty(_weaponId))
        {
            return null;
        }
        foreach (var offer in _table.weapons)
        {
            if (offer != null && offer.weaponId == _weaponId)
            {
                return offer;
            }
        }
        return null;
    }

    public static ShopData.Era FindEra(ShopData _table, string _eraId)
    {
        if (_table == null || _table.eras == null)
        {
            return null;
        }
        foreach (var era in _table.eras)
        {
            if (era != null && era.id == _eraId)
            {
                return era;
            }
        }
        return null;
    }

    // 해당 웨이브의 시대. startWave가 웨이브 이하인 시대 중 가장 늦은 것이며, 마지막 시대 이후는 마지막 시대를 유지한다(무한 모드).
    public static ShopData.Era EraForWave(ShopData _table, int _wave)
    {
        ShopData.Era result = null;
        if (_table == null || _table.eras == null)
        {
            return null;
        }
        foreach (var era in _table.eras)
        {
            if (era != null && era.startWave <= _wave && (result == null || era.startWave >= result.startWave))
            {
                result = era;
            }
        }
        return result;
    }

    // 무기 판매 해금 여부. 시대 진열은 누적이라 한 번 열린 시대 무기는 계속 판다.
    public static bool IsWeaponUnlocked(ShopData _table, WeaponData.Entry _weapon, int _wave)
    {
        if (_weapon == null)
        {
            return false;
        }
        var era = FindEra(_table, _weapon.era);
        return era != null && era.startWave <= _wave;
    }

    public static int WeaponPrice(ShopData _table, string _weaponId)
    {
        var offer = FindWeapon(_table, _weaponId);
        return offer != null ? offer.price : 0;
    }

    // 파츠 가격 = 소속 무기 가격 × 비율, 반올림 단위로 맞춘다.
    public static int PartPrice(ShopData _table, PartData.Entry _part)
    {
        if (_table == null || _part == null)
        {
            return 0;
        }
        return RoundPrice(_table, WeaponPrice(_table, _part.weaponId) * _table.partPriceRatio);
    }

    public static ShopData.Stat FindStat(ShopData _table, string _id)
    {
        if (_table == null || _table.stats == null)
        {
            return null;
        }
        foreach (var stat in _table.stats)
        {
            if (stat != null && stat.id == _id)
            {
                return stat;
            }
        }
        return null;
    }

    // 스탯 가격 = 기준가 × 성장률^(이미 산 횟수). 상한 없이 쌓이며 가격만 오른다.
    public static int StatPrice(ShopData _table, ShopData.Stat _stat, int _boughtCount)
    {
        if (_table == null || _stat == null)
        {
            return 0;
        }
        double raw = _stat.basePrice * Math.Pow(_table.statPriceGrowth, Math.Max(0, _boughtCount));
        return RoundPrice(_table, (float)Math.Min(raw, int.MaxValue / 2));
    }

    public static ShopData.AmmoPack FindAmmoPack(ShopData _table, string _ammoId)
    {
        if (_table == null || _table.ammoPacks == null)
        {
            return null;
        }
        foreach (var pack in _table.ammoPacks)
        {
            if (pack != null && pack.ammoId == _ammoId)
            {
                return pack;
            }
        }
        return null;
    }

    // 탄약 팩 가격 = 기준 무기 가격 × 비율. 탄약은 시대가 올라가도 남는 돈의 상수 사용처다.
    public static int AmmoPackPrice(ShopData _table, ShopData.AmmoPack _pack)
    {
        if (_table == null || _pack == null || _table.ammoPack == null)
        {
            return 0;
        }
        return Mathf.Max(1, Mathf.RoundToInt(WeaponPrice(_table, _pack.referenceWeaponId) * _table.ammoPack.priceRatio));
    }

    public static int AmmoPackRounds(ShopData _table)
    {
        return _table != null && _table.ammoPack != null ? Mathf.Max(0, _table.ammoPack.rounds) : 0;
    }

    // 탄약 팩 해금 시점 = 그 탄종을 쓰는 무기 중 가장 먼저 해금되는 무기의 시대.
    public static bool IsAmmoUnlocked(ShopData _table, WeaponData _weapons, string _ammoId, int _wave)
    {
        if (_weapons == null || _weapons.weapons == null)
        {
            return false;
        }
        foreach (var weapon in _weapons.weapons)
        {
            if (weapon != null && weapon.ammoId == _ammoId && IsWeaponUnlocked(_table, weapon, _wave))
            {
                return true;
            }
        }
        return false;
    }

    public static ShopData.SlotUpgrade FindSlotUpgrade(ShopData _table, string _perkId)
    {
        if (_table == null || _table.slotUpgrades == null || _table.slotUpgrades.pool == null)
        {
            return null;
        }
        foreach (var upgrade in _table.slotUpgrades.pool)
        {
            if (upgrade != null && upgrade.perkId == _perkId)
            {
                return upgrade;
            }
        }
        return null;
    }

    public static int SlotOfferCount(ShopData _table)
    {
        return _table != null && _table.slotUpgrades != null ? Mathf.Max(0, _table.slotUpgrades.offerCount) : 0;
    }

    public static int HealPrice(ShopData _table)
    {
        return _table != null && _table.heal != null ? Mathf.Max(0, _table.heal.price) : 0;
    }

    // 체력 회복을 살 수 있는지. 체력이 이미 최대면 회복할 것이 없으므로 살 수 없다.
    public static bool CanHeal(float _current, float _max)
    {
        return _max > 0f && _current < _max;
    }

    public static bool CanAddAmmo(AmmoInventory _ammo, string _id)
    {
        return _ammo != null && _ammo.Contains(_id) && _ammo.GetReserve(_id) < _ammo.GetMaximum(_id);
    }

    public static int RoundPrice(ShopData _table, float _price)
    {
        int unit = _table != null && _table.priceRounding > 0 ? _table.priceRounding : 1;
        return Mathf.Max(0, Mathf.RoundToInt(_price / unit) * unit);
    }

    public static void ValidateReferences(ShopData _table, WeaponData _weapons, AmmoData _ammo, PerkData _perks)
    {
        if (_table == null)
        {
            return;
        }
        foreach (var offer in _table.weapons)
        {
            var weapon = offer != null ? WeaponInfo.Find(_weapons, offer.weaponId) : null;
            if (weapon == null)
            {
                Debug.LogError($"상점 무기 진열의 무기 참조가 올바르지 않습니다: {offer?.weaponId}");
            }
            else if (FindEra(_table, weapon.era) == null)
            {
                Debug.LogError($"무기 {weapon.id}의 시대({weapon.era})가 상점 시대 목록에 없습니다.");
            }
        }
        if (_weapons != null && _weapons.weapons != null)
        {
            foreach (var weapon in _weapons.weapons)
            {
                if (weapon != null && FindWeapon(_table, weapon.id) == null)
                {
                    Debug.LogWarning($"무기 {weapon.id}는 상점 진열에 없습니다(구매 불가).");
                }
            }
        }
        foreach (var pack in _table.ammoPacks)
        {
            if (pack == null || AmmoInfo.Find(_ammo, pack.ammoId) == null || FindWeapon(_table, pack.referenceWeaponId) == null)
            {
                Debug.LogError($"상점 탄약 팩의 탄종·기준 무기 참조가 올바르지 않습니다: {pack?.ammoId}/{pack?.referenceWeaponId}");
            }
        }
        if (_table.slotUpgrades != null && _table.slotUpgrades.pool != null)
        {
            foreach (var upgrade in _table.slotUpgrades.pool)
            {
                if (upgrade == null || PerkInfo.Find(_perks, upgrade.perkId) == null)
                {
                    Debug.LogError($"슬롯 업그레이드의 퍽 참조가 올바르지 않습니다: {upgrade?.perkId}");
                }
            }
        }
    }

    public static void Validate(ShopData _table)
    {
        if (_table == null || _table.eras == null || _table.eras.Length == 0 || _table.weapons == null || _table.weapons.Length == 0)
        {
            Debug.LogError("상점 테이블의 시대·무기 진열이 비어 있습니다.");
            return;
        }
        if (_table.priceRounding <= 0 || _table.partPriceRatio < 0f || _table.statPriceGrowth < 1f)
        {
            Debug.LogError("상점 가격 반올림 단위·파츠 비율·스탯 가격 성장률이 올바르지 않습니다.");
        }
        if (_table.ammoPack == null || _table.ammoPack.rounds <= 0 || _table.ammoPack.priceRatio <= 0f)
        {
            Debug.LogError("탄약 팩 발수·가격 비율이 올바르지 않습니다.");
        }
        if (_table.heal == null || _table.heal.price < 0)
        {
            Debug.LogError("체력 회복 가격이 올바르지 않습니다.");
        }
        if (_table.slotUpgrades == null || _table.slotUpgrades.offerCount < 0)
        {
            Debug.LogError("슬롯 업그레이드 진열 칸 수가 올바르지 않습니다.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var era in _table.eras)
        {
            if (era == null || string.IsNullOrWhiteSpace(era.id) || !ids.Add(era.id) || era.startWave < 1)
            {
                Debug.LogError($"상점 시대 항목이 비어 있거나 중복·시작 웨이브 오류입니다: {era?.id}");
            }
        }
        ids.Clear();
        foreach (var offer in _table.weapons)
        {
            if (offer == null || string.IsNullOrWhiteSpace(offer.weaponId) || !ids.Add(offer.weaponId) || offer.price < 0)
            {
                Debug.LogError($"상점 무기 진열 항목이 비어 있거나 중복·음수 가격입니다: {offer?.weaponId}");
            }
        }
        ids.Clear();
        foreach (var stat in _table.stats)
        {
            if (stat == null || string.IsNullOrWhiteSpace(stat.id) || !ids.Add(stat.id) || stat.basePrice < 0)
            {
                Debug.LogError($"상점 스탯 항목이 비어 있거나 중복·음수 가격입니다: {stat?.id}");
            }
        }
        if (_table.slotUpgrades != null && _table.slotUpgrades.pool != null)
        {
            foreach (var upgrade in _table.slotUpgrades.pool)
            {
                if (upgrade == null || upgrade.price < 0)
                {
                    Debug.LogError($"슬롯 업그레이드 가격이 올바르지 않습니다: {upgrade?.perkId}");
                }
            }
        }
    }
}
