using System;
using System.Collections.Generic;
using UnityEngine;

// 웨이브의 적 스폰 순서와 생존 추적. 웨이브 시간은 GameManager가 관리하고, 여기서는 전멸 여부만 알린다.
// 스폰은 gameplay 객체 생성이므로 런타임 Instantiate를 쓴다(작업지침 §3 예외).
public sealed class WaveController : MonoBehaviour
{
    private const float HEAVY_TINT_SCALE = 0.6f;

    [SerializeField] private GameObject enemyPrefab;
    // 좌우 참호 입구. 스폰 지점을 번갈아 쓴다.
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
                Spawn(pending.Dequeue());
                spawnTimer = spawnInterval;
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

    private void Spawn(string _kind)
    {
        if (enemyPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("WaveController: 적 프리팹 또는 스폰 지점이 연결되지 않았습니다.");
            pending.Clear();
            return;
        }

        Transform point = spawnPoints[spawnIndex % spawnPoints.Length];
        spawnIndex++;
        Vector3 jitter = new Vector3(
            UnityEngine.Random.Range(-spawnJitter, spawnJitter),
            0f,
            UnityEngine.Random.Range(-spawnJitter, spawnJitter));
        // 스폰 흔들림이 참호 벽 안으로 들어가면 가장 가까운 안전한 NavMesh 점을 쓴다.
        if (!EnemyNavigation.TryStandPoint(point.position + jitter, TableManager.Instance.EnemyAi.navSampleDistance, out Vector3 spawn) &&
            !EnemyNavigation.TryStandPoint(point.position, TableManager.Instance.EnemyAi.navSampleDistance, out spawn))
        {
            Debug.LogWarning("WaveController: 안전한 NavMesh 스폰 위치가 없어 이번 적을 건너뜁니다.");
            return;
        }
        GameObject instance = Instantiate(enemyPrefab, spawn, point.rotation, transform);
        Enemy enemy = instance.GetComponent<Enemy>();
        if (enemy == null)
        {
            Debug.LogError("WaveController: 적 프리팹에 Enemy 컴포넌트가 없습니다.");
            Destroy(instance);
            return;
        }

        var stats = EnemyInfo.Find(enemyTable, _kind);
        var weapon = EnemyWeaponInfo.ForEnemy(stats, enemyWeaponTable, enemyWeaponId);
        Color color = _kind == EnemyInfo.HEAVY_ID
            ? new Color(tint.r * HEAVY_TINT_SCALE, tint.g * HEAVY_TINT_SCALE, tint.b * HEAVY_TINT_SCALE, 1f)
            : tint;
        enemy.Configure(stats, weapon, hpMultiplier, color, target);
        enemy.Died += OnEnemyDied;
        alive.Add(enemy);
    }

    private void OnEnemyDied(Enemy _enemy)
    {
        _enemy.Died -= OnEnemyDied;
        alive.Remove(_enemy);
        EnemyKilled?.Invoke(_enemy);
    }
}
