using System.Collections.Generic;
using System.Linq;

// 개별 무기 한 자루. 같은 종류를 여러 자루 가질 수 있으므로 장전탄과 장착 파츠는 종류가 아니라 이 객체에 속한다.
// 휴대 슬롯·바닥 어디에 있든 같은 객체를 옮기며, 바닥에 놓였다 다시 주워도 장전탄과 파츠가 그대로 남는다.
public sealed class WeaponInstance
{
    // 장착 칸 → 파츠 개체. 칸마다 하나만 붙는다. 실제 장착·해제는 PartInventory가 양쪽 기록을 맞춰 가며 바꾼다.
    private readonly Dictionary<string, PartInstance> parts = new Dictionary<string, PartInstance>();

    public string WeaponId { get; }
    // 장전탄. 공유 예비탄(AmmoInventory)에 포함되지 않는다.
    public int Magazine { get; set; }
    // 다음에 쏠 수 있는 시각(Time.time). 무기마다 따로 두어 슬롯 전환·아킴보 전환으로 연사 간격이 초기화되지 않게 한다.
    public float NextShotTime { get; set; }

    public WeaponInstance(string _weaponId, int _magazine)
    {
        WeaponId = _weaponId;
        Magazine = _magazine < 0 ? 0 : _magazine;
    }

    public IEnumerable<PartInstance> Parts => parts.Values;

    public IEnumerable<string> PartIds => parts.Values.Select(_part => _part.PartId);

    public int PartCount => parts.Count;

    public PartInstance PartInstanceIn(string _slot)
    {
        return _slot != null && parts.TryGetValue(_slot, out var part) ? part : null;
    }

    // 칸에 붙은 파츠 id. 비어 있으면 null이다.
    public string PartIn(string _slot)
    {
        return PartInstanceIn(_slot)?.PartId;
    }

    public bool HasPart(string _partId)
    {
        if (_partId == null)
        {
            return false;
        }
        foreach (var part in parts.Values)
        {
            if (part.PartId == _partId)
            {
                return true;
            }
        }
        return false;
    }

    internal void SetPart(string _slot, PartInstance _part)
    {
        parts[_slot] = _part;
    }

    internal void ClearPart(string _slot)
    {
        parts.Remove(_slot);
    }
}
