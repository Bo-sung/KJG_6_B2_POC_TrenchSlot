// 총알 적중·피해를 받을 수 있는 대상. 콜라이더의 부모 중 하나가 구현하면 된다.
public interface IDamageable
{
    void TakeDamage(float _amount);
}
