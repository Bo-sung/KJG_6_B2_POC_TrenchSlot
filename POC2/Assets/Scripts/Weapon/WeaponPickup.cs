using UnityEngine;

// 바닥에 놓인 개별 무기 한 자루. 무기 객체(WeaponInstance)를 그대로 들고 있어 장전탄과 파츠가 유지된다.
// 상호작용(E) 대상이 아니다. 픽업 키로만 주우며, 줍는 순간 한 번만 소비된다(GameManager.TryPickup 재검증).
// 바닥 무기는 웨이브가 지나도 남고, 사망 시에만 치운다.
public sealed class WeaponPickup : MonoBehaviour
{
    // 프리팹 기본 무기 id. 실제 무기는 Initialize로 받은 WeaponInstance가 정한다.
    [SerializeField] private string weaponId;

    private WeaponInstance weapon;
    private string displayName = "";
    private bool consumed;

    public WeaponInstance Weapon => weapon;
    public string WeaponId => weapon != null ? weapon.WeaponId : weaponId;
    public string DisplayName => displayName;
    public int Ammo => weapon != null ? weapon.Magazine : 0;

    // 줍기에 소비되어 재사용 불가함을 뜻한다. 같은 프레임 연속 swap 중복 방지용.
    public bool IsConsumed => consumed;

    public void Initialize(WeaponInstance _weapon, string _displayName)
    {
        weapon = _weapon;
        displayName = _displayName ?? "";
        consumed = false;
    }

    // 줍기 성공 시 호출. 소비 표시·콜라이더 끄기·비활성화를 즉시 해서 같은 프레임의 조준·재줍기에 다시 걸리지 않게 하고,
    // 파괴는 프레임 끝에 한다. 이미 소비된 픽업은 false를 돌려 이중 소비를 막는다.
    public bool Consume()
    {
        if (consumed)
        {
            return false;
        }
        consumed = true;
        foreach (Collider part in GetComponentsInChildren<Collider>(true))
        {
            part.enabled = false;
        }
        gameObject.SetActive(false);
        Destroy(gameObject);
        return true;
    }
}
