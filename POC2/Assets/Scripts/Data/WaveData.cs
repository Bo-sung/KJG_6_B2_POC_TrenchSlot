using System;

// Resources/WaveData.json의 직렬화 자료. 로직 없음. 조회·스케일링은 WaveInfo가 담당한다.
[Serializable]
public sealed class WaveData
{
    [Serializable]
    public sealed class Entry
    {
        public int wave;
        // 웨이브 전체 시간(초). 전멸 후에도 이 시간까지 슬롯 회전이 가능하다.
        public float durationSeconds;
        public int rushers;
        public int riflemen;
        public int heavies;
        // 적 한 명 스폰 간격(초).
        public float spawnInterval;
        public string era;
        // 이 웨이브의 원거리 적이 드는 무기 id(EnemyWeaponData). 근접 적에게는 쓰이지 않는다.
        public string enemyWeapon;
        // 적 유닛 색상(#RRGGBB). 시대가 바뀌면 색만 교체한다.
        public string tint;
    }

    [Serializable]
    public sealed class Scaling
    {
        // 표에 없는 웨이브에서 적 체력에 곱하는 증가율(웨이브당).
        public float hpGrowthPerWave;
        // 표 마지막 웨이브 이후 웨이브마다 더하는 적 수.
        public int extraRushersPerWave;
        public int extraRiflemenPerWave;
        public int extraHeaviesPerWave;
    }

    public Entry[] waves = Array.Empty<Entry>();
    public Scaling scaling = new Scaling();
}
