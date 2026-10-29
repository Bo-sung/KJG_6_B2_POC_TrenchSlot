using System;

// 구조 검증용 잠정값. 전투 피해/웨이브 밸런스는 변경하지 않는다.
[Serializable]
public sealed class EnemyAiData
{
    public string status;
    public float hideSeconds = 1.5f;
    public float peekSeconds = 1.2f;
    public float coverRetrySeconds = 0.75f;
    public float revalidateSeconds = 0.25f;
    public float pathRefreshSeconds = 0.5f;
    public float stuckSeconds = 3f;
    public float arrivalDistance = 0.2f;
    public float searchRadius = 65f;
    public float navSampleDistance = 2f;
}
