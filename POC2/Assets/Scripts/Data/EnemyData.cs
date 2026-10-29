using System;

// Resources/EnemyData.json의 직렬화 자료. 로직 없음. 조회는 EnemyInfo가 담당한다.
[Serializable]
public sealed class EnemyData
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string name;
        public float maxHealth;
        public float moveSpeed;
        public float attackDamage;
        public float attackInterval;
        // 공격 사거리(m). 원거리는 이 거리에서 쏘고, 근접은 이 거리 안에서 때린다.
        public float attackRange;
        // 이 거리까지 접근한 뒤 멈춘다. 근접 러셔는 0.
        public float stopDistance;
        public bool ranged;
        // 무기 없이 원거리인 적의 폴백 탄도(provisional). 웨이브 무기가 있으면 EnemyWeaponData 값을 쓴다.
        public float projectileSpeed;
        public float projectileLifetime;
        public float projectileRadius;
        // 처치 시 지급 소지금.
        public int reward;
    }

    public Entry[] enemies = Array.Empty<Entry>();
}
