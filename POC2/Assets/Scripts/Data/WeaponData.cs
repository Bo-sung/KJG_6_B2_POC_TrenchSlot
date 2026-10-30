using System;

// Resources/WeaponData.json의 직렬화 자료. 로직 없음. 조회는 WeaponInfo가 담당한다.
[Serializable]
public sealed class WeaponData
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        // 판매 해금 시대 id(ShopData.eras). 상점 진열 시점만 정하며 시대가 지나도 판매는 유지된다.
        public string era;
        // 손·바닥 외형 id(WeaponHeldModels·WeaponPickupCatalog). 같은 계열 총기는 외형을 공유한다.
        public string modelId;
        public string ammoId;
        public string name;
        public int magazineSize;
        // 산탄총은 산탄 1발 피해다.
        public float damage;
        public float range;
        public float shotsPerSecond;
        public float reloadSeconds;
        // 탄을 개별 삽입하는 무기는 한 발씩 예비탄을 소비한다.
        public bool reloadPerRound;
        public bool automatic;
        // 발사체 수와 산탄 자체의 확산 반각(도). 단발 무기는 탄 퍼짐과 별개로 0이다.
        public int pellets;
        public float spreadDegrees;
        // 발사마다 적용되는 산포 및 카메라 반동. 플레이 테스트 전 잠정값이다.
        public float hipSpreadDegrees;
        public float aimSpreadMultiplier = 1f;
        public float recoilPitchDegrees;
        public float recoilYawDegrees;
        public float recoilRecoveryDegreesPerSecond = 7f;
        // 발당 저지력. 산탄은 발사 1회 합산 상한(pelletStoppingPowerCap)을 둔다. 0이면 상한 없음.
        public float stoppingPower;
        public float pelletStoppingPowerCap;
        // 넉백 세기. 산탄총 등 지정 무기만 0보다 크다.
        public float knockback;
        // 들고 있을 때 이동속도 배율. 무거운 총기는 1보다 작다.
        public float moveSpeedMultiplier;
        // 발사체 탄도(provisional). 속도 m/s, 최대 수명 s, 스윕 반경 m. 최대 비행 거리는 range를 쓴다.
        public float projectileSpeed;
        public float projectileLifetime;
        public float projectileRadius;
        // 시작 시 보유 여부. 시작 무기 하나만 true.
        public bool startOwned;
        // 아킴보(같은 종류 두 자루를 양손에) 허용. 권총류만 true.
        public bool akimboAllowed;
    }

    // 아킴보 공통 규칙(provisional). 재장전은 양손 동시이며 두 자루 중 긴 재장전 시간에 이 배율을 곱한다.
    [Serializable]
    public sealed class AkimboSettings
    {
        public string status;
        public float reloadMultiplier;
    }

    // 떨어뜨린 무기가 놓일 자리. 플레이어 발밑 기준 거리(m)와 자리 확보 크기(m)다.
    [Serializable]
    public sealed class DropSettings
    {
        public float minDistance;
        public float maxDistance;
        public float clearanceRadius;
        public float clearanceHeight;
        // 외형 프리팹이 없는 modelId를 대신할 외형 id.
        public string fallbackModelId;
    }

    // 수치 확정 상태. "provisional"이면 임시값이며 플레이테스트 조율 전이다.
    public string status;
    public string note;
    public DropSettings drop = new DropSettings();
    public AkimboSettings akimbo = new AkimboSettings();
    public Entry[] weapons = Array.Empty<Entry>();
}
