using System;
using UnityEngine;

public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(0f)] private float maxHealth;

    public float CurrentHealth { get; private set; }
    public float MaxHealth => maxHealth;
    public bool IsAlive => CurrentHealth > 0f;

    public event Action<float, float> HealthChanged;
    public event Action Died;

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

    public void ApplyDamage(float _amount)
    {
        if (!IsAlive || _amount <= 0f)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - _amount);
        HealthChanged?.Invoke(CurrentHealth, maxHealth);
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
