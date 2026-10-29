using UnityEngine;

// 탄과 동일하게 자기 몸/아군/트리거를 제외한다. 사거리와 탄 반지름을 포함한 LOS다.
public static class EnemySight
{
    public static Vector3 TargetCenter(PlayerHealth _target)
    {
        var controller = _target.GetComponent<CharacterController>();
        return controller != null ? controller.bounds.center : _target.transform.position;
    }

    public static bool Clear(Vector3 _origin, PlayerHealth _target, float _range, Transform _owner, float _radius)
    {
        if (_target == null || !_target.IsAlive)
        {
            return false;
        }
        Vector3 delta = TargetCenter(_target) - _origin;
        if (delta.magnitude > _range || delta.sqrMagnitude < 0.0001f)
        {
            return false;
        }
        return ClearSegment(_origin, TargetCenter(_target), _target, _owner, _radius);
    }

    public static bool ClearSegment(Vector3 _origin, Vector3 _destination, PlayerHealth _target, Transform _owner, float _radius)
    {
        Vector3 delta = _destination - _origin;
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
