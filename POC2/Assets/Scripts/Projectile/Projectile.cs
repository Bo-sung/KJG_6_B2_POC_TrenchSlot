using System;
using System.Collections.Generic;
using UnityEngine;

// 날아가는 발사체. 매 스텝 이전 위치에서 다음 위치까지 구간을 스윕 캐스트해, 실제로 닿은 순간에만 피해를 준다.
// 발사 즉시 피해는 없다. 가까운 순서로 맞은 대상 하나만 결정하고, 그 뒤로는 더 이상 처리하지 않는다(정확히 한 번).
// 발사 시점의 수치는 Shot 스냅샷으로 복사해 두므로, 발사 뒤 무기를 바꿔도 날아가는 탄은 바뀌지 않는다.
public sealed class Projectile : MonoBehaviour
{
    public enum Team
    {
        Player,
        Enemy
    }

    // 발사 스냅샷. 값은 발사한 무기(또는 적 무기)의 테이블에서 온다.
    public struct Shot
    {
        public Team team;
        public float damage;
        public float speed;
        // 최대 비행 거리(m). 이 거리를 넘으면 소멸한다.
        public float maxDistance;
        // 최대 비행 시간(s). 속도가 0이거나 스텝이 멈춰도 결국 소멸시키는 안전장치.
        public float lifetime;
        // 스윕 캐스트 반경(m). 얇은 장애물을 놓치지 않을 정도로 작게 둔다.
        public float radius;

        // 전투 확장 (저지력·넉백·산탄 그룹)
        public float stoppingPower;
        public float knockback;
        public int pelletGroup;
        public int pelletCount;
        public float pelletStopCap;
        public LogShotContext logContext;
        public string projectileId;
        public LogAttackSource attackSource;
    }

    // 충돌 결과. 피격 대상이 적이고 실제로 피해가 들어갔으면 damagedEnemy가 true다.
    public struct Impact
    {
        public Team team;
        public Vector3 point;
        public Vector3 normal;
        public bool damagedEnemy;
        public bool killed;
        public LogShotContext logContext;
        public string projectileId;
        public string enemyTypeId;
        public string targetId;
        public float actualEnemyDamage;

        // 전투 피드백 확장
        public bool isHeadshot;
        public bool isHelmetHit;
        public float damageDealt;
        public float stoppingApplied;
        public bool staggered;
        public bool knockedBack;
    }

    // 발사 중인 탄 목록. 페이즈 종료·게임 오버 때 CancelAll로 한 번에 치운다.
    private static readonly List<Projectile> active = new List<Projectile>();

    private Shot shot;
    private Vector3 direction;
    private Vector3 shotOrigin;
    private Transform owner;
    private LayerMask mask;
    private float traveled;
    private float age;
    private bool resolved;

    public Shot Snapshot => shot;
    public bool IsResolved => resolved;
    public static int ActiveCount => active.Count;

    public event Action<Impact> Impacted;

