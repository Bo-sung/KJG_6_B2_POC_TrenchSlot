using System;
using System.Collections.Generic;
using UnityEngine;

// 무기 테이블 조회와 개별 무기 최종 스탯 계산(무기 기본값 × 장착 파츠 × 플레이어 스탯).
public static class WeaponInfo
{
    public static WeaponData.Entry Find(WeaponData _table, string _id)
    {
        if (_table == null || _table.weapons == null || string.IsNullOrEmpty(_id))
        {
            return null;
        }
        foreach (var weapon in _table.weapons)
        {
            if (weapon != null && weapon.id == _id)
            {
                return weapon;
            }
        }
        return null;
    }

    // 시작 무기 id. startOwned 항목 중 첫 번째이며 없으면 빈 문자열.
    public static string StartWeaponId(WeaponData _table)
    {
        if (_table == null || _table.weapons == null)
        {
            return "";
        }
        foreach (var weapon in _table.weapons)
        {
            if (weapon != null && weapon.startOwned)
            {
                return weapon.id;
            }
        }
        return "";
    }

    // 떨어뜨린 무기 자리 규칙. 테이블이 없으면 null이며, 호출 측은 자리를 찾지 못한 것으로 처리한다.
    public static WeaponData.DropSettings Drop(WeaponData _table)
    {
        return _table != null ? _table.drop : null;
    }

    public static string DisplayName(WeaponData _table, string _id)
    {
        var entry = Find(_table, _id);
        return entry != null && !string.IsNullOrEmpty(entry.name) ? entry.name : _id;
    }

    // 개별 무기의 최종 스탯. 파츠 배율은 곱하고 탄창 보너스는 더한다. _reloadMultiplier는 플레이어 장전 속도 스탯이다.
    public static PlayerWeapon.WeaponSettings Resolve(WeaponData.Entry _entry, IEnumerable<PartData.Entry> _parts, float _reloadMultiplier)
    {
        var settings = new PlayerWeapon.WeaponSettings();
        if (_entry == null)
        {
            return settings;
        }
        float damage = 1f;
        float fireRate = 1f;
        float reload = 1f;
        int magazineBonus = 0;
        float keepChance = 1f;
        if (_parts != null)
        {
            foreach (var part in _parts)
            {
                if (part == null || part.weaponId != _entry.id)
                {
                    continue;
                }
                damage *= part.damageMultiplier;
                fireRate *= part.fireRateMultiplier;
                reload *= part.reloadMultiplier;
                magazineBonus += part.magazineBonus;
                keepChance *= 1f - Mathf.Clamp01(part.ammoSaveChance);
            }
        }
        settings.magazineSize = Mathf.Max(1, _entry.magazineSize + magazineBonus);
        settings.damage = _entry.damage * damage;
        settings.range = _entry.range;
        settings.shotsPerSecond = _entry.shotsPerSecond * fireRate;
        settings.reloadSeconds = _entry.reloadSeconds * reload * Mathf.Max(0f, _reloadMultiplier);
        settings.reloadPerRound = _entry.reloadPerRound;
        settings.automatic = _entry.automatic;
        settings.pellets = Mathf.Max(1, _entry.pellets);
        settings.spreadDegrees = Mathf.Max(0f, _entry.spreadDegrees);
        settings.stoppingPower = Mathf.Max(0f, _entry.stoppingPower);
        settings.pelletStoppingPowerCap = Mathf.Max(0f, _entry.pelletStoppingPowerCap);
        settings.knockback = Mathf.Max(0f, _entry.knockback);
        settings.ammoSaveChance = 1f - keepChance;
        settings.projectileSpeed = _entry.projectileSpeed;
        settings.projectileLifetime = _entry.projectileLifetime;
        settings.projectileRadius = _entry.projectileRadius;
        return settings;
    }

    // 들고 있을 때 이동속도 배율. 무기가 없으면 1이다.
    public static float MoveSpeedMultiplier(WeaponData.Entry _entry)
    {
        return _entry != null && _entry.moveSpeedMultiplier > 0f ? _entry.moveSpeedMultiplier : 1f;
    }

