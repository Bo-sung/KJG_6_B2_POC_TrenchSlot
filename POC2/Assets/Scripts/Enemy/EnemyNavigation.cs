using UnityEngine;
using UnityEngine.AI;

// NavMesh는 에디터에서 굽는다. 경로가 없으면 멈추며 직선 transform 이동으로 폴백하지 않는다.
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyNavigation : MonoBehaviour
{
    // 본체 CapsuleCollider와 같은 치수. 기하학 안전 여유이며 전투 밸런스 값이 아니다.
    public const float RADIUS = 0.4f;
    public const float HEIGHT = 1.8f;
    private const float SKIN = 0.03f;
    private NavMeshAgent agent;
    private Vector3 destination;
    private Vector3 lastProgressPosition;
    private float lastProgressTime;
    private float nextPathTime;
    private bool moving;
    private EnemyAiData tuning;
    private NavMeshPath path;

    // isOnNavMesh는 surface 제거 직후 한 프레임 이상 stale일 수 있으므로 실제 지면도 확인한다.
    public bool Ready => agent != null && agent.enabled && agent.isOnNavMesh &&
        NavMesh.SamplePosition(transform.position, out _, 0.3f, agent.areaMask);
    public bool IsStuck => moving && Time.time - lastProgressTime > tuning.stuckSeconds;

    public void Configure(float _speed, EnemyAiData _tuning)
    {
        tuning = _tuning;
        path = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        agent.speed = _speed;
        agent.updateRotation = false;
        agent.updatePosition = false;
        agent.radius = RADIUS;
        agent.height = HEIGHT;
        agent.stoppingDistance = tuning.arrivalDistance;
        lastProgressPosition = transform.position;
        lastProgressTime = Time.time;
    }

    public bool CanReach(Vector3 _position)
    {
        return Ready && TryStandPoint(_position, 0.3f, out Vector3 point) &&
            agent.CalculatePath(point, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    public bool MoveTo(Vector3 _position)
    {
        if (!Ready)
        {
            return false;
        }
        if (moving && (destination - _position).sqrMagnitude < 0.04f && Time.time < nextPathTime)
        {
            return true;
        }
        if (!TryStandPoint(_position, tuning.navSampleDistance, out Vector3 point) ||
            !agent.CalculatePath(point, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            Stop();
            return false;
        }
        if (!moving)
        {
            lastProgressPosition = transform.position;
            lastProgressTime = Time.time;
        }
        destination = _position;
        nextPathTime = Time.time + tuning.pathRefreshSeconds;
        moving = true;
        agent.isStopped = false;
        return agent.SetPath(path);
    }

    // Agent가 계산한 우회 이동만 적용하고, 추가된 동적 solid도 capsule sweep으로 차단한다.
    public void TickMotion()
    {
        if (!Ready || !moving)
        {
            return;
        }
        Vector3 delta = agent.nextPosition - transform.position;
        float length = delta.magnitude;
        if (length > 0.0001f)
        {
            float allowed = length;
            foreach (var hit in Physics.CapsuleCastAll(transform.position + Vector3.up * RADIUS,
                transform.position + Vector3.up * (HEIGHT - RADIUS), RADIUS - SKIN,
                delta / length, length + SKIN, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsSolid(hit.collider))
                {
                    allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - SKIN));
                }
            }
            transform.position += delta / length * allowed;
            agent.nextPosition = transform.position;
        }
        if ((transform.position - lastProgressPosition).sqrMagnitude > 0.04f)
        {
            lastProgressPosition = transform.position;
            lastProgressTime = Time.time;
        }
    }

    public void Stop()
    {
        moving = false;
        if (Ready)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.nextPosition = transform.position;
        }
    }

    public static bool TryStandPoint(Vector3 _position, float _sampleDistance, out Vector3 _point)
    {
        _point = _position;
        if (!NavMesh.SamplePosition(_position, out NavMeshHit hit, _sampleDistance, NavMesh.AllAreas))
        {
            return false;
        }
        _point = hit.position;
        foreach (var collider in Physics.OverlapCapsule(_point + Vector3.up * (RADIUS + SKIN),
            _point + Vector3.up * (HEIGHT - RADIUS), RADIUS - SKIN,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (IsSolid(collider))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSolid(Collider _collider)
    {
        return _collider != null && _collider.GetComponentInParent<Enemy>() == null &&
            _collider.GetComponentInParent<PlayerHealth>() == null;
    }
}
