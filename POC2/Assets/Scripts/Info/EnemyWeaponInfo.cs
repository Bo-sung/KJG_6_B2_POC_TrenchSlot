using UnityEngine;

// 적 무기 테이블 조회. 무기는 원거리 적에게만 붙고, 근접 적은 항상 무기 없이 나온다.
public static class EnemyWeaponInfo
{
    public static EnemyWeaponData.Entry Find(EnemyWeaponData _table, string _id)
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

    // 이 적이 웨이브 무기를 들어야 하면 그 무기를, 근접 적이면 null을 준다.
    public static EnemyWeaponData.Entry ForEnemy(EnemyData.Entry _enemy, EnemyWeaponData _table, string _waveWeaponId)
    {
        if (_enemy == null || !_enemy.ranged)
        {
            return null;
        }
        return Find(_table, _waveWeaponId);
    }

    public static void ValidateAmmo(EnemyWeaponData _table, AmmoData _ammo)
    {
        if (_table == null || _table.weapons == null)
        {
            return;
        }
        foreach (var weapon in _table.weapons)
        {
            if (weapon != null && AmmoInfo.Find(_ammo, weapon.dropAmmoId) == null)
            {
                Debug.LogError($"적 무기 {weapon.id}의 드롭 탄종 참조가 올바르지 않습니다: {weapon.dropAmmoId}");
            }
        }
    }

    public static void ValidatePlayerWeapons(EnemyWeaponData _table, WeaponData _weapons)
    {
        if (_table == null || _table.weapons == null)
        {
            return;
        }
        foreach (var enemyWeapon in _table.weapons)
        {
            if (enemyWeapon == null)
            {
                continue;
            }
            var playerWeapon = WeaponInfo.Find(_weapons, enemyWeapon.playerWeaponId);
            if (playerWeapon == null || playerWeapon.ammoId != enemyWeapon.dropAmmoId)
            {
                Debug.LogError($"적 무기 {enemyWeapon.id}의 플레이어 무기 또는 탄종 참조가 올바르지 않습니다.");
            }
        }
    }

    public static void Validate(EnemyWeaponData _table)
    {
        if (_table == null || _table.weapons == null || _table.weapons.Length == 0)
        {
            Debug.LogError("적 무기 테이블이 비어 있습니다.");
            return;
        }
        foreach (var weapon in _table.weapons)
        {
            if (weapon == null)
            {
                continue;
            }
            if (weapon.attackDamage <= 0f || weapon.attackRange <= 0f || weapon.attackInterval <= 0f)
            {
                Debug.LogError($"적 무기 {weapon.id}의 피해·사거리·발사 간격 값이 올바르지 않습니다.");
            }
            if (weapon.projectileSpeed <= 0f || weapon.projectileLifetime <= 0f || weapon.projectileRadius <= 0f)
            {
                Debug.LogError($"적 무기 {weapon.id}의 발사체 속도·수명·반경 값이 올바르지 않습니다.");
            }
            else if (weapon.projectileSpeed * weapon.projectileLifetime < weapon.attackRange)
            {
                Debug.LogWarning($"적 무기 {weapon.id}: 발사체 수명 동안 사거리에 닿지 못합니다(속도×수명 < 사거리).");
            }
        }
    }
}
