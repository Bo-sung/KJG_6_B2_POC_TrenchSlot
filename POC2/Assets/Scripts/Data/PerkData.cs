using System;

// Resources/PerkData.json의 직렬화 자료. 로직 없음. 효과 계산은 PerkInventory가 담당한다.
[Serializable]
public sealed class PerkData
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string name;
        public string description;
        // 최대 스택. 1이면 한 번만 구매 가능.
        public int maxStack;
        // 스택 1당 효과량. 의미는 퍽 id마다 다르다(PerkInventory 주석 참조).
        public float value;
    }

    public Entry[] perks = Array.Empty<Entry>();
}