    // 아킴보 허용 무기인지(권총류).
    public static bool CanAkimbo(WeaponData.Entry _entry)
    {
        return _entry != null && _entry.akimboAllowed;
    }

    // 아킴보 재장전 시간 배율. 테이블 값이 없거나 올바르지 않으면 1(배율 없음)이다.
    public static float AkimboReloadMultiplier(WeaponData _table)
    {
        return _table != null && _table.akimbo != null && _table.akimbo.reloadMultiplier > 0f ? _table.akimbo.reloadMultiplier : 1f;
    }

    public static void ValidateAmmo(WeaponData _table, AmmoData _ammo)
    {
        if (_table == null || _table.weapons == null)
        {
            return;
        }
        foreach (var weapon in _table.weapons)
        {
            if (weapon != null && AmmoInfo.Find(_ammo, weapon.ammoId) == null)
            {
                Debug.LogError($"무기 {weapon.id}의 탄종 참조가 올바르지 않습니다: {weapon.ammoId}");
            }
        }
    }

    public static void Validate(WeaponData _table)
    {
        if (_table == null || _table.weapons == null || _table.weapons.Length == 0)
        {
            Debug.LogError("무기 테이블이 비어 있습니다.");
            return;
        }
        if (StartWeaponId(_table) == "")
        {
            Debug.LogError("시작 무기(startOwned)가 없습니다.");
        }
        var drop = _table.drop;
        if (drop == null || drop.minDistance <= 0f || drop.maxDistance < drop.minDistance || drop.clearanceRadius <= 0f || drop.clearanceHeight <= 0f)
        {
            Debug.LogError("무기 drop 자리 규칙(minDistance·maxDistance·clearanceRadius·clearanceHeight)이 올바르지 않습니다.");
        }
        if (_table.akimbo == null || _table.akimbo.reloadMultiplier <= 0f)
        {
            Debug.LogError("무기 akimbo 규칙(reloadMultiplier)이 올바르지 않습니다.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var weapon in _table.weapons)
        {
            if (weapon == null)
            {
                Debug.LogError("무기 테이블에 빈 항목이 있습니다.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(weapon.id) || !ids.Add(weapon.id))
            {
                Debug.LogError($"무기 ID가 비어 있거나 중복됩니다: {weapon.id}");
            }
            if (string.IsNullOrWhiteSpace(weapon.modelId))
            {
                Debug.LogError($"무기 {weapon.id}의 외형 id(modelId)가 비어 있습니다.");
            }
            if (weapon.magazineSize <= 0 || weapon.shotsPerSecond <= 0f || weapon.range <= 0f || weapon.reloadSeconds < 0f)
            {
                Debug.LogError($"무기 {weapon.id}의 탄창·연사력·사거리·재장전 값이 올바르지 않습니다.");
            }
            if (weapon.pellets <= 0 || weapon.spreadDegrees < 0f || weapon.stoppingPower < 0f || weapon.pelletStoppingPowerCap < 0f ||
                weapon.knockback < 0f || weapon.moveSpeedMultiplier <= 0f)
            {
                Debug.LogError($"무기 {weapon.id}의 산탄·산포·저지력·넉백·이동 배율 값이 올바르지 않습니다.");
            }
            if (weapon.akimboAllowed && (weapon.automatic || weapon.pellets != 1))
            {
                Debug.LogError($"무기 {weapon.id}: 아킴보는 단발 권총류만 허용합니다.");
            }
            if (weapon.projectileSpeed <= 0f || weapon.projectileLifetime <= 0f || weapon.projectileRadius <= 0f)
            {
                Debug.LogError($"무기 {weapon.id}의 발사체 속도·수명·반경 값이 올바르지 않습니다.");
            }
            else if (weapon.projectileSpeed * weapon.projectileLifetime < weapon.range)
            {
                Debug.LogWarning($"무기 {weapon.id}: 발사체 수명 동안 사거리에 닿지 못합니다(속도×수명 < 사거리).");
            }
        }
    }
}
