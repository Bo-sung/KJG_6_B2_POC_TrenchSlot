using UnityEngine;

// 적 테이블 조회.
public static class EnemyInfo
{
    public const string RUSHER_ID = "rusher";
    public const string RIFLEMAN_ID = "rifleman";
    public const string HEAVY_ID = "heavy";

    public static EnemyData.Entry Find(EnemyData _table, string _id)
    {
        if (_table == null || _table.enemies == null)
        {
            return null;
        }
        foreach (var enemy in _table.enemies)
        {
            if (enemy != null && enemy.id == _id)
            {
                return enemy;
            }
        }
        return null;
    }

    public static void Validate(EnemyData _table)
    {
        if (_table == null || _table.enemies == null || _table.enemies.Length == 0)
        {
            Debug.LogError("적 테이블이 비어 있습니다.");
            return;
        }
        foreach (var enemy in _table.enemies)
        {
            if (enemy == null)
            {
                continue;
            }
            if (enemy.maxHealth <= 0f || enemy.moveSpeed <= 0f || enemy.attackInterval <= 0f)
            {
                Debug.LogError($"적 {enemy.id}의 체력·이동속도·공격 간격 값이 올바르지 않습니다.");
            }
            if (enemy.reward < 0)
            {
                Debug.LogError($"적 {enemy.id}의 보상이 음수입니다.");
            }
        }
    }
}
