using System;
using UnityEditor;
using UnityEngine;

// 상점·무기 슬롯·파츠·스탯·슬롯 업그레이드 모델 검사. 씬·플레이 모드 없이 테이블과 모델만 본다.
// 에디터를 종료하지 않으므로 열린 에디터에서도 돌릴 수 있다(메뉴 POC/Verify Shop Models, 또는 PocVerification.RunUnitChecks가 함께 부른다).
public static class PocShopVerification
{
    private const string TAG = "[PocShopVerification]";
    private static int failures;

    [MenuItem("POC/Verify Shop Models")]
    public static void RunFromMenu()
    {
        int result = RunModelChecks();
        Debug.Log($"{TAG} finished: failures={result}");
    }

    // 실패 수를 돌려준다.
    public static int RunModelChecks()
    {
        failures = 0;
        TableManager.ReloadAfterTableEdit();
        var table = TableManager.Instance;
        var shop = table.Shop;

        // 테이블 구성
        Check(table.Weapon.weapons.Length == 11 && WeaponInfo.StartWeaponId(table.Weapon) == "m1873", "무기 11종, 시작 무기 M1873");
        Check(table.Ammo.ammoTypes.Length == 8 && AmmoInfo.Find(table.Ammo, "colt45") != null, "세부 탄종 8종");
        Check(table.Part.parts.Length == 9, "POC 파츠 9종(1887 3종)");
        Check(WeaponInfo.Find(table.Weapon, "m1873").ammoId == "colt45" && WeaponInfo.Find(table.Weapon, "colt1878").ammoId == "ga12", "Colt 1878은 12게이지 산탄총이다");

        // 시대 진열(1~5 / 6~10 / 11~ 누적)
        Check(ShopInfo.EraForWave(shop, 1).id == "1887" && ShopInfo.EraForWave(shop, 5).id == "1887", "1~5웨이브는 1887");
        Check(ShopInfo.EraForWave(shop, 6).id == "WW1" && ShopInfo.EraForWave(shop, 11).id == "WW2" && ShopInfo.EraForWave(shop, 30).id == "WW2", "6웨이브 WW1, 11웨이브 이후 WW2 유지");
        var mp18 = WeaponInfo.Find(table.Weapon, "mp18");
        var win1886 = WeaponInfo.Find(table.Weapon, "win1886");
        Check(!ShopInfo.IsWeaponUnlocked(shop, mp18, 5) && ShopInfo.IsWeaponUnlocked(shop, mp18, 6), "WW1 무기는 6웨이브부터 진열");
        Check(ShopInfo.IsWeaponUnlocked(shop, win1886, 15), "이전 시대 무기도 계속 판다(누적)");
        Check(!ShopInfo.IsAmmoUnlocked(shop, table.Weapon, "p9x19", 5) && ShopInfo.IsAmmoUnlocked(shop, table.Weapon, "p9x19", 6), "탄약 팩은 해당 탄종 무기 해금과 함께 진열");

        // 가격
        var maxHealth = ShopInfo.FindStat(shop, PlayerStats.MAX_HEALTH);
        var moveSpeed = ShopInfo.FindStat(shop, PlayerStats.MOVE_SPEED);
        Check(ShopInfo.StatPrice(shop, maxHealth, 0) == 100 && ShopInfo.StatPrice(shop, maxHealth, 1) == 250 && ShopInfo.StatPrice(shop, maxHealth, 2) == 625, "최대 체력 가격 100 → 250 → 625");
        Check(ShopInfo.StatPrice(shop, moveSpeed, 1) == 375 && ShopInfo.StatPrice(shop, moveSpeed, 2) % 5 == 0, "스탯 가격은 2.5배씩 오르고 $5 단위로 반올림");
        Check(ShopInfo.PartPrice(shop, PartInfo.Find(table.Part, "win1886_long_tube")) == 50, "파츠 가격 = 무기가 140 × 0.35 → $50");
        Check(ShopInfo.AmmoPackPrice(shop, ShopInfo.FindAmmoPack(shop, "colt45")) == 12 && ShopInfo.AmmoPackPrice(shop, ShopInfo.FindAmmoPack(shop, "m792")) == 180, "탄약 팩 = 기준 무기가 × 0.2 (.45 Colt $12, 7.92 $180)");

        // 무기 최종 스탯
        var m1873 = WeaponInfo.Find(table.Weapon, "m1873");
        var plain = WeaponInfo.Resolve(m1873, null, 1f);
        var heavy = WeaponInfo.Resolve(m1873, new[] { PartInfo.Find(table.Part, "m1873_heavy_barrel") }, 0.88f);
        Check(Mathf.Approximately(plain.damage, 30f) && Mathf.Approximately(heavy.damage, 36f) && Mathf.Approximately(heavy.shotsPerSecond, 1.7f), "무거운 총열: 피해 ×1.2, 연사 ×0.85");
        Check(Mathf.Approximately(heavy.reloadSeconds, 2.4f * 0.88f), "장전 속도 스탯이 재장전 시간에 곱해진다");
        var tube = WeaponInfo.Resolve(win1886, new[] { PartInfo.Find(table.Part, "win1886_long_tube"), PartInfo.Find(table.Part, "m1873_heavy_barrel") }, 1f);
        Check(tube.magazineSize == 11 && Mathf.Approximately(tube.damage, win1886.damage), "다른 무기 종류의 파츠는 스탯에 들어가지 않는다");
        var shotgun = WeaponInfo.Resolve(WeaponInfo.Find(table.Weapon, "m1897"), null, 1f);
        Check(shotgun.pellets == 8 && shotgun.knockback > 0f && shotgun.pelletStoppingPowerCap > 0f, "산탄총은 산탄 수·넉백·저지력 합산 상한을 가진다");

        // 파츠 개체(중복 보유)
        var parts = new PartInventory(table.Part);
        var a = new WeaponInstance("m1873", 6);
        var b = new WeaponInstance("m1873", 6);
        var colt = new WeaponInstance("colt1878", 2);
        Check(!parts.Attach("m1873_nickel", a), "보유하지 않은 파츠는 붙일 수 없다");
        var nickel1 = parts.Add("m1873_nickel");
        var nickel2 = parts.Add("m1873_nickel");
        Check(nickel1 != null && nickel2 != null && nickel1 != nickel2 && parts.OwnedCount("m1873_nickel") == 2 && parts.StoredCount("m1873_nickel") == 2,
            "같은 파츠를 여러 개 보유한다(개체별 보관)");
        Check(parts.Add("no_such_part") == null, "테이블에 없는 파츠는 보유할 수 없다");
        Check(parts.Attach(nickel1, a) && parts.Attach(nickel2, b) && a.HasPart("m1873_nickel") && b.HasPart("m1873_nickel") &&
            nickel1.AttachedTo == a && nickel2.AttachedTo == b && parts.AttachedCount("m1873_nickel") == 2,
            "같은 파츠 두 개를 같은 종류 두 자루에 하나씩 붙인다");
        Check(!parts.Attach(nickel1, colt) && nickel1.AttachedTo == a, "다른 종류 무기에는 붙일 수 없다(실패 시 그대로)");
        Check(parts.Attach(nickel1, b) && nickel1.AttachedTo == b && nickel2.IsStored && !a.HasPart("m1873_nickel") && b.PartInstanceIn(PartInfo.SLOT_MUZZLE) == nickel1,
            "같은 칸에 붙이면 원래 개체는 보관함으로 가고, 옮긴 무기의 칸은 빈다");
        Check(parts.Detach(nickel1) && nickel1.IsStored && b.PartCount == 0 && parts.StoredCount("m1873_nickel") == 2, "분리한 개체는 보관함으로 간다");
        Check(parts.Attach(nickel1, a) && parts.Attach(nickel2, b), "보관한 개체를 다시 무료로 붙인다");
        var heavy1 = parts.Add("m1873_heavy_barrel");
        Check(parts.Attach(heavy1, a) && a.PartCount == 2 && parts.PartsOf(a).Count == 2, "다른 칸 파츠는 함께 붙는다");
        var dropped = a;
        Check(nickel1.AttachedTo == dropped && heavy1.AttachedTo == dropped, "무기 개체에 붙은 파츠는 그 무기를 따라간다(바닥에 놓아도 유지)");

        // 아킴보 모델
        Check(WeaponInfo.CanAkimbo(m1873) && !WeaponInfo.CanAkimbo(WeaponInfo.Find(table.Weapon, "colt1878")) && WeaponInfo.CanAkimbo(WeaponInfo.Find(table.Weapon, "p08")),
            "M1873·P08은 아킴보 허용, Colt 1878 산탄총은 불가");
        Check(!WeaponInfo.CanAkimbo(win1886) && !WeaponInfo.CanAkimbo(mp18) && !WeaponInfo.CanAkimbo(WeaponInfo.Find(table.Weapon, "m1897")), "소총·기관단총·샷건은 아킴보 불가");
        Check(WeaponInfo.AkimboReloadMultiplier(table.Weapon) > 1f, "아킴보 재장전 배율(provisional) > 1");
        var pair = new WeaponLoadout(2);
        pair.Put(0, a);
        Check(!pair.CanPairAkimbo && !pair.SetAkimbo(true), "한 자루로는 아킴보를 켤 수 없다");
        pair.Put(1, colt);
        Check(!pair.CanPairAkimbo, "다른 종류 두 자루는 아킴보 짝이 아니다");
        pair.Put(1, b);
        Check(pair.CanPairAkimbo && pair.SetAkimbo(true) && pair.Akimbo && pair.OffHandIndex == 1 && pair.OffHand == b, "같은 종류 두 자루로 아킴보를 켠다");
        Check(pair.SetActive(1) && !pair.Akimbo && pair.ActiveIndex == 1, "슬롯 전환은 아킴보를 푼다");
        pair.SetAkimbo(true);
        pair.Put(1, new WeaponInstance("m1873", 6));
        Check(!pair.Akimbo && pair.OffHandIndex == -1, "슬롯 무기가 바뀌면 아킴보가 풀린다");
        Check(pair.Put(0, pair.Get(1)) == null && pair.Get(0) == a, "같은 무기를 두 슬롯에 넣지 않는다");

        // 스탯
        var stats = new PlayerStats(shop);
        stats.Buy(PlayerStats.ARMOR);
        stats.Buy(PlayerStats.ARMOR);
        stats.Buy(PlayerStats.MOVE_SPEED);
        stats.Buy(PlayerStats.AMMO_CAPACITY);
        Check(Mathf.Approximately(stats.DamageTakenMultiplier, 0.94f * 0.94f) && Mathf.Approximately(stats.MoveSpeedMultiplier, 1.07f) && stats.AmmoCapacityBonus == 100,
            "방어력 곱연산·이동속도 합연산·탄약 보유량 +100");

        // 탄약 팩
        var ammo = new AmmoInventory(table.Ammo);
        var wallet = new PlayerWallet(100);
        var ammoShop = new AmmoShop(shop, ammo, wallet);
        int reserve = ammo.GetReserve("colt45");
        Check(ammoShop.TryBuy("colt45") == "" && ammo.GetReserve("colt45") == reserve + 125 && wallet.Balance == 88, "탄약 팩은 125발을 더하고 팩 가격만 낸다");
        ammo.Add("colt45", 10000);
        Check(ammoShop.TryBuy("colt45") != "" && wallet.Balance == 88, "최대 예비탄이면 팩을 살 수 없다");

        // 슬롯 업그레이드 진열
        var perks = new PerkInventory(table.Perk);
        var offers = new SlotUpgradeOffers(shop, perks, 1234);
        Check(offers.Count == 2 && offers.Get(0) != "" && offers.Get(1) != "" && offers.Get(0) != offers.Get(1), "슬롯 업그레이드는 서로 다른 2종을 진열한다");
        string first = offers.Get(0);
        string second = offers.Get(1);
        perks.Buy(first);
        offers.MarkBought(0);
        offers.OnWaveEnded();
        Check(offers.Get(0) == first && offers.IsBought(0) && offers.Get(1) == second && !offers.IsBought(1), "일부만 사면 남은 상품과 빈 칸을 유지한다");
        perks.Buy(second);
        offers.MarkBought(1);
        offers.OnWaveEnded();
        Check(!offers.IsBought(0) && !offers.IsBought(1), "전부 사면 웨이브 종료 때 새로 진열한다");
        // 진열 상품이 다른 경로로 구매 제한에 도달해도 진열이 막히지 않는다.
        var lockPerks = new PerkInventory(table.Perk);
        var lockOffers = new SlotUpgradeOffers(shop, lockPerks, 77);
        string stuck = lockOffers.Get(1);
        lockPerks.Buy(lockOffers.Get(0));
        lockOffers.MarkBought(0);
        for (int i = 0; i < 10; i++)
        {
            lockPerks.Buy(stuck);
        }
        lockOffers.OnWaveEnded();
        Check(!lockOffers.IsBought(0) && lockOffers.Get(0) != stuck && lockOffers.Get(1) != stuck, "살 수 없게 된 진열 상품은 다음 웨이브 종료 때 새 진열로 바뀐다");

        for (int i = 0; i < 20; i++)
        {
            foreach (var perk in table.Perk.perks)
            {
                perks.Buy(perk.id);
            }
        }
        for (int i = 0; i < offers.Count; i++)
        {
            offers.MarkBought(i);
        }
        offers.OnWaveEnded();
        Check(offers.IsSoldOut(0) && offers.IsSoldOut(1), "구매 제한에 도달한 퍽만 남으면 품절 처리한다");

        // 페이플러스·이월 저금통
        var economy = new PerkInventory(table.Perk);
        Check(Mathf.Approximately(economy.PayoutMultiplier, 1f) && Mathf.Approximately(economy.PiggyBankMultiplier(true), 1f), "미보유 시 지급 배율 1");
        economy.Buy(PerkInventory.PAY_PLUS);
        economy.Buy(PerkInventory.PIGGY_BANK);
        Check(Mathf.Approximately(economy.PayoutMultiplier, 1.15f) && !economy.CanBuy(PerkInventory.PAY_PLUS), "페이플러스: 지급 +15%, 런당 1회");
        Check(Mathf.Approximately(economy.PiggyBankMultiplier(true), 2f) && Mathf.Approximately(economy.PiggyBankMultiplier(false), 1f) && !economy.CanBuy(PerkInventory.PIGGY_BANK),
            "이월 저금통: 조건 충족 시 첫 회전 x2, 런당 1회");
        Check(ShopInfo.FindSlotUpgrade(shop, PerkInventory.PAY_PLUS) != null && ShopInfo.FindSlotUpgrade(shop, PerkInventory.PIGGY_BANK) != null, "슬롯 업그레이드 풀에 페이플러스·이월 저금통 포함");

        // 슬롯 업그레이드 풀: 상점-세부설계 §6의 5종, 런당 각 1회.
        var pool = shop.slotUpgrades.pool;
        bool onceEach = pool.Length == 5;
        foreach (var upgrade in pool)
        {
            var perk = PerkInfo.Find(table.Perk, upgrade.perkId);
            onceEach &= perk != null && perk.maxStack == 1 && upgrade.price >= 120 && upgrade.price <= 150;
        }
        Check(onceEach, "슬롯 업그레이드 풀 5종, 각 $120~150, 런당 1회");

        // 휴대 슬롯
        var loadout = new WeaponLoadout(GameConfigInfo.WeaponSlotCount(table.GameConfig));
        Check(loadout.SlotCount == 2 && loadout.FirstEmptySlot() == 0, "휴대 슬롯 2개");

        Debug.Log($"{TAG} model checks: failures={failures}");
        return failures;
    }

    private static void Check(bool _ok, string _label)
    {
        if (!_ok)
        {
            failures++;
        }
        Debug.Log($"{TAG} {(_ok ? "PASS" : "FAIL")} {_label}");
    }
}
