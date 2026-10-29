using System;

// Resources/ShopData.json의 직렬화 자료. 로직 없음. 조회는 ShopInfo가 담당한다.
[Serializable]
public sealed class ShopData
{
    [Serializable]
    public sealed class Item
    {
        public string id;
        // weapon | heal | perk
        public string type;
        // weapon은 무기 id, perk는 퍽 id, heal은 비워 둔다.
        public string target;
        public string label;
        public int price;
    }

    // 정비 상점 칸. 배열 순서가 화면 칸 순서다.
    public Item[] items = Array.Empty<Item>();
}
