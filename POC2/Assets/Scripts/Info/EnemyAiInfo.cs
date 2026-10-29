using UnityEngine;

public static class EnemyAiInfo
{
    public static void Validate(EnemyAiData _table)
    {
        if (_table == null || _table.hideSeconds <= 0f || _table.peekSeconds <= 0f ||
            _table.coverRetrySeconds <= 0f || _table.revalidateSeconds <= 0f ||
            _table.pathRefreshSeconds <= 0f || _table.stuckSeconds <= 0f ||
            _table.arrivalDistance <= 0f || _table.searchRadius <= 0f || _table.navSampleDistance <= 0f)
        {
            Debug.LogError("EnemyAiData의 엄폐·경로 시간/거리 값은 양수여야 합니다.");
        }
    }
}
