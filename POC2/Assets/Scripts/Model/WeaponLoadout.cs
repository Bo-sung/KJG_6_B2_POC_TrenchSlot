using System;
using System.Collections.Generic;

// 획득 기록과 들고 있는 무기. 한 번 획득한 무기는 다시 구매하지 않는다.
// 들고 있지 않은 보유 무기는 바닥의 WeaponPickup 한 개로만 존재하며, 장착 전환은 구매(이전 무기 바닥 낙하)나 픽업으로만 일어난다.
// 상점에서 보유 무기를 무료로 다시 장착하는 경로는 없다.
public sealed class WeaponLoadout
{
    private readonly HashSet<string> owned = new HashSet<string>();

    public string EquippedId { get; private set; }

    public event Action Changed;

    public WeaponLoadout(string _startWeaponId)
    {
        if (!string.IsNullOrEmpty(_startWeaponId))
        {
            owned.Add(_startWeaponId);
            EquippedId = _startWeaponId;
        }
    }

    // 한 번이라도 획득한 무기인지(들고 있거나 바닥에 있음).
    public bool IsOwned(string _id) => owned.Contains(_id);

    public bool IsEquipped(string _id) => EquippedId == _id;

    public void Own(string _id)
    {
        if (string.IsNullOrEmpty(_id) || owned.Contains(_id))
        {
            return;
        }
        owned.Add(_id);
        Changed?.Invoke();
    }

    // 보유한 무기를 들고 있는 무기로 바꾼다. 떨어뜨리기·ammo 이동은 호출 측(GameManager)이 맞춘다.
    public bool Equip(string _id)
    {
        if (!owned.Contains(_id) || EquippedId == _id)
        {
            return false;
        }
        EquippedId = _id;
        Changed?.Invoke();
        return true;
    }
}
