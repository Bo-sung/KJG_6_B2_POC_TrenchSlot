using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// 웨이브의 적 스폰 순서와 생존 추적. 웨이브 시간은 GameManager가 관리하고, 여기서는 전멸 여부만 알린다.
// 스폰은 gameplay 객체 생성이므로 런타임 Instantiate를 쓴다(작업지침 §3 예외).
public sealed class WaveController : MonoBehaviour
{
    private const float HEAVY_TINT_SCALE = 0.6f;

    [SerializeField] private GameObject enemyPrefab;
    // Authored entrances are tried round-robin, but only current-view-safe reachable candidates may spawn.
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField, Min(0f)] private float spawnJitter;

    private readonly List<Enemy> alive = new List<Enemy>();
    private readonly Queue<string> pending = new Queue<string>();
    private EnemyData enemyTable;
    private EnemyWeaponData enemyWeaponTable;
    private string enemyWeaponId;
    private PlayerHealth target;
    private Color tint;
    private float hpMultiplier;
    private float spawnInterval;
    private float spawnTimer;
    private int spawnIndex;
    private bool running;
    private bool clearRaised;

    public int AliveCount => alive.Count;
    public bool AllSpawned => pending.Count == 0;
    public bool IsRunning => running;
    public int PendingCount => pending.Count;

    public event Action<Enemy> EnemyKilled;
    public event Action Cleared;

    public void Begin(WaveSpec _spec, EnemyData _enemyTable, EnemyWeaponData _enemyWeaponTable, PlayerHealth _target)
    {
        Stop();
        enemyTable = _enemyTable;
        enemyWeaponTable = _enemyWeaponTable;
        enemyWeaponId = _spec.enemyWeapon;
        target = _target;
        tint = _spec.tint;
        hpMultiplier = _spec.hpMultiplier;
        spawnInterval = Mathf.Max(0.1f, _spec.spawnInterval);
        BuildQueue(_spec);
        spawnTimer = 0f;
        spawnIndex = 0;
        clearRaised = false;
        running = true;
    }

    // 종류를 번갈아 쌓아 한 가지 적만 연속으로 나오지 않게 한다.
    private void BuildQueue(WaveSpec _spec)
    {
        pending.Clear();
        int rushers = _spec.rushers;
        int riflemen = _spec.riflemen;
        int heavies = _spec.heavies;
        while (rushers > 0 || riflemen > 0 || heavies > 0)
        {
            if (rushers > 0)
            {
                pending.Enqueue(EnemyInfo.RUSHER_ID);
                rushers--;
            }
            if (riflemen > 0)
            {
                pending.Enqueue(EnemyInfo.RIFLEMAN_ID);
                riflemen--;
            }
            if (heavies > 0)
            {
                pending.Enqueue(EnemyInfo.HEAVY_ID);
                heavies--;
            }
        }
    }

    private void Update()
    {
        if (!running)
        {
            return;
        }

        if (pending.Count > 0)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                bool spawned = Spawn(pending.Peek());
                if (spawned)
                {
                    pending.Dequeue();
                }
                spawnTimer = spawned ? spawnInterval : TableManager.Instance.EnemyAi.spawnRetrySeconds;
            }
        }

        if (AllSpawned && alive.Count == 0 && !clearRaised)
        {
            clearRaised = true;
            Cleared?.Invoke();
        }
    }

    // 남은 적을 모두 치운다(시간 종료·게임 오버). 이때 처치 보상은 주지 않는다.
    // 날아가던 탄도 같이 치운다(웨이브 종료·정비·게임 오버 시 발사체가 남지 않게 한다).
    public void Stop()
    {
        running = false;
        pending.Clear();
        Projectile.CancelAll();
        foreach (var enemy in alive)
        {
            if (enemy == null)
            {
                continue;
            }
            enemy.Died -= OnEnemyDied;
            enemy.StopCombat();
            Destroy(enemy.gameObject);
        }
        alive.Clear();
    }

    private bool Spawn(string _kind)
    {
        if (enemyPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            return false;
        }

        if (!TryChooseSpawn(out Vector3 spawn))
        {
            return false;
        }
        Vector3 facing = target.transform.position - spawn;
        facing.y = 0f;
        GameObject instance = Instantiate(enemyPrefab, spawn, Quaternion.LookRotation(facing), transform);
        Enemy enemy = instance.GetComponent<Enemy>();
        if (enemy == null)
        {
            Debug.LogError("WaveController: 적 프리팹에 Enemy 컴포넌트가 없습니다.");
            Destroy(instance);
            return false;
        }

        var stats = EnemyInfo.Find(enemyTable, _kind);
        var weapon = EnemyWeaponInfo.ForEnemy(stats, enemyWeaponTable, enemyWeaponId);
        Color color = _kind == EnemyInfo.HEAVY_ID
            ? new Color(tint.r * HEAVY_TINT_SCALE, tint.g * HEAVY_TINT_SCALE, tint.b * HEAVY_TINT_SCALE, 1f)
            : tint;
        enemy.Configure(stats, weapon, hpMultiplier, color, target);
        enemy.Died += OnEnemyDied;
        alive.Add(enemy);
        return true;
    }

    // Check the final sampled/jittered point, never just its authored anchor. No valid point means retry, not loss.
    public bool TryChooseSpawn(out Vector3 _spawn)
    {
        _spawn = default;
        if (target == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            return false;
        }
        var tuning = TableManager.Instance.EnemyAi;
        var view = target.GetComponentInChildren<Camera>();
        Vector3 forward = view != null ? view.transform.forward : target.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = target.transform.forward;
            forward.y = 0f;
        }
        if (forward.sqrMagnitude < 0.0001f)
        {
            return false;
        }
        forward.Normalize();
        if (!NavMesh.SamplePosition(target.transform.position, out NavMeshHit destination, tuning.navSampleDistance, NavMesh.AllAreas))
        {
            return false;
        }
        var path = new NavMeshPath();
        for (int offset = 0; offset < spawnPoints.Length; offset++)
        {
            int index = (spawnIndex + offset) % spawnPoints.Length;
            var point = spawnPoints[index];
            if (point == null)
            {
                continue;
            }
            for (int attempt = 0; attempt <= tuning.spawnAttemptsPerPoint; attempt++)
            {
                // Last attempt uses the anchor, but it must pass exactly the same policy.
                Vector3 jitter = attempt == tuning.spawnAttemptsPerPoint ? Vector3.zero : new Vector3(
                    UnityEngine.Random.Range(-spawnJitter, spawnJitter), 0f, UnityEngine.Random.Range(-spawnJitter, spawnJitter));
                if (!EnemyNavigation.TryStandPoint(point.position + jitter, tuning.navSampleDistance, out Vector3 candidate) ||
                    !IsInSpawnFront(candidate, target.transform.position, forward, tuning) ||
                    !NavMesh.CalculatePath(candidate, destination.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }
                bool occupied = false;
                foreach (var collider in Physics.OverlapCapsule(candidate + Vector3.up * EnemyNavigation.RADIUS,
                    candidate + Vector3.up * (EnemyNavigation.HEIGHT - EnemyNavigation.RADIUS),
                    EnemyNavigation.RADIUS + tuning.spawnOccupancyPadding, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    // Terrain was checked by TryStandPoint; additionally reject live character occupancy.
                    if (collider.GetComponentInParent<Enemy>() != null || collider.GetComponentInParent<PlayerHealth>() != null)
                    {
                        occupied = true;
                        break;
                    }
                }
                if (!occupied)
                {
                    _spawn = candidate;
                    spawnIndex = (index + 1) % spawnPoints.Length;
                    return true;
                }
            }
        }
        return false;
    }

    public static bool IsInSpawnFront(Vector3 _candidate, Vector3 _player, Vector3 _forward, EnemyAiData _tuning)
    {
        Vector3 delta = _candidate - _player;
        delta.y = 0f;
        _forward.y = 0f;
        return delta.sqrMagnitude >= _tuning.spawnMinDistance * _tuning.spawnMinDistance &&
            _forward.sqrMagnitude > 0.0001f && Vector3.Dot(delta.normalized, _forward.normalized) >= _tuning.spawnForwardDot;
    }

    private void OnEnemyDied(Enemy _enemy)
    {
        _enemy.Died -= OnEnemyDied;
        if (!alive.Remove(_enemy))
        {
            return;
        }
        EnemyKilled?.Invoke(_enemy);
    }
}
