using System;

// 휴대 무기 슬롯. 슬롯마다 개별 무기(WeaponInstance) 하나를 들고, 그중 하나를 사용 중으로 둔다.
// 같은 종류 무기를 여러 자루 가질 수 있다. 슬롯에서 빠진 무기는 바닥의 WeaponPickup으로만 존재한다.
// 떨어뜨리기·자리 찾기·무기 적용은 호출 측(GameManager)이 맞춘다.
// 아킴보: 두 슬롯이 같은 종류의 서로 다른 무기일 때 양손에 함께 든다. 허용 무기 종류(권총류)와 전환 시점은 호출 측이 확인한다.
// 슬롯 구성이 바뀌면(구매 교체·줍기) 아킴보는 자동으로 풀린다.
public sealed class WeaponLoadout
{
    private readonly WeaponInstance[] slots;

    public int ActiveIndex { get; private set; }
    public int SlotCount => slots.Length;
    public WeaponInstance Active => Get(ActiveIndex);
    public string ActiveWeaponId => Active != null ? Active.WeaponId : "";
    public bool Akimbo { get; private set; }
    // 아킴보 중 왼손(보조) 무기 슬롯. 아킴보가 아니면 -1이다.
    public int OffHandIndex => Akimbo ? PairIndex() : -1;
    public WeaponInstance OffHand => Get(OffHandIndex);
    // 사용 중 슬롯과 짝이 될 다른 슬롯이 같은 종류의 다른 무기인지. 무기 종류 허용 여부는 보지 않는다.
    public bool CanPairAkimbo => PairIndex() >= 0;

    public event Action Changed;

    public WeaponLoadout(int _slotCount)
    {
        slots = new WeaponInstance[Math.Max(1, _slotCount)];
    }

    public WeaponInstance Get(int _index)
    {
        return _index >= 0 && _index < slots.Length ? slots[_index] : null;
    }

    public int IndexOf(WeaponInstance _weapon)
    {
        if (_weapon == null)
        {
            return -1;
        }
        return Array.IndexOf(slots, _weapon);
    }

    public bool Contains(WeaponInstance _weapon) => IndexOf(_weapon) >= 0;

    // 첫 빈 슬롯. 가득 찼으면 -1이다.
    public int FirstEmptySlot()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                return i;
            }
        }
        return -1;
    }

    // 슬롯에 무기를 넣고 원래 있던 무기를 돌려준다. 같은 무기를 두 슬롯에 넣지 않는다.
    public WeaponInstance Put(int _index, WeaponInstance _weapon)
    {
        if (_index < 0 || _index >= slots.Length || _weapon == null || Contains(_weapon))
        {
            return null;
        }
        var previous = slots[_index];
        slots[_index] = _weapon;
        Akimbo = false;
        Changed?.Invoke();
        return previous;
    }

    // 빈 슬롯은 사용할 수 없다. 아킴보 중이면 풀고 그 슬롯 무기만 든다.
    public bool SetActive(int _index)
    {
        if (Get(_index) == null || (_index == ActiveIndex && !Akimbo))
        {
            return false;
        }
        ActiveIndex = _index;
        Akimbo = false;
        Changed?.Invoke();
        return true;
    }

    // 아킴보 켜기·끄기. 켜려면 짝이 맞아야 한다. 상태가 그대로면 false다.
    public bool SetAkimbo(bool _on)
    {
        if (_on == Akimbo || (_on && !CanPairAkimbo))
        {
            return false;
        }
        Akimbo = _on;
        Changed?.Invoke();
        return true;
    }

    // 사용 중 슬롯과 같은 종류인 다른 슬롯(슬롯 2개 기준 나머지 하나). 없으면 -1이다.
    private int PairIndex()
    {
        var active = Active;
        if (active == null)
        {
            return -1;
        }
        for (int i = 0; i < slots.Length; i++)
        {
            if (i != ActiveIndex && slots[i] != null && slots[i] != active && slots[i].WeaponId == active.WeaponId)
            {
                return i;
            }
        }
        return -1;
    }

    // 장전탄·파츠 변경처럼 슬롯 구성 밖의 변화를 화면에 알릴 때 쓴다.
    public void NotifyChanged()
    {
        Changed?.Invoke();
    }
}
