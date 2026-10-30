using System;

// Resources/EnemyWeaponData.json의 직렬화 자료. 로직 없음. 조회는 EnemyWeaponInfo가 담당한다.
[Serializable]
public sealed class EnemyWeaponData
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        // 동일한 총기 종류를 가리키는 플레이어 무기 ID. 적의 전투 밸런스는 아래 별도 수치를 사용한다.
        public string playerWeaponId;
        public string dropAmmoId;
        public string name;
        // 시대 표기(MUSKET·WWI·WWII). 표시·확인용이며 판정에는 쓰지 않는다.
        public string era;
        // 원거리 적이 이 무기로 쏠 때의 값. 근접 적에게는 적용되지 않는다.
        public float attackDamage;
        public float attackRange;
        public float attackInterval;
        // 발사체 탄도(provisional). 속도 m/s, 최대 수명 s, 스윕 반경 m. 최대 비행 거리는 attackRange를 쓴다.
        public float projectileSpeed;
        public float projectileLifetime;
        public float projectileRadius;
    }

    // 수치 확정 상태. "provisional"이면 임시값이며 공개 레퍼런스 기준 조율 전이다(기획노트 §1-5·§1-6).
    public string status;
    public Entry[] weapons = Array.Empty<Entry>();
}
