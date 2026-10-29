using System;

// Resources/WeaponData.json의 직렬화 자료. 로직 없음. 조회는 WeaponInfo가 담당한다.
[Serializable]
public sealed class WeaponData
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string name;
        public int magazineSize;
        public float damage;
        public float range;
        public float shotsPerSecond;
        public float reloadSeconds;
        public bool automatic;
        // 발사체 탄도(provisional). 속도 m/s, 최대 수명 s, 스윕 반경 m. 최대 비행 거리는 range를 쓴다.
        public float projectileSpeed;
        public float projectileLifetime;
        public float projectileRadius;
        // 시작 시 보유 여부. 리볼버만 true.
        public bool startOwned;
    }

    // 떨어뜨린 무기가 놓일 자리. 플레이어 발밑 기준 거리(m)와 자리 확보 크기(m)다.
    [Serializable]
    public sealed class DropSettings
    {
        public float minDistance;
        public float maxDistance;
        public float clearanceRadius;
        public float clearanceHeight;
    }

    // 수치 확정 상태. "provisional"이면 임시값이며 공개 레퍼런스 기준 조율 전이다(기획노트 §1-5·§1-6).
    public string status;
    public DropSettings drop = new DropSettings();
    public Entry[] weapons = Array.Empty<Entry>();
}
