using UnityEngine;

public static class EnemyAiInfo
{
    public static void Validate(EnemyAiData _table)
    {
        if (_table == null || _table.hideSeconds <= 0f || _table.peekSeconds <= 0f ||
            _table.coverRetrySeconds <= 0f || _table.revalidateSeconds <= 0f ||
            _table.pathRefreshSeconds <= 0f || _table.stuckSeconds <= 0f ||
            _table.arrivalDistance <= 0f || _table.searchRadius <= 0f || _table.navSampleDistance <= 0f ||
            _table.spawnMinDistance <= 0f || _table.spawnForwardDot < 0f || _table.spawnForwardDot >= 1f ||
            _table.spawnRetrySeconds <= 0f || _table.spawnAttemptsPerPoint < 1 || _table.spawnOccupancyPadding < 0f)
        {
            Debug.LogError("EnemyAiData: 엄폐·경로·스폰 시간/거리는 양수, 스폰 전방 내적은 [0, 1), 시도 횟수는 1 이상이어야 합니다.");
        }
    }
}
