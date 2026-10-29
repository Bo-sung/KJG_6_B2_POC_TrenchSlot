using UnityEngine;

// 짧게 보였다 사라지는 연출(머즐 플래시·타격 스파크). 수명이 지나면 스스로 제거한다.
public sealed class TransientEffect : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float lifetime = 0.1f;

    private float endTime;

    private void Start()
    {
        endTime = Time.time + lifetime;
    }

    private void Update()
    {
        if (Time.time >= endTime)
        {
            Destroy(gameObject);
        }
    }
}
