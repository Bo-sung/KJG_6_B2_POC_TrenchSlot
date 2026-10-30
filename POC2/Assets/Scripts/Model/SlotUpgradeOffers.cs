using System;
using System.Collections.Generic;

// 슬롯 업그레이드 진열. 처음 열릴 때 서로 다른 퍽을 칸 수만큼 추첨한다.
// 일부만 사면 남은 상품과 빈 칸을 유지하고, 진열된 상품을 전부 산 뒤 다음 웨이브 종료 때 새로 추첨한다.
// 구매 제한(퍽 maxStack)에 도달한 퍽은 추첨에서 빠지며, 후보가 부족한 칸은 품절(빈 id)이다.
public sealed class SlotUpgradeOffers
{
    private readonly ShopData table;
    private readonly PerkInventory perks;
    private readonly Random random;
    private readonly string[] offers;
    private readonly bool[] bought;

    public event Action Changed;

    public SlotUpgradeOffers(ShopData _table, PerkInventory _perks, int _seed)
    {
        table = _table;
        perks = _perks;
        random = new Random(_seed);
        int count = ShopInfo.SlotOfferCount(_table);
        offers = new string[count];
        bought = new bool[count];
        Draw();
    }

    public int Count => offers.Length;

    // 진열 퍽 id. 품절 칸은 빈 문자열이다.
    public string Get(int _index)
    {
        return _index >= 0 && _index < offers.Length ? offers[_index] ?? "" : "";
    }

    public bool IsBought(int _index)
    {
        return _index >= 0 && _index < bought.Length && bought[_index];
    }

    public bool IsSoldOut(int _index) => Get(_index) == "";

    public bool CanBuy(int _index)
    {
        return !IsSoldOut(_index) && !IsBought(_index) && perks != null && perks.CanBuy(Get(_index));
    }

    public void MarkBought(int _index)
    {
        if (!IsSoldOut(_index) && !IsBought(_index))
        {
            bought[_index] = true;
            Changed?.Invoke();
        }
    }

    // 웨이브 종료 시 호출. 남은 상품이 있으면 진열을 유지한다.
    public void OnWaveEnded()
    {
        // 다른 경로로 구매 제한에 도달해 더는 살 수 없는 상품은 남은 상품으로 보지 않는다(진열이 영구히 막히지 않게).
        for (int i = 0; i < offers.Length; i++)
        {
            if (CanBuy(i))
            {
                return;
            }
        }
        Draw();
    }

    private void Draw()
    {
        var candidates = new List<string>();
        if (table != null && table.slotUpgrades != null && table.slotUpgrades.pool != null)
        {
            foreach (var upgrade in table.slotUpgrades.pool)
            {
                if (upgrade != null && perks != null && perks.CanBuy(upgrade.perkId) && !candidates.Contains(upgrade.perkId))
                {
                    candidates.Add(upgrade.perkId);
                }
            }
        }
        for (int i = 0; i < offers.Length; i++)
        {
            bought[i] = false;
            if (candidates.Count == 0)
            {
                offers[i] = "";
                continue;
            }
            int pick = random.Next(candidates.Count);
            offers[i] = candidates[pick];
            candidates.RemoveAt(pick);
        }
        Changed?.Invoke();
    }
}
