using UnityEngine;

// 탄과 동일하게 자기 몸/아군/트리거를 제외한다. 사거리와 탄 반지름을 포함한 LOS다.
public static class EnemySight
{
    public static bool Clear(Vector3 _origin, PlayerHealth _target, float _range, Transform _owner, float _radius)
    {
        if (_target == null || !_target.IsAlive)
        {
            return false;
        }
        Vector3 delta = _target.transform.position + Vector3.up - _origin;
        if (delta.magnitude > _range || delta.sqrMagnitude < 0.0001f)
        {
            return false;
        }
        // SphereCast는 시작 겹침을 보고하지 않으므로 총구가 solid 안이면 발사하지 않는다.
        foreach (var overlap in Physics.OverlapSphere(_origin, Mathf.Max(0.01f, _radius), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (Blocks(overlap, _target, _owner))
            {
                return false;
            }
        }
        foreach (var hit in Physics.SphereCastAll(_origin, Mathf.Max(0.01f, _radius), delta.normalized, delta.magnitude,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (Blocks(hit.collider, _target, _owner))
            {
                return false;
            }
        }
        return true;
    }

    private static bool Blocks(Collider _collider, PlayerHealth _target, Transform _owner)
    {
        return _collider != null && (_owner == null || !_collider.transform.IsChildOf(_owner)) &&
            _collider.GetComponentInParent<Enemy>() == null && _collider.GetComponentInParent<PlayerHealth>() != _target;
    }
}
