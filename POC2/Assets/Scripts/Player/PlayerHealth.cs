using System;
using UnityEngine;

public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(0f)] private float maxHealth;

    public float CurrentHealth { get; private set; }
    public float MaxHealth => maxHealth;
    public bool IsAlive => CurrentHealth > 0f;
    // 받는 피해 배율(방어력 스탯). 1이면 그대로 받는다.
    public float DamageTakenMultiplier { get; private set; } = 1f;

    public event Action<float, float> HealthChanged;
    public event Action Died;
    public event Action<Vector3> DamagedFrom;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    // 게임 설정 테이블의 최대 체력을 적용한다(프리팹 값은 기본값일 뿐).
    public void Configure(float _maxHealth)
    {
        maxHealth = Mathf.Max(1f, _maxHealth);
        CurrentHealth = maxHealth;
        HealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    // 최대 체력을 바꾸고 늘어난 만큼 현재 체력도 더한다. 줄어들면 현재 체력을 최대치로 자른다.
    public void SetMaxHealth(float _maxHealth)
    {
        float next = Mathf.Max(1f, _maxHealth);
        float gained = Mathf.Max(0f, next - maxHealth);
        maxHealth = next;
        if (IsAlive)
        {
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + gained);
        }
        HealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    public void SetDamageTakenMultiplier(float _multiplier)
    {
        DamageTakenMultiplier = Mathf.Clamp01(_multiplier);
    }

    public void ApplyDamage(float _amount)
    {
        ApplyDamageInternal(_amount, null);
    }

    public void ApplyDamage(float _amount, Vector3 _sourceWorldPosition)
    {
        ApplyDamageInternal(_amount, _sourceWorldPosition);
    }

    private void ApplyDamageInternal(float _amount, Vector3? _sourceWorldPosition)
    {
        if (!IsAlive || _amount <= 0f || float.IsNaN(_amount))
        {
            return;
        }

        float previous = CurrentHealth;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - _amount * DamageTakenMultiplier);
        if (CurrentHealth >= previous)
        {
            return;
        }
        HealthChanged?.Invoke(CurrentHealth, maxHealth);
        if (_sourceWorldPosition.HasValue)
        {
            DamagedFrom?.Invoke(_sourceWorldPosition.Value);
        }
        if (!IsAlive)
        {
            Died?.Invoke();
        }
    }

    public void Heal(float _amount)
    {
        if (!IsAlive || _amount <= 0f)
        {
            return;
        }

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + _amount);
        HealthChanged?.Invoke(CurrentHealth, maxHealth);
    }
}
