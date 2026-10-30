using System;
using System.Collections.Generic;
using UnityEngine;

// 파츠 테이블 조회. 파츠는 무기 종류에 종속되며, 같은 파츠를 여러 개 보유할 수 있다(개체는 PartInstance).
public static class PartInfo
{
    public const string SLOT_MUZZLE = "muzzle";
    public const string SLOT_BARREL = "barrel";
    public const string SLOT_MAGAZINE = "magazine";
    public const string SLOT_STOCK = "stock";
    public const string SLOT_SCOPE = "scope";

    public static PartData.Entry Find(PartData _table, string _id)
    {
        if (_table == null || _table.parts == null || string.IsNullOrEmpty(_id))
        {
            return null;
        }
        foreach (var part in _table.parts)
        {
            if (part != null && part.id == _id)
            {
                return part;
            }
        }
        return null;
    }

    // 무기 종류에 속한 파츠. 테이블 순서를 유지한다.
    public static List<PartData.Entry> ForWeapon(PartData _table, string _weaponId)
    {
        var result = new List<PartData.Entry>();
        if (_table == null || _table.parts == null)
        {
            return result;
        }
        foreach (var part in _table.parts)
        {
            if (part != null && part.weaponId == _weaponId)
            {
                result.Add(part);
            }
        }
        return result;
    }

    public static string SlotName(string _slot)
    {
        switch (_slot)
        {
            case SLOT_MUZZLE:
                return "총구";
            case SLOT_BARREL:
                return "총열";
            case SLOT_MAGAZINE:
                return "탄창";
            case SLOT_STOCK:
                return "개머리판/그립";
            case SLOT_SCOPE:
                return "조준경";
            default:
                return _slot;
        }
    }

    public static void ValidateWeapons(PartData _table, WeaponData _weapons)
    {
        if (_table == null || _table.parts == null)
        {
            return;
        }
        foreach (var part in _table.parts)
        {
            if (part != null && WeaponInfo.Find(_weapons, part.weaponId) == null)
            {
                Debug.LogError($"파츠 {part.id}의 무기 참조가 올바르지 않습니다: {part.weaponId}");
            }
        }
    }

    public static void Validate(PartData _table)
    {
        if (_table == null || _table.parts == null)
        {
            Debug.LogError("파츠 테이블을 읽지 못했습니다.");
            return;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var slots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in _table.parts)
        {
            if (part == null)
            {
                Debug.LogError("파츠 테이블에 빈 항목이 있습니다.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(part.id) || !ids.Add(part.id))
            {
                Debug.LogError($"파츠 ID가 비어 있거나 중복됩니다: {part.id}");
            }
            if (part.slot != SLOT_MUZZLE && part.slot != SLOT_BARREL && part.slot != SLOT_MAGAZINE && part.slot != SLOT_STOCK && part.slot != SLOT_SCOPE)
            {
                Debug.LogError($"파츠 {part.id}의 장착 칸({part.slot})을 알 수 없습니다.");
            }
            // 같은 무기에 같은 칸 파츠가 둘이면 한쪽은 영원히 함께 쓸 수 없다. 의도된 선택지라면 경고만 남긴다.
            if (!slots.Add(part.weaponId + "/" + part.slot))
            {
                Debug.LogWarning($"무기 {part.weaponId}의 {part.slot} 칸 파츠가 둘 이상입니다(동시 장착 불가).");
            }
            if (part.damageMultiplier <= 0f || part.fireRateMultiplier <= 0f || part.reloadMultiplier <= 0f ||
                part.ammoSaveChance < 0f || part.ammoSaveChance >= 1f)
            {
                Debug.LogError($"파츠 {part.id}의 배율·탄 절감 값이 올바르지 않습니다.");
            }
        }
    }
}
