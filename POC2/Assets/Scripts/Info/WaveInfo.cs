using UnityEngine;

// 웨이브 한 판의 확정 값. 표에 있으면 표 값, 없으면 마지막 표 값에서 스케일링한 값이다.
public struct WaveSpec
{
    public int wave;
    public float durationSeconds;
    public int rushers;
    public int riflemen;
    public int heavies;
    public float spawnInterval;
    public string era;
    public string enemyWeapon;
    public Color tint;
    public float hpMultiplier;
}

// 웨이브 테이블 조회·스케일링. 표 밖 웨이브는 마지막 표 행에서 증가분을 더한다.
public static class WaveInfo
{
    public static WaveSpec Resolve(WaveData _table, int _wave)
    {
        int wave = Mathf.Max(1, _wave);
        var spec = new WaveSpec { wave = wave, hpMultiplier = 1f };
        if (_table == null || _table.waves == null || _table.waves.Length == 0)
        {
            Debug.LogError("웨이브 테이블이 비어 있습니다. 기본값 1웨이브로 진행합니다.");
            spec.durationSeconds = 40f;
            spec.spawnInterval = 1.5f;
            spec.era = "MUSKET";
            spec.enemyWeapon = "musket";
            spec.tint = Color.gray;
            spec.rushers = 3;
            spec.riflemen = 2;
            return spec;
        }

        WaveData.Entry row = null;
        WaveData.Entry last = _table.waves[_table.waves.Length - 1];
        foreach (var entry in _table.waves)
        {
            if (entry != null && entry.wave == wave)
            {
                row = entry;
                break;
            }
        }

        if (row != null)
        {
            spec.durationSeconds = row.durationSeconds;
            spec.spawnInterval = row.spawnInterval;
            spec.era = row.era;
            spec.enemyWeapon = row.enemyWeapon;
            spec.tint = ParseTint(row.tint);
            spec.rushers = row.rushers;
            spec.riflemen = row.riflemen;
            spec.heavies = row.heavies;
            return spec;
        }

        int extra = Mathf.Max(0, wave - last.wave);
        var scaling = _table.scaling ?? new WaveData.Scaling();
        spec.durationSeconds = last.durationSeconds;
        spec.spawnInterval = last.spawnInterval;
        spec.era = last.era;
        // 표 밖 웨이브는 마지막 행의 무기를 그대로 쓴다(시대 진행은 표 안에서 끝낸다).
        spec.enemyWeapon = last.enemyWeapon;
        spec.tint = ParseTint(last.tint);
        spec.rushers = last.rushers + extra * scaling.extraRushersPerWave;
        spec.riflemen = last.riflemen + extra * scaling.extraRiflemenPerWave;
        spec.heavies = last.heavies + extra * scaling.extraHeaviesPerWave;
        spec.hpMultiplier = 1f + extra * scaling.hpGrowthPerWave;
        return spec;
    }

    public static Color ParseTint(string _hex)
    {
        if (!string.IsNullOrEmpty(_hex) && ColorUtility.TryParseHtmlString(_hex, out Color color))
        {
            return color;
        }
        return Color.gray;
    }

    public static void Validate(WaveData _table)
    {
        if (_table == null || _table.waves == null || _table.waves.Length == 0)
        {
            Debug.LogError("웨이브 테이블이 비어 있습니다.");
            return;
        }
        foreach (var entry in _table.waves)
        {
            if (entry == null)
            {
                continue;
            }
            if (entry.durationSeconds <= 0f || entry.spawnInterval <= 0f)
            {
                Debug.LogError($"웨이브 {entry.wave}의 시간·스폰 간격이 올바르지 않습니다.");
            }
        }
    }

    // 웨이브가 가리키는 적 무기 id가 무기 테이블에 있는지 확인한다. 빈 값도 누락으로 본다.
    public static void ValidateEnemyWeapons(WaveData _waves, EnemyWeaponData _weapons)
    {
        if (_waves == null || _waves.waves == null)
        {
            return;
        }
        foreach (var entry in _waves.waves)
        {
            if (entry != null && EnemyWeaponInfo.Find(_weapons, entry.enemyWeapon) == null)
            {
                Debug.LogError($"웨이브 {entry.wave}의 적 무기 '{entry.enemyWeapon}'를 적 무기 테이블에서 찾지 못했습니다.");
            }
        }
    }
}
