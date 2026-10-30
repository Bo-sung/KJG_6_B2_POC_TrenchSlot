using System;

// Resources/PartData.json의 직렬화 자료. 로직 없음. 조회와 스탯 합산은 PartInfo가 담당한다.
[Serializable]
public sealed class PartData
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        // 장착 가능한 무기 종류 id. 파츠는 이 무기 종류의 개별 무기에만 붙는다.
        public string weaponId;
        // 장착 칸(PartInfo.SLOT_*). 한 무기에 같은 칸 파츠는 하나만 붙는다.
        public string slot;
        public string name;
        public string description;
        public float damageMultiplier;
        public float fireRateMultiplier;
        public int magazineBonus;
        public float reloadMultiplier;
        // 발사 시 탄창 탄을 소비하지 않을 확률(0~1).
        public float ammoSaveChance;
    }

    public string status;
    public string note;
    public Entry[] parts = Array.Empty<Entry>();
}
