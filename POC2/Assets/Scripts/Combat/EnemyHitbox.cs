using UnityEngine;

// 적의 피격 부위(몸통·머리·헬멧). 프리팹 자식 콜라이더에 붙여 부위별 판정을 명시하거나,
// 런타임에 기본 콜라이더를 보조하는 용도로 쓴다.
public sealed class EnemyHitbox : MonoBehaviour
{
    [SerializeField] private HitZone zone = HitZone.Body;

    public HitZone Zone => zone;

    public void Configure(HitZone _zone)
    {
        zone = _zone;
    }
}
