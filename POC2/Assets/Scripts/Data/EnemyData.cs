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
        // 전투(provisional, 플레이테스트 후 결정). 적용은 Enemy가 한다.
        // 헤드샷 피해 배율, 헬멧 착용과 머리 피해 감소율(머리 피해가 몸통보다 낮아지지 않도록 전투 쪽이 제한).
        public float headshotMultiplier = 1.5f;
        public bool hasHelmet;
        public float helmetDamageReduction;
        // 저지력 저항 기준(누적치가 넘으면 경직), 초당 감소량, 경직 시간, 경직 후 면역 시간.
        public float stoppingResistance;
        public float stoppingDecayRate;
        public float staggerDuration;
        public float staggerImmunityDuration;
        // 넉백 저항(0~1). 중장일수록 높다.
        public float knockbackResistance;
        // 머리 판정 영역: 몸 콜라이더 높이 대비 이 비율 이상에 맞으면 머리(헬멧 착용 시 헬멧)다. EnemyHitbox가 붙은 콜라이더는 그 지정을 우선한다.
        public float headHeightRatio = 0.72f;
    }

    public Entry[] enemies = Array.Empty<Entry>();
}
