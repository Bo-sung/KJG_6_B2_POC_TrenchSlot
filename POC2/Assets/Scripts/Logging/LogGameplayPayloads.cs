using System;

[Serializable]
public sealed class IntermissionStartedEvent
{
    public float intermissionSeconds;
    public int balance;
}

[Serializable]
public sealed class SlotSpinEvent
{
    public int cost;
    public int balance;
    // SpinRejected 사유. 그 밖에는 빈 문자열.
    public string reason;
}

[Serializable]
public sealed class SlotSpinStartedEvent
{
    public int cost;
    public int balance;
    public float spinSeconds;
    public int[] faces;
    // 회전 시작 시 실제 회복량(최대 체력에서 잘린 값)과 회복 후 체력.
    public float healthRestored;
    public float health;
}

[Serializable]
public sealed class SlotSpinResolvedEvent
{
    public int multiplier;
    // 배수 당첨 현금(퍽·저금통 적용).
    public int payout;
    // 현물 보상(SlotRewardInfo.Apply로 실제 지급된 계획). 현금 대체·탄약·파츠.
    public int rewardCash;
    public string rewardAmmoId;
    public int rewardAmmoAmount;
    public bool ammoRefill;
    public string[] rewardPartIds;
    public bool nearMiss;
    public float piggyMultiplier;
    public int balance;
    public int magazineAmmo;
    public int reserveAmmo;
}

[Serializable]
public sealed class ShopPurchaseEvent
{
    // Weapon · Part · Stat · Ammo · SlotUpgrade
    public string itemType;
    public string itemId;
    public bool success;
    // 실패 사유(성공이면 빈 문자열).
    public string reason;
    public int spentAmount;
    public int balanceBefore;
    public int balance;
}

[Serializable]
public sealed class WeaponChangeEvent
{
    public string weaponId;
    public int slot;
    // 교체로 바닥에 놓인 무기(없으면 빈 문자열).
    public string replacedWeaponId;
    public int magazine;
    public string reason;
}
