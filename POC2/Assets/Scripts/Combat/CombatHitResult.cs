// 적중 처리의 상세 결과. 발사체 및 피드백 시스템에서 부위 판정·피해·저지력·넉백 상태를 확인한다.
public struct CombatHitResult
{
    public bool damagedEnemy;
    public bool killed;
    public bool isHeadshot;
    public bool isHelmetHit;
    public float damageDealt;
    public float stoppingApplied;
    public bool staggered;
    public bool knockedBack;
}
