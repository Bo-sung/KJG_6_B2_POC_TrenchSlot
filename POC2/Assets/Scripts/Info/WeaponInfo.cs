using UnityEngine;

// 무기 테이블 조회. 무기 차이는 사거리·연사력·탄창뿐이며 위력은 공통이다.
public static class WeaponInfo
{
    public static WeaponData.Entry Find(WeaponData _table, string _id)
    {
        if (_table == null || _table.weapons == null)
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
        foreach (var weapon in _table.weapons)
        {
            if (weapon == null)
            {
                continue;
            }
            if (weapon.magazineSize <= 0 || weapon.shotsPerSecond <= 0f || weapon.range <= 0f || weapon.reloadSeconds < 0f)
            {
                Debug.LogError($"무기 {weapon.id}의 탄창·연사력·사거리·재장전 값이 올바르지 않습니다.");
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
