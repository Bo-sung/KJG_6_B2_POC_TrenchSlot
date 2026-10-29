using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// 실제 PlayerLoop에서 NavMeshAgent/Enemy.Update/Projectile.Update를 진행하는 CLI 검증.
// -batchmode -nographics -executeMethod PocCoverVerification.RunPlayChecks (NOT -quit)
[InitializeOnLoad]
public static class PocCoverVerification
{
    private const string RUNNING = "PocCoverVerification.running";
    private const string TAG = "[PocCoverVerification]";
    private static readonly Stack<IEnumerator> STEPS = new Stack<IEnumerator>();
    private static readonly List<Enemy> ENEMIES = new List<Enemy>();
    private static int failures;
    private static int passes;
    private static int lastFrame = -1;
    private static bool initialized;
    private static bool sawProjectile;
    private static bool collisionReported;
    private static float deadline;
    private static PlayerHealth player;
    private static Enemy observed;
    private static Enemy.CombatState lastState;

    static PocCoverVerification()
    {
        EditorApplication.update += Tick;
    }

    public static void RunPlayChecks()
    {
        EditorSceneManager.OpenScene(PocCoverAuthoring.SCENE);
        var surface = UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>();
        Check(surface != null && surface.navMeshData != null && AssetDatabase.GetAssetPath(surface.navMeshData) == PocCoverAuthoring.NAV_DATA,
            "saved Game references persisted baked NavMesh data");
        var wave = UnityEngine.Object.FindFirstObjectByType<WaveController>();
        var so = new SerializedObject(wave);
        Check(so.FindProperty("enemyPrefab").objectReferenceValue != null && so.FindProperty("spawnPoints").arraySize >= 2,
            "saved wave prefab and both spawn references preserved");
        foreach (var name in new[] { "SpawnLeft", "SpawnRight" })
        {
            Check(EnemyNavigation.TryStandPoint(GameObject.Find(name).transform.position, 0.3f, out _), name + " is safe on baked navigation");
        }
        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour is GameManager || behaviour is HudPresenter || behaviour is ShopPresenter || behaviour is GameOverPresenter)
            {
                behaviour.gameObject.SetActive(false);
            }
            else if (behaviour is PlayerMovement || behaviour is PlayerWeapon || behaviour is PlayerInteractor || behaviour is WaveController)
            {
                behaviour.enabled = false;
            }
        }
        SessionState.SetInt("cover.preFailures", failures);
        SessionState.SetInt("cover.prePasses", passes);
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(RUNNING, false) || !EditorApplication.isPlaying || Time.frameCount == lastFrame)
        {
            return;
        }
        lastFrame = Time.frameCount;
        try
        {
            if (!initialized)
            {
                initialized = true;
                // 실제 PlayerLoop는 유지하고 프레임당 시간을 60Hz로 고정한다.
                Time.captureDeltaTime = 1f / 60f;
                failures = SessionState.GetInt("cover.preFailures", 0);
                passes = SessionState.GetInt("cover.prePasses", 0);
                deadline = Time.realtimeSinceStartup + 180f;
                Application.logMessageReceived += OnLog;
                STEPS.Push(Scenarios());
            }
            if (Time.realtimeSinceStartup > deadline)
            {
                throw new TimeoutException("Real PlayerLoop cover checks exceeded 180 seconds.");
            }
            Observe();
            while (STEPS.Count > 0)
            {
                IEnumerator step = STEPS.Peek();
                if (!step.MoveNext())
                {
                    STEPS.Pop();
                    continue;
                }
                if (step.Current is IEnumerator nested)
                {
                    STEPS.Push(nested);
                    continue;
                }
                return;
            }
            Finish();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogException(exception);
            Finish();
        }
    }

    private static IEnumerator Scenarios()
    {
        player = GameObject.Find("Player").GetComponent<PlayerHealth>();
        player.Configure(10000f);
        Check(CoverNode.Active.Count == 4, "four authored cover nodes registered in actual play mode");
        var enemy = Spawn(new Vector3(-26f, 0f, 4f), true);
        observed = enemy;
        foreach (var node in CoverNode.Active)
        {
            Check(node.Solid != null && !node.Solid.isTrigger && node.IsHiddenFrom(player), node.name + " has solid body/head occlusion");
            Check(enemy.Navigation.CanReach(node.HiddenPosition) && node.TryPeek(enemy, player, 50f, out _), node.name + " hidden and exposed peek are reachable with LOS");
        }
        yield return Wait(() => enemy.State == Enemy.CombatState.Hiding, 15f, "seek -> path -> hidden arrival");
        Check(!enemy.Navigation.CanReach(new Vector3(70f, 0f, 0f)), "destination outside baked arena is rejected rather than straight-line movement");
        var reserved = enemy.CurrentCover;
        Check(reserved != null && reserved.Owner == enemy, "cover is exclusively reserved during hiding");
        int shotsBefore = Projectile.ActiveCount;
        yield return Wait(() => enemy.State == Enemy.CombatState.MovingToPeek, 4f, "hide timer -> move to exposed peek");
        Check(Projectile.ActiveCount == shotsBefore, "no shot while hidden or walking to first peek");
        yield return Wait(() => enemy.State == Enemy.CombatState.Peeking, 7f, "exposed peek arrival");
        yield return Wait(() => enemy.State == Enemy.CombatState.Retreating, 3f, "LOS projectile fire -> retreat");
        Check(sawProjectile, "real Enemy.Update launched travelling projectile");
        yield return Wait(() => enemy.State == Enemy.CombatState.Hiding, 7f, "retreat returns behind solid cover");
        Check(player.CurrentHealth < 10000f, "projectile Update reaches player after flight (not direct damage)");

        var second = Spawn(new Vector3(-25f, 0f, 4f), true);
        yield return Frames(3);
        Check(second.CurrentCover != null && second.CurrentCover != enemy.CurrentCover, "competing ranged enemy reserves different cover");
        enemy.TakeDamage(100000f);
        Check(reserved.Owner == null && enemy.CurrentCover == null, "death releases reservation synchronously");
        var secondCover = second.CurrentCover;
        second.gameObject.SetActive(false);
        Check(secondCover.Owner == null, "disable releases reservation");
        UnityEngine.Object.Destroy(second.gameObject);
        Projectile.CancelAll();

        // 동적 peek 차단은 NavMesh 재베이크 없이 무효 처리한다.
        var blocked = Spawn(new Vector3(-22f, 0f, -2f), true);
        observed = blocked;
        yield return Wait(() => blocked.CurrentCover != null, 2f, "acquire cover for blocked peek scenario");
        var blockedCover = blocked.CurrentCover;
        var blockers = new List<GameObject>();
        foreach (Transform child in blockedCover.transform)
        {
            if (child.name.StartsWith("Peek", StringComparison.Ordinal))
            {
                blockers.Add(Box("DynamicPeekBlock", child.position + Vector3.up, new Vector3(1.5f, 2f, 1.5f)));
            }
        }
        Physics.SyncTransforms();
        Check(!blockedCover.TryPeek(blocked, player, 50f, out _), "occupied/blocked peek is rejected");
        yield return Wait(() => blocked.CurrentCover != blockedCover, 2f, "blocked peek causes release and reselection");
        Check(blockedCover.Owner == null, "invalid peek reservation cleaned");
        foreach (var box in blockers)
        {
            UnityEngine.Object.Destroy(box);
        }
        blocked.StopCombat();
        UnityEngine.Object.Destroy(blocked.gameObject);
        yield return Frames(2);

        var movingTarget = Spawn(new Vector3(-22f, 0f, -2f), true);
        yield return Wait(() => movingTarget.CurrentCover != null, 2f, "acquire before target flank");
        var oldCover = movingTarget.CurrentCover;
        SetPlayer(new Vector3(-32f, 0f, -2f));
        Check(!oldCover.IsHiddenFrom(player), "flanking target invalidates hidden position");
        yield return Wait(() => movingTarget.CurrentCover != oldCover, 2f, "moving target triggers cover reconsideration");
        Check(oldCover.Owner == null, "flanked cover reservation released");
        movingTarget.StopCombat();
        UnityEngine.Object.Destroy(movingTarget.gameObject);
        SetPlayer(new Vector3(0f, 0f, -6f));
        yield return Frames(2);

        var right = Spawn(new Vector3(26f, 0f, 4f), true);
        observed = right;
        yield return Wait(() => right.State == Enemy.CombatState.Hiding, 15f, "right entrance routes to real cover at production speed");
        Check(right.CurrentCover != null && right.CurrentCover.HiddenPosition.x > 0f, "right spawn selects right-side cover");
        var rightCover = right.CurrentCover;
        player.ApplyDamage(100000f);
        yield return Frames(3);
        Check(right.State == Enemy.CombatState.Stopped && rightCover.Owner == null, "target death stops combat and releases cover");
        right.StopCombat();
        UnityEngine.Object.Destroy(right.gameObject);
        player.Configure(10000f);
        yield return Frames(2);

        var coverObjects = new List<GameObject>();
        foreach (var node in UnityEngine.Object.FindObjectsByType<CoverNode>(FindObjectsSortMode.None))
        {
            coverObjects.Add(node.gameObject);
            node.enabled = false;
        }
        var fallback = Spawn(new Vector3(-23f, 0f, -6f), true);
        observed = fallback;
        yield return Frames(3);
        Check(fallback.State == Enemy.CombatState.Advancing && fallback.CurrentCover == null, "no cover -> safe navigation/LOS fallback");
        float beforeFallback = player.CurrentHealth;
        yield return Wait(() => player.CurrentHealth < beforeFallback, 8f, "no-cover fallback fires LOS projectile");
        fallback.StopCombat();
        UnityEngine.Object.Destroy(fallback.gameObject);
        Projectile.CancelAll();

        // 동적 solid는 기존 NavMesh에 없어도 sweep으로 멈춘다.
        SetPlayer(new Vector3(-5f, 0f, -6f));
        var dynamicEnemy = Spawn(new Vector3(-23f, 0f, -6f), false);
        var dynamicWall = Box("DynamicMovementBlock", new Vector3(-21f, 1.1f, -6f), new Vector3(0.4f, 2.2f, 8f));
        Physics.SyncTransforms();
        yield return Delay(1f);
        Check(dynamicEnemy.transform.position.x < -21.5f, "capsule sweep blocks newly added unbaked wall");
        Check(!EnemySight.Clear(dynamicEnemy.transform.position + Vector3.up * 1.4f, player, 50f, dynamicEnemy.transform, 0.05f),
            "solid wall blocks ranged muzzle-to-player LOS");
        dynamicEnemy.StopCombat();
        UnityEngine.Object.Destroy(dynamicEnemy.gameObject);
        var blockedShooter = Spawn(new Vector3(-23f, 0f, -6f), true);
        float wallHp = player.CurrentHealth;
        yield return Delay(1f);
        Check(Projectile.ActiveCount == 0 && player.CurrentHealth == wallHp, "ranged Update does not fire or damage through blocked LOS");
        blockedShooter.StopCombat();
        UnityEngine.Object.Destroy(blockedShooter.gameObject);
        UnityEngine.Object.Destroy(dynamicWall);
        yield return Frames(2);

        var noNav = Spawn(new Vector3(-23f, 0f, -6f), true);
        var surface = UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>();
        surface.RemoveData();
        Vector3 noNavPosition = noNav.transform.position;
        yield return Delay(0.5f);
        Debug.Log($"{TAG} missing-nav ready={noNav.Navigation.Ready} delta={noNav.transform.position - noNavPosition} triangles={NavMesh.CalculateTriangulation().vertices.Length}");
        Check(!noNav.Navigation.Ready && (noNav.transform.position - noNavPosition).sqrMagnitude < 0.001f,
            "missing navigation fails closed without transform traversal");
        noNav.StopCombat();
        UnityEngine.Object.Destroy(noNav.gameObject);
        surface.AddData();
        Projectile.CancelAll();
        yield return Frames(2);

        // 기존 TrenchSouth를 우회한다. 정면 직선은 solid를 가로지르는 배치다.
        SetPlayer(new Vector3(0f, 0f, 2f));
        var rusher = Spawn(new Vector3(0f, 0f, 10f), false);
        observed = rusher;
        var route = new NavMeshPath();
        bool path = NavMesh.CalculatePath(rusher.transform.position, player.transform.position, NavMesh.AllAreas, route);
        Check(path && route.status == NavMeshPathStatus.PathComplete && route.corners.Length > 2, "melee path bends around existing TrenchSouth wall");
        Check(rusher.WeaponId == "", "rusher ignores supplied era weapon");
        float beforeMelee = player.CurrentHealth;
        yield return Delay(0.7f);
        Check(player.CurrentHealth == beforeMelee && Projectile.ActiveCount == 0, "rusher cannot hit through wall and never shoots");
        yield return Wait(() => player.CurrentHealth < beforeMelee, 14f, "rusher navigates around wall and lands melee hit");
        Check(Projectile.ActiveCount == 0 && rusher.CurrentCover == null, "melee-only invariant after arrival");
        rusher.StopCombat();
        UnityEngine.Object.Destroy(rusher.gameObject);
        SetPlayer(new Vector3(0f, 0f, -6f));
        foreach (var coverObject in coverObjects)
        {
            coverObject.GetComponent<CoverNode>().enabled = true;
        }
        yield return Frames(2);

        // 실제 WaveController.Stop 호출: Destroy 대기 전 같은 프레임에 해제한다.
        var waves = UnityEngine.Object.FindFirstObjectByType<WaveController>();
        var spec = WaveInfo.Resolve(TableManager.Instance.Wave, 1);
        spec.rushers = 0;
        spec.riflemen = 1;
        spec.heavies = 0;
        waves.enabled = true;
        waves.Begin(spec, TableManager.Instance.Enemy, TableManager.Instance.EnemyWeapon, player);
        yield return Wait(() => waves.AliveCount == 1 && FindOwnedCover() != null, 5f, "wave-spawned prefab uses baked navigation and reserves cover");
        var owned = FindOwnedCover();
        waves.Stop();
        Check(owned != null && owned.Owner == null && waves.AliveCount == 0 && Projectile.ActiveCount == 0,
            "wave stop immediately releases cover and cancels all projectiles");
        waves.enabled = false;
        Check(!collisionReported, "all tracked frame movement stayed outside solid geometry");
    }

    private static CoverNode FindOwnedCover()
    {
        foreach (var cover in CoverNode.Active)
        {
            if (cover.Owner != null)
            {
                return cover;
            }
        }
        return null;
    }

    private static Enemy Spawn(Vector3 _position, bool _ranged)
    {
        Check(EnemyNavigation.TryStandPoint(_position, 2f, out Vector3 point), "scenario spawn is on unoccupied NavMesh");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PocCoverAuthoring.ENEMY_PREFAB);
        var go = UnityEngine.Object.Instantiate(prefab, point, Quaternion.identity);
        var enemy = go.GetComponent<Enemy>();
        var source = EnemyInfo.Find(TableManager.Instance.Enemy, _ranged ? EnemyInfo.RIFLEMAN_ID : EnemyInfo.RUSHER_ID);
        var stats = JsonUtility.FromJson<EnemyData.Entry>(JsonUtility.ToJson(source));
        // 우회가 긴 근접 시나리오만 속도를 높인다. 제품 JSON은 바꾸지 않는다.
        if (!_ranged)
        {
            stats.moveSpeed = 9f;
        }
        enemy.Configure(stats, TableManager.Instance.EnemyWeapon.weapons[2], 1f, Color.white, player);
        ENEMIES.Add(enemy);
        return enemy;
    }

    private static void SetPlayer(Vector3 _position)
    {
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = _position;
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static GameObject Box(string _name, Vector3 _position, Vector3 _size)
    {
        var go = new GameObject(_name);
        go.transform.position = _position;
        go.AddComponent<BoxCollider>().size = _size;
        return go;
    }

    private static IEnumerator Wait(Func<bool> _condition, float _seconds, string _name)
    {
        float end = Time.time + _seconds;
        while (!_condition() && Time.time < end)
        {
            yield return null;
        }
        Check(_condition(), _name);
    }

    private static IEnumerator Frames(int _count)
    {
        for (int i = 0; i < _count; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator Delay(float _seconds)
    {
        float end = Time.time + _seconds;
        while (Time.time < end)
        {
            yield return null;
        }
    }

    private static void Observe()
    {
        sawProjectile |= Projectile.ActiveCount > 0;
        if (observed != null && observed.State != lastState)
        {
            lastState = observed.State;
            Debug.Log($"{TAG} FRAME {Time.frameCount} t={Time.time:F2} state={lastState} position={observed.transform.position}");
        }
        foreach (var enemy in ENEMIES)
        {
            if (enemy == null || enemy.IsDead || !enemy.gameObject.activeInHierarchy || collisionReported)
            {
                continue;
            }
            Vector3 point = enemy.transform.position;
            foreach (var collider in Physics.OverlapCapsule(point + Vector3.up * 0.45f, point + Vector3.up * 1.4f,
                0.34f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (collider.GetComponentInParent<Enemy>() == null && collider.GetComponentInParent<PlayerHealth>() == null)
                {
                    collisionReported = true;
                    Check(false, $"enemy penetrated {collider.name} at {point}");
                }
            }
        }
    }

    private static void OnLog(string _message, string _stack, LogType _type)
    {
        if ((_type == LogType.Error && !_message.StartsWith(TAG, StringComparison.Ordinal)) || _type == LogType.Exception)
        {
            failures++;
        }
    }

    private static void Check(bool _condition, string _name)
    {
        if (_condition)
        {
            passes++;
            Debug.Log($"{TAG} PASS: {_name}");
        }
        else
        {
            failures++;
            Debug.LogError($"{TAG} FAIL: {_name}");
        }
    }

    private static void Finish()
    {
        SessionState.SetBool(RUNNING, false);
        Debug.Log($"{TAG} finished real play-mode frames={Time.frameCount}, passes={passes}, failures={failures}");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
