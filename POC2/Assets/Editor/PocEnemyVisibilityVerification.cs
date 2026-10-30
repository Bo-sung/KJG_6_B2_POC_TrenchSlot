using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// -batchmode -nographics -executeMethod PocEnemyVisibilityVerification.RunPlayChecks (no -quit).
[InitializeOnLoad]
public static class PocEnemyVisibilityVerification
{
    private const string RUNNING = "PocEnemyVisibilityVerification.running";
    private static int passes;
    private static int failures;

    static PocEnemyVisibilityVerification()
    {
        EditorApplication.update += Tick;
    }

    public static void RunPlayChecks()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
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
        SessionState.SetBool(RUNNING, true);
        EditorApplication.EnterPlaymode();
    }

    public static void AuthorSpawnsAndRunPlayChecks()
    {
        PocEnemyVisibilityAuthoring.AuthorWeapons();
        string prefab = System.IO.File.ReadAllText(PocEnemyVisibilityAuthoring.PREFAB);
        PocEnemyVisibilityAuthoring.AuthorWeapons();
        if (prefab != System.IO.File.ReadAllText(PocEnemyVisibilityAuthoring.PREFAB))
        {
            throw new InvalidOperationException("Weapon authoring is not idempotent.");
        }
        PocEnemyVisibilityAuthoring.AuthorAheadSpawnPoints();
        string scene = System.IO.File.ReadAllText("Assets/Scenes/Game.unity");
        PocEnemyVisibilityAuthoring.AuthorAheadSpawnPoints();
        if (scene != System.IO.File.ReadAllText("Assets/Scenes/Game.unity"))
        {
            throw new InvalidOperationException("Spawn authoring is not idempotent.");
        }
        Debug.Log("[PocEnemyVisibilityVerification] PASS selective weapon and spawn authoring idempotence");
        RunPlayChecks();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(RUNNING, false) || !EditorApplication.isPlaying)
        {
            return;
        }
        SessionState.SetBool(RUNNING, false);
        try
        {
            Checks();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogException(exception);
        }
        Debug.Log($"[PocEnemyVisibilityVerification] passes={passes} failures={failures}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    private static void Checks()
    {
        var table = TableManager.Instance;
        var player = GameObject.Find("Player").GetComponent<PlayerHealth>();
        player.Configure(10000f);
        var camera = player.GetComponentInChildren<Camera>();
        var tuning = table.EnemyAi;
        Check(!WaveController.IsInSpawnFront(new Vector3(0f, 0f, -20f), Vector3.zero, Vector3.forward, tuning), "rear candidate rejected");
        Check(!WaveController.IsInSpawnFront(new Vector3(0f, 0f, 2f), Vector3.zero, Vector3.forward, tuning), "minimum distance enforced");
        Check(!WaveController.IsInSpawnFront(new Vector3(20f, 0f, -0.01f), Vector3.zero, Vector3.forward, tuning), "jitter crossing rear boundary rejected");
        Check(WaveController.IsInSpawnFront(new Vector3(0f, 20f, 20f), Vector3.zero, Vector3.forward, tuning), "horizontal hemisphere ignores pitch/height");

        var waves = UnityEngine.Object.FindFirstObjectByType<WaveController>();
        var serialized = new SerializedObject(waves);
        var references = serialized.FindProperty("spawnPoints");
        int authoredCount = references.arraySize;
        Check(authoredCount >= 6, "original two plus optional four facing anchors authored");
        for (int i = 0; i < authoredCount; i++)
        {
            var point = (Transform)references.GetArrayElementAtIndex(i).objectReferenceValue;
            var path = new NavMeshPath();
            Check(EnemyNavigation.TryStandPoint(point.position, 0.3f, out Vector3 safe) &&
                NavMesh.CalculatePath(safe, player.transform.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete,
                point.name + " safe and reachable to actual player");
        }
        var spec = WaveInfo.Resolve(table.Wave, 1);
        spec.rushers = 0;
        spec.riflemen = 2;
        spec.heavies = 0;
        waves.Begin(spec, table.Enemy, table.EnemyWeapon, player);
        for (int yaw = 0; yaw < 360; yaw += 45)
        {
            camera.transform.rotation = Quaternion.Euler(25f, yaw, 0f);
            int chosen = 0;
            for (int i = 0; i < 24; i++)
            {
                if (waves.TryChooseSpawn(out Vector3 candidate))
                {
                    chosen++;
                    Check(WaveController.IsInSpawnFront(candidate, player.transform.position, camera.transform.forward, tuning), "actual jittered sample respects current view yaw=" + yaw);
                }
            }
            Check(chosen > 0, "authored arena flow supports current view yaw=" + yaw);
        }
        // Restrict to one anchor to prove turning away defers without discarding or clearing the wave.
        var original = GameObject.Find("SpawnLeft").transform;
        Vector3 direction = original.position - player.transform.position;
        direction.y = 0f;
        camera.transform.rotation = Quaternion.LookRotation(direction);
        Set(waves, "spawnPoints", new[] { GameObject.Find("SpawnRight").transform, original });
        Set(waves, "spawnIndex", 0);
        Set(waves, "spawnJitter", 0f);
        Check(waves.TryChooseSpawn(out Vector3 alternate) && Vector3.Distance(alternate, original.position) < 0.3f,
            "behind-first authored anchor is skipped for valid front alternative");
        Set(waves, "spawnPoints", new[] { original });
        Set(waves, "spawnJitter", 0f);
        camera.transform.rotation = Quaternion.LookRotation(-direction);
        bool cleared = false;
        waves.Cleared += () => cleared = true;
        Invoke(waves, "Update");
        Check(waves.PendingCount == 2 && waves.AliveCount == 0 && !waves.AllSpawned && !cleared, "rear-only retry preserves queue and cannot clear wave");
        camera.transform.rotation = Quaternion.LookRotation(direction);
        Set(waves, "spawnTimer", 0f);
        Invoke(waves, "Update");
        Check(waves.PendingCount == 1 && waves.AliveCount == 1, "camera turn resumes same pending enemy exactly once");
        Check(!waves.TryChooseSpawn(out _), "occupied character spawn rejected");
        foreach (var enemy in waves.GetComponentsInChildren<Enemy>())
        {
            enemy.StopCombat();
            UnityEngine.Object.DestroyImmediate(enemy.gameObject);
        }
        waves.Stop();

        Vector3 savedAnchor = original.position;
        waves.Begin(spec, table.Enemy, table.EnemyWeapon, player);
        original.position = player.transform.position + direction.normalized * 2f;
        Check(!waves.TryChooseSpawn(out _), "actual candidate below minimum distance rejected");
        original.position = new Vector3(500f, 0f, 500f);
        Check(!waves.TryChooseSpawn(out _), "off-NavMesh/unreachable authored point rejected");
        Set(waves, "spawnTimer", 0f);
        Invoke(waves, "Update");
        Check(waves.PendingCount == 2 && !waves.AllSpawned, "unreachable retry also preserves pending queue");
        original.position = savedAnchor;
        waves.Stop();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PocEnemyVisibilityAuthoring.PREFAB);
        Check(EnemyNavigation.TryStandPoint(original.position, 0.3f, out Vector3 spawn), "gun inspection spawn safe");
        var go = UnityEngine.Object.Instantiate(prefab, spawn, Quaternion.identity);
        var inspected = go.GetComponent<Enemy>();
        inspected.enabled = false;
        // 시대 구간: 1~5 MUSKET · 6~10 WWI · 11~ WWII(상점-세부설계 §2).
        foreach (int wave in new[] { 1, 5, 6, 10, 11, 20 })
        {
            string id = WaveInfo.Resolve(table.Wave, wave).enemyWeapon;
            string expected = wave <= 5 ? "musket" : wave <= 10 ? "wwi_rifle" : "wwii_rifle";
            Check(id == expected, "wave era boundary preserved at " + wave);
            inspected.Configure(EnemyInfo.Find(table.Enemy, EnemyInfo.RIFLEMAN_ID), EnemyWeaponInfo.Find(table.EnemyWeapon, id), 1f, Color.red, player);
            var mount = go.transform.Find("WeaponMount");
            int active = 0;
            foreach (Transform model in mount)
            {
                if (!model.gameObject.activeInHierarchy)
                {
                    continue;
                }
                active++;
                Check(model.name == id && inspected.WeaponId == id, "era active model matches wave " + wave);
                var barrel = model.Find("Barrel").GetComponent<Renderer>();
                var stock = model.Find("Stock").GetComponent<Renderer>();
                Check(barrel.enabled && barrel.bounds.min.x - go.transform.position.x > EnemyNavigation.RADIUS + 0.1f && barrel.bounds.size.x >= 0.12f,
                    id + " visible barrel outside body silhouette, not hidden or thin");
                Check(stock.bounds.size.x >= 0.19f && stock.sharedMaterial.GetColor("_BaseColor").maxColorComponent > 0.8f,
                    id + " broad contrasting stock and static grips");
                Check(model.Find("RearGrip") != null && model.Find("SupportGrip") != null, id + " authored two-point grip support");
                Vector3 muzzle = (Vector3)Invoke(inspected, "ShotOrigin");
                Check(Mathf.Abs(muzzle.x - barrel.bounds.center.x) < 0.01f && Mathf.Abs(muzzle.y - barrel.bounds.center.y) < 0.01f &&
                    muzzle.z >= barrel.bounds.max.z && muzzle.z - barrel.bounds.max.z < 0.04f, id + " projectile origin at visible barrel tip");
                Check(go.GetComponentsInChildren<Collider>().Length == 1, id + " held gun has no projectile-blocking colliders");
            }
            Check(active == 1, "exactly one visibly active era weapon at wave " + wave);
        }
        inspected.Configure(EnemyInfo.Find(table.Enemy, EnemyInfo.RUSHER_ID), table.EnemyWeapon.weapons[2], 1f, Color.red, player);
        Check(inspected.WeaponId == "" && inspected.State == Enemy.CombatState.Melee && go.GetComponentsInChildren<Renderer>().Length == 1,
            "melee-only rusher remains entirely unarmed including grips");
        var controller = player.GetComponent<CharacterController>();
        controller.height = 1f;
        controller.center = Vector3.up * 0.5f;
        Physics.SyncTransforms();
        Check(Vector3.Distance(EnemySight.TargetCenter(player), controller.bounds.center) < 0.001f, "crouching aim uses real controller bounds center");
        UnityEngine.Object.DestroyImmediate(go);
    }

    private static void Set(object _owner, string _name, object _value)
    {
        _owner.GetType().GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_owner, _value);
    }

    private static object Invoke(object _owner, string _name)
    {
        return _owner.GetType().GetMethod(_name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_owner, null);
    }

    private static void Check(bool _passed, string _message)
    {
        if (_passed)
        {
            passes++;
            Debug.Log("[PocEnemyVisibilityVerification] PASS " + _message);
        }
        else
        {
            failures++;
            Debug.LogError("[PocEnemyVisibilityVerification] FAIL " + _message);
        }
    }
}
