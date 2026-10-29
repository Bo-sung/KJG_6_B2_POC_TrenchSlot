using System.Collections.Generic;
using UnityEngine;

// 씬에 배치한 실제 solid와 숨는 점/노출 점. 런타임에 엄폐물은 생성하지 않는다.
public sealed class CoverNode : MonoBehaviour
{
    private static readonly List<CoverNode> ACTIVE = new List<CoverNode>();
    [SerializeField] private Collider solid;
    [SerializeField] private Transform hidden;
    [SerializeField] private Transform[] peeks;
    private Enemy owner;

    public static IReadOnlyList<CoverNode> Active => ACTIVE;
    public Enemy Owner => owner;
    public Vector3 HiddenPosition => hidden != null ? hidden.position : transform.position;
    public Collider Solid => solid;

    private void OnEnable()
    {
        if (!ACTIVE.Contains(this))
        {
            ACTIVE.Add(this);
        }
    }

    private void OnDisable()
    {
        owner = null;
        ACTIVE.Remove(this);
    }

    public bool TryReserve(Enemy _enemy)
    {
        if (owner != null && owner != _enemy && !owner.IsDead)
        {
            return false;
        }
        owner = _enemy;
        return true;
    }

    public void Release(Enemy _enemy)
    {
        if (owner == _enemy)
        {
            owner = null;
        }
    }

    public bool IsHiddenFrom(PlayerHealth _target)
    {
        if (!isActiveAndEnabled || solid == null || !solid.enabled || solid.isTrigger || hidden == null || _target == null)
        {
            return false;
        }
        // 몸통뿐 아니라 머리도 solid 뒤에 있어야 한다. 높이는 본체 기하학 기준이다.
        return SolidBetween(_target.transform.position + Vector3.up * 1.6f, HiddenPosition + Vector3.up * 1.4f) &&
            SolidBetween(_target.transform.position + Vector3.up * 1.6f, HiddenPosition + Vector3.up * 1.7f);
    }

    private bool SolidBetween(Vector3 _from, Vector3 _to)
    {
        Vector3 delta = _to - _from;
        return solid.Raycast(new Ray(_from, delta.normalized), out _, delta.magnitude);
    }

    public bool TryPeek(Enemy _enemy, PlayerHealth _target, float _range, out Vector3 _position)
    {
        _position = default;
        if (peeks == null || !IsHiddenFrom(_target))
        {
            return false;
        }
        float best = float.PositiveInfinity;
        foreach (var peek in peeks)
        {
            if (peek == null || !EnemyNavigation.TryStandPoint(peek.position, 0.3f, out Vector3 point) ||
                !_enemy.Navigation.CanReach(point) || !EnemySight.Clear(point + Vector3.up * 1.4f, _target, _range, _enemy.transform, _enemy.ShotRadius))
            {
                continue;
            }
            float distance = (point - _enemy.transform.position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                _position = point;
            }
        }
        return !float.IsPositiveInfinity(best);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        ACTIVE.Clear();
    }
}
