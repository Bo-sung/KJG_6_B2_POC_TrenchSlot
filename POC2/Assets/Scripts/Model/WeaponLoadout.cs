using System;
using System.Collections.Generic;

// 보유 무기와 장착 무기. 구매는 한 번, 장착 전환은 무료다.
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

    // 보유한 무기만 장착할 수 있다.
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
