using System;

// Resources/ShopData.json의 직렬화 자료. 로직 없음. 조회·가격 계산은 ShopInfo가 담당한다.
// 상점은 무기(시대 누적)·파츠(무기 종속)·스탯(고정, 구매 횟수별 가격 상승)·보급(탄약 팩·체력)·슬롯 업그레이드(랜덤 진열)로 나뉜다.
[Serializable]
public sealed class ShopData
{
    // 시대. startWave부터 그 시대 무기가 진열되고 이후에도 계속 판다.
    [Serializable]
    public sealed class Era
    {
        public string id;
        public string name;
        public int startWave;
    }

    [Serializable]
    public sealed class WeaponOffer
    {
        public string weaponId;
        public int price;
    }

    // 스탯. n번째 구매 가격 = basePrice × statPriceGrowth^(이미 산 횟수), priceRounding 단위 반올림.
    // value 의미는 PlayerStats 주석 참조.
    [Serializable]
    public sealed class Stat
    {
        public string id;
        public string name;
        public string description;
        public int basePrice;
        public float value;
    }

    // 탄약 팩 1개 = rounds발, 가격 = 기준 무기 가격 × priceRatio.
    [Serializable]
    public sealed class AmmoPackSettings
    {
        public int rounds;
        public float priceRatio;
    }

    [Serializable]
    public sealed class AmmoPack
    {
        public string ammoId;
        // 가격 기준 무기. 해당 탄종 등장 시점의 최고가 무기로 고정한다(물가 안정).
        public string referenceWeaponId;
    }

    [Serializable]
    public sealed class Heal
    {
        public string label;
        public int price;
    }

    [Serializable]
    public sealed class SlotUpgrade
    {
        public string perkId;
        public int price;
    }

    [Serializable]
    public sealed class SlotUpgradeSettings
    {
        // 동시에 진열하는 칸 수.
        public int offerCount;
        public SlotUpgrade[] pool = Array.Empty<SlotUpgrade>();
    }

    public string status;
    public string note;
    public int priceRounding;
    public Era[] eras = Array.Empty<Era>();
    public WeaponOffer[] weapons = Array.Empty<WeaponOffer>();
    // 파츠 가격 = 소속 무기 가격 × partPriceRatio, priceRounding 단위 반올림.
    public float partPriceRatio;
    public float statPriceGrowth;
    public Stat[] stats = Array.Empty<Stat>();
    public AmmoPackSettings ammoPack = new AmmoPackSettings();
    public AmmoPack[] ammoPacks = Array.Empty<AmmoPack>();
    public Heal heal = new Heal();
    public SlotUpgradeSettings slotUpgrades = new SlotUpgradeSettings();
}