    // 탄을 발사 상태로 만든다. 등록만 하며 첫 이동은 다음 Step에서 일어난다(발사 즉시 피해 없음).
    public void Launch(Shot _shot, Vector3 _origin, Vector3 _direction, LayerMask _mask, Transform _owner)
    {
        shot = _shot;
        shotOrigin = _origin;
        direction = _direction.normalized;
        mask = _mask;
        owner = _owner;
        traveled = 0f;
        age = 0f;
        resolved = false;
        transform.position = _origin;
        transform.rotation = Quaternion.LookRotation(direction);
        active.Add(this);
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    // 한 스텝 진행한다. 테스트에서도 같은 경로를 쓰도록 public이다.
    public void Step(float _dt)
    {
        if (resolved)
        {
            return;
        }

        age += _dt;
        if (age >= shot.lifetime)
        {
            Finish();
            return;
        }

        float remaining = shot.maxDistance - traveled;
        if (remaining <= 0f)
        {
            Finish();
            return;
        }

        float travel = Mathf.Min(shot.speed * _dt, remaining);
        if (travel <= 0f)
        {
            return;
        }

        Vector3 start = transform.position;
        if (SweepAndResolve(start, travel))
        {
            return;
        }

        traveled += travel;
        transform.position = start + direction * travel;
        if (traveled >= shot.maxDistance)
        {
            Finish();
        }
    }

    // 이번 구간에서 가장 먼저 닿는 대상 하나를 찾아 처리한다. 닿았으면 true.
    private bool SweepAndResolve(Vector3 _start, float _distance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(_start, shot.radius, direction, _distance, mask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (_a, _b) => _a.distance.CompareTo(_b.distance));
        foreach (RaycastHit sweepHit in hits)
        {
            if (sweepHit.collider == null || IsOwnerPart(sweepHit.transform))
            {
                continue;
            }
            RaycastHit hit = FixOverlapHit(sweepHit, _start);

            Enemy enemy = hit.collider.GetComponentInParent<Enemy>();
            PlayerHealth player = hit.collider.GetComponentInParent<PlayerHealth>();

            if (shot.team == Team.Player)
            {
                if (enemy != null)
                {
                    if (enemy.IsDead)
                    {
                        continue;
                    }
                    float healthBefore = enemy.Health;
                    string enemyTypeId = enemy.KindId;
                    string targetId = enemy.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    CombatHitResult combatResult = enemy.TakeHit(shot, hit, shotOrigin);
                    float actualDamage = Mathf.Max(0f, healthBefore - Mathf.Max(0f, enemy.Health));
                    Resolve(hit, true, enemy.IsDead, combatResult, targetId, enemyTypeId, actualDamage);
                    return true;
                }
            }
            else
            {
                // 적 탄은 같은 편 적을 통과한다. 플레이어 몸에만 피해를 준다.
                if (enemy != null)
                {
                    continue;
                }
                if (player != null)
                {
                    if (player.IsAlive)
                    {
                        player.ApplyDamage(shot.damage, shotOrigin, shot.attackSource);
                    }
                    Resolve(hit, false, false);
                    return true;
                }
            }

            // 적 탄이 적을, 플레이어 탄이 플레이어를 맞히는 경우는 위에서 걸렀다. 남은 콜라이더는 지형·엄폐물이다.
            Resolve(hit, false, false);
            return true;
        }
        return false;
    }

    // 스윕 시작부터 겹친 콜라이더는 point가 (0,0,0)·distance 0으로 온다. 시작점에서 가장 가까운 표면점과 진행 반대 법선으로 바꾼다.
    // 볼록하지 않은 MeshCollider는 ClosestPoint를 쓸 수 없으므로 시작점을 쓴다.
    private RaycastHit FixOverlapHit(RaycastHit _hit, Vector3 _start)
    {
        if (_hit.distance > 0f || _hit.point != Vector3.zero)
        {
            return _hit;
        }
        var mesh = _hit.collider as MeshCollider;
        _hit.point = mesh != null && !mesh.convex ? _start : _hit.collider.ClosestPoint(_start);
        _hit.normal = -direction;
        return _hit;
    }

    private bool IsOwnerPart(Transform _part)
    {
        return owner != null && _part.IsChildOf(owner);
    }

    private void Resolve(RaycastHit _hit, bool _damagedEnemy, bool _killed, CombatHitResult _combatResult = default,
        string _targetId = null, string _enemyTypeId = null, float _actualEnemyDamage = 0f)
    {
        resolved = true;
        EffectSpawner.Spawn(EffectSpawner.HIT_SPARK, _hit.point, Quaternion.LookRotation(_hit.normal));
        var impact = new Impact
        {
            team = shot.team,
            point = _hit.point,
            normal = _hit.normal,
            damagedEnemy = _damagedEnemy,
            killed = _killed,
            logContext = shot.logContext,
            projectileId = shot.projectileId,
            targetId = _targetId,
            enemyTypeId = _enemyTypeId,
            actualEnemyDamage = _actualEnemyDamage,
            isHeadshot = _combatResult.isHeadshot,
            isHelmetHit = _combatResult.isHelmetHit,
            damageDealt = _combatResult.damageDealt,
            stoppingApplied = _combatResult.stoppingApplied,
            staggered = _combatResult.staggered,
            knockedBack = _combatResult.knockedBack
        };
        shot.logContext?.Impact(new LogCombatImpact
        {
            projectileId = shot.projectileId,
            hitEnemy = _damagedEnemy,
            killed = _killed,
            targetId = _targetId,
            enemyTypeId = _enemyTypeId,
            actualDamage = _actualEnemyDamage,
            point = new LogPosition3 { x = _hit.point.x, y = _hit.point.y, z = _hit.point.z }
        });
        Impacted?.Invoke(impact);
        Finish();
    }

    // 소멸(명중·사거리·수명). 소멸 자체는 피해·이벤트를 만들지 않는다.
    private void Finish()
    {
        resolved = true;
        shot.logContext?.ProjectileEnded(shot.projectileId, "resolved_or_expired");
        active.Remove(this);
        Discard(gameObject);
    }

    // 플레이 중에는 Destroy, 편집 모드 검증(에디터 배치)에서는 DestroyImmediate를 쓴다. 편집 모드에서 Destroy는 실패한다.
    private static void Discard(GameObject _go)
    {
        if (Application.isPlaying)
        {
            Destroy(_go);
        }
        else
        {
            DestroyImmediate(_go);
        }
    }

    // 모든 탄을 즉시 치운다. 웨이브 종료·정비·게임 오버에서 호출한다. 피해·이벤트 없음.
    public static void CancelAll()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Projectile projectile = active[i];
            active.RemoveAt(i);
            if (projectile != null)
            {
                projectile.resolved = true;
                projectile.shot.logContext?.ProjectileEnded(projectile.shot.projectileId, "cancelled");
                Discard(projectile.gameObject);
            }
        }
        active.Clear();
    }
}
