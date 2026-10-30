using System;
using System.Collections.Generic;

// 보유 파츠와 장착 위치. 같은 파츠를 여러 개 살 수 있고, 산 파츠 하나하나(PartInstance)가 한 번에 한 무기에만 붙는다.
// 붙어 있지 않은 보유 파츠는 보관함에 있고, 보관함에서 무료로 다시 장착한다.
// 바닥에 놓인 무기에 붙은 파츠는 그 무기와 함께 바닥에 남는다(다시 주우면 그대로 붙어 있다).
public sealed class PartInventory
{
    private readonly PartData table;
    private readonly List<PartInstance> instances = new List<PartInstance>();
    private int nextUid = 1;

    public event Action Changed;

    public PartInventory(PartData _table)
    {
        table = _table;
    }

    // 산 순서대로의 보유 파츠 전체.
    public IReadOnlyList<PartInstance> Instances => instances;

    // 새로 산 파츠 하나를 보관함에 넣는다. 테이블에 없는 id면 null이다.
    public PartInstance Add(string _partId)
    {
        if (PartInfo.Find(table, _partId) == null)
        {
            return null;
        }
        var part = new PartInstance(nextUid++, _partId);
        instances.Add(part);
        Changed?.Invoke();
        return part;
    }

    public List<PartInstance> InstancesOf(string _partId)
    {
        return instances.FindAll(_part => _part.PartId == _partId);
    }

    public int OwnedCount(string _partId)
    {
        return _partId != null ? instances.FindAll(_part => _part.PartId == _partId).Count : 0;
    }

    public int StoredCount(string _partId)
    {
        return _partId != null ? instances.FindAll(_part => _part.PartId == _partId && _part.IsStored).Count : 0;
    }

    // 무기에 붙어 있는 개수(휴대·바닥 무기 모두).
    public int AttachedCount(string _partId) => OwnedCount(_partId) - StoredCount(_partId);

    public PartInstance FirstStored(string _partId)
    {
        return instances.Find(_part => _part.PartId == _partId && _part.IsStored);
    }

    public bool Owns(PartInstance _part) => _part != null && instances.Contains(_part);

    // 파츠를 무기에 붙인다. 다른 무기에 붙어 있었다면 떼어 옮기고, 대상 무기의 같은 칸 파츠는 보관함으로 돌린다.
    public bool Attach(PartInstance _part, WeaponInstance _weapon)
    {
        var entry = _part != null ? PartInfo.Find(table, _part.PartId) : null;
        if (entry == null || _weapon == null || !Owns(_part) || entry.weaponId != _weapon.WeaponId)
        {
            return false;
        }
        if (_part.AttachedTo == _weapon)
        {
            return true;
        }
        DetachSilently(_part);
        var previous = _weapon.PartInstanceIn(entry.slot);
        if (previous != null)
        {
            previous.AttachedTo = null;
        }
        _weapon.SetPart(entry.slot, _part);
        _part.AttachedTo = _weapon;
        Changed?.Invoke();
        return true;
    }

    public bool Detach(PartInstance _part)
    {
        if (!Owns(_part) || !DetachSilently(_part))
        {
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    // ---------- 파츠 id 기준 조회(같은 파츠가 여럿이면 "그중 하나") ----------

    public bool IsOwned(string _partId) => OwnedCount(_partId) > 0;

    // 보관함에 하나라도 있는지.
    public bool IsStored(string _partId) => FirstStored(_partId) != null;

    // 이 파츠가 붙은 첫 무기. 전부 보관함에 있거나 보유하지 않았으면 null이다.
    public WeaponInstance AttachedTo(string _partId)
    {
        var part = instances.Find(_p => _p.PartId == _partId && !_p.IsStored);
        return part?.AttachedTo;
    }

    // 하나를 새로 보유한다. 중복 보유를 허용하므로 테이블에 있는 id면 항상 성공한다.
    public bool Own(string _partId) => Add(_partId) != null;

    // 보관함의 개체를 우선 붙이고, 없으면 다른 무기에 붙은 개체를 옮긴다. 이미 같은 파츠가 붙어 있으면 그대로 성공이다.
    public bool Attach(string _partId, WeaponInstance _weapon)
    {
        if (_weapon != null && _weapon.HasPart(_partId))
        {
            return true;
        }
        var part = FirstStored(_partId) ?? instances.Find(_p => _p.PartId == _partId);
        return Attach(part, _weapon);
    }

    // 붙어 있는 개체 하나를 보관함으로 돌린다.
    public bool Detach(string _partId)
    {
        return Detach(instances.Find(_p => _p.PartId == _partId && !_p.IsStored));
    }

    // 무기에 붙은 파츠 자료. 스탯 계산에 쓴다.
    public List<PartData.Entry> PartsOf(WeaponInstance _weapon)
    {
        var result = new List<PartData.Entry>();
        if (_weapon == null)
        {
            return result;
        }
        foreach (string id in _weapon.PartIds)
        {
            var part = PartInfo.Find(table, id);
            if (part != null)
            {
                result.Add(part);
            }
        }
        return result;
    }

    private bool DetachSilently(PartInstance _part)
    {
        var weapon = _part.AttachedTo;
        var entry = PartInfo.Find(table, _part.PartId);
        if (weapon == null || entry == null)
        {
            return false;
        }
        if (weapon.PartInstanceIn(entry.slot) == _part)
        {
            weapon.ClearPart(entry.slot);
        }
        _part.AttachedTo = null;
        return true;
    }
}
