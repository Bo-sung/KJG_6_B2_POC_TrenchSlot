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
public sealed class WeaponChangeEvent
{
    public string weaponId;
    public int slot;
    // 교체로 바닥에 놓인 무기(없으면 빈 문자열).
    public string replacedWeaponId;
    public int magazine;
    public string reason;
}
