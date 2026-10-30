// 보유 파츠 한 개. 같은 파츠를 여러 개 살 수 있으므로 보관·장착은 파츠 종류가 아니라 이 객체 단위로 한다.
// 한 번에 한 무기에만 붙으며, 붙은 무기가 바닥에 놓이면 그 무기와 함께 바닥에 남는다.
public sealed class PartInstance
{
    public int Uid { get; }
    public string PartId { get; }
    // 붙어 있는 무기. 보관함에 있으면 null이다. 장착·해제는 PartInventory가 무기 쪽 칸과 함께 바꾼다.
    public WeaponInstance AttachedTo { get; internal set; }
    public bool IsStored => AttachedTo == null;

    internal PartInstance(int _uid, string _partId)
    {
        Uid = _uid;
        PartId = _partId;
    }
}
