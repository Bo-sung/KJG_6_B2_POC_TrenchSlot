using UnityEditor;
using UnityEngine;

// 전투 테이블 연동 검사: 적 전투 수치가 EnemyData에서 그대로 들어가고, 머리·헬멧 영역이 테이블 비율로 판정되는지 본다.
// 플레이 모드 없이 실제 Enemy 프리팹을 세워 Configure·DetermineHitZone을 호출하고 바로 지운다. 에디터를 종료하지 않는다.
public static class PocCombatVerification
{
    private const string TAG = "[PocCombatVerification]";
    private static int failures;

    [MenuItem("POC/Verify Combat Tables")]
    public static void RunFromMenu()
    {
        Debug.Log($"{TAG} finished: failures={RunChecks()}");
    }

    public static int RunChecks()
    {
        failures = 0;
        TableManager.ReloadAfterTableEdit();
        var table = TableManager.Instance;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PocAssetBuilder.GAMEPLAY_PREFAB_DIR}/Enemy.prefab");
        Check(prefab != null, "Enemy 프리팹을 찾는다");
        if (prefab == null)
        {
            return failures;
        }
        foreach (var stats in table.Enemy.enemies)
        {
            var enemy = Object.Instantiate(prefab).GetComponent<Enemy>();
            try
            {
                enemy.Configure(stats, null, 1f, Color.gray, null);
                Check(Mathf.Approximately(enemy.HeadshotMultiplier, stats.headshotMultiplier) && enemy.HasHelmet == stats.hasHelmet &&
                      Mathf.Approximately(enemy.HelmetDamageReduction, stats.helmetDamageReduction) &&
                      Mathf.Approximately(enemy.StoppingResistance, stats.stoppingResistance) &&
                      Mathf.Approximately(enemy.StoppingDecayRate, stats.stoppingDecayRate) &&
                      Mathf.Approximately(enemy.StaggerDuration, stats.staggerDuration) &&
                      Mathf.Approximately(enemy.StaggerImmunityDuration, stats.staggerImmunityDuration) &&
                      Mathf.Approximately(enemy.KnockbackResistance, stats.knockbackResistance),
                    $"{stats.id}: 전투 수치를 EnemyData에서 그대로 읽는다");

                var capsule = enemy.GetComponent<CapsuleCollider>();
                float bottom = enemy.transform.position.y + capsule.center.y - capsule.height * 0.5f;
                HitZone below = Zone(enemy, bottom + capsule.height * (stats.headHeightRatio - 0.02f));
                HitZone above = Zone(enemy, bottom + capsule.height * (stats.headHeightRatio + 0.02f));
                HitZone head = stats.hasHelmet ? HitZone.Helmet : HitZone.Head;
                Check(below == HitZone.Body && above == head, $"{stats.id}: 머리 영역은 테이블 비율 {stats.headHeightRatio} 기준({above})");
            }
            finally
            {
                Object.DestroyImmediate(enemy.gameObject);
            }
        }
        Debug.Log($"{TAG} checks: failures={failures}");
        return failures;
    }

    // 콜라이더 없이 명중점만 준 판정(EnemyHitbox가 없는 경우의 높이 판정 경로).
    private static HitZone Zone(Enemy _enemy, float _height)
    {
        var hit = new RaycastHit { point = new Vector3(_enemy.transform.position.x, _height, _enemy.transform.position.z) };
        return _enemy.DetermineHitZone(hit);
    }

    private static void Check(bool _ok, string _label)
    {
        if (!_ok)
        {
            failures++;
        }
        Debug.Log($"{TAG} {(_ok ? "PASS" : "FAIL")} {_label}");
    }
}
