using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 발사체 결정적 검증(에디터 배치, 플레이 없음). Projectile.Step을 직접 돌려 시간 진행을 고정한다.
//   -executeMethod PocProjectileVerification.RunProjectileChecks
// 확인 항목: 발사 즉시 피해 없음, 닿는 순간 1회 피해, 지형 차단(양쪽), 발사자 자기 몸 통과·중복 피해 없음,
// 고속·얇은 장애물, 수명·최대 거리 소멸, 전체 정리(CancelAll), 적 발사가 직접 피해를 주지 않음, 테이블·프리팹 참조.
public static class PocProjectileVerification
{
    private const string TAG = "[PocProjectileVerification]";
    private const float PLAYER_Z = 10f;
    private const float PLAYER_HP = 100f;
    private const float ENEMY_HP = 200f;
    private const float DT = 0.1f;

    private static int failures;

    public static void RunProjectileChecks()
    {
        failures = 0;
        CheckTables();
        CheckNoDamageAtLaunch();
        CheckDelayedPlayerCollision();
        CheckWallBlocksBothTeams();
        CheckShooterImmuneAndExactlyOnce();
        CheckFastThinWall();
        CheckExpiryAndMaxDistance();
        CheckCancelAll();
        CheckEnemyFireDoesNotDamageDirectly();

        Debug.Log($"{TAG} finished: failures={failures}");
        PocEditorExit.Exit(failures == 0 ? 0 : 1);
    }

    // ---------- 테이블·프리팹 ----------

    private static void CheckTables()
    {
        var weapons = JsonUtility.FromJson<WeaponData>(LoadJson("WeaponData"));
        Check(weapons != null && weapons.weapons.Length > 0, "WeaponData.json parses");
        if (weapons != null)
        {
            foreach (var weapon in weapons.weapons)
            {
                Check(weapon.projectileSpeed > 0f && weapon.projectileLifetime > 0f && weapon.projectileRadius > 0f, $"weapon {weapon.id} has projectile tuning");
                Check(weapon.projectileSpeed * weapon.projectileLifetime >= weapon.range, $"weapon {weapon.id} projectile reaches range within lifetime");
            }
        }

        var enemyWeapons = JsonUtility.FromJson<EnemyWeaponData>(LoadJson("EnemyWeaponData"));
        Check(enemyWeapons != null && enemyWeapons.weapons.Length == 3, "EnemyWeaponData.json has 3 era weapons");
        if (enemyWeapons != null)
        {
            foreach (var weapon in enemyWeapons.weapons)
            {
                Check(weapon.projectileSpeed > 0f && weapon.projectileLifetime > 0f && weapon.projectileRadius > 0f, $"enemy weapon {weapon.id} has projectile tuning");
                Check(weapon.projectileSpeed * weapon.projectileLifetime >= weapon.attackRange, $"enemy weapon {weapon.id} projectile reaches range within lifetime");
            }
        }

        var enemies = JsonUtility.FromJson<EnemyData>(LoadJson("EnemyData"));
        Check(enemies != null && enemies.enemies.Length == 3, "EnemyData.json has 3 kinds");
        Check(Resources.Load<GameObject>("Projectiles/PlayerBullet")?.GetComponent<Projectile>() != null, "PlayerBullet prefab has Projectile");
        Check(Resources.Load<GameObject>("Projectiles/EnemyBullet")?.GetComponent<Projectile>() != null, "EnemyBullet prefab has Projectile");
    }

    private static string LoadJson(string _name)
    {
        TextAsset asset = Resources.Load<TextAsset>(_name);
        return asset != null ? asset.text : "";
    }

    // ---------- 장면 시나리오 ----------

    private static void CheckNoDamageAtLaunch()
    {
        var scene = NewScene();
        var player = MakePlayer(scene, PLAYER_Z);
        Projectile shot = LaunchEnemyShot(0f, 20f, 30f, 5f, 8f);
        Check(player.CurrentHealth == PLAYER_HP, "no damage at launch (same frame)");
        shot.Step(0f);
        Check(player.CurrentHealth == PLAYER_HP && !shot.IsResolved, "no damage on zero-length first step");
        ClearProjectiles();
    }

    private static void CheckDelayedPlayerCollision()
    {
        var player = MakePlayer(NewScene(), PLAYER_Z);
        int impacts = 0;
        Projectile shot = LaunchEnemyShot(0f, 20f, 30f, 5f, 8f);
        shot.Impacted += _impact => impacts++;

        // 플레이어 앞면은 z=9.5. 속도 20, 스텝 0.1s = 2m. 4스텝 후 z=8이라 아직 닿지 않는다.
        for (int i = 0; i < 4; i++)
        {
            shot.Step(DT);
        }
        Check(player.CurrentHealth == PLAYER_HP && impacts == 0, "player untouched before projectile reaches body");
        shot.Step(DT);
        Check(player.CurrentHealth == PLAYER_HP - 8f, "player takes damage once at collision");
        Check(impacts == 1 && shot.IsResolved, "impact raised once and projectile resolved");
        shot.Step(DT);
        Check(player.CurrentHealth == PLAYER_HP - 8f && impacts == 1, "no repeated damage after resolution");
        ClearProjectiles();
    }

    private static void CheckWallBlocksBothTeams()
    {
        var scene = NewScene();
        var player = MakePlayer(scene, PLAYER_Z);
        var enemy = MakeEnemy(scene, PLAYER_Z);
        MakeWall(scene, 5f, 0.5f);

        Projectile enemyShot = LaunchEnemyShot(0f, 20f, 30f, 5f, 8f);
        Projectile playerShot = LaunchPlayerShot(0f, 20f, 30f, 5f, 25f);
        Projectile.Impact? enemyImpact = null;
        Projectile.Impact? playerImpact = null;
        enemyShot.Impacted += _impact => enemyImpact = _impact;
        playerShot.Impacted += _impact => playerImpact = _impact;
        for (int i = 0; i < 20; i++)
        {
            enemyShot.Step(DT);
            playerShot.Step(DT);
        }
        Check(player.CurrentHealth == PLAYER_HP, "wall blocks enemy shot from player");
        Check(enemy.Health == ENEMY_HP, "wall blocks player shot from enemy");
        Check(enemyImpact.HasValue && !enemyImpact.Value.damagedEnemy && playerImpact.HasValue && !playerImpact.Value.damagedEnemy, "wall impacts report no damage");
        ClearProjectiles();
    }

    private static void CheckShooterImmuneAndExactlyOnce()
    {
        var scene = NewScene();
        var enemy = MakeEnemy(scene, PLAYER_Z);
        // 플레이어 몸통 콜라이더 안에서 쏜다. 자기 몸은 통과해야 하고 자기를 맞혀서는 안 된다.
        var body = new GameObject("PlayerBody");
        body.transform.position = Vector3.zero;
        body.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        body.AddComponent<PlayerHealth>();
        Projectile shot = LaunchPlayerShotFrom(body.transform, Vector3.zero, 20f, 30f, 5f, 25f);
        int impacts = 0;
        shot.Impacted += _impact => impacts++;
        for (int i = 0; i < 40; i++)
        {
            shot.Step(DT);
        }
        Check(enemy.Health == ENEMY_HP - 25f, "shooter's own collider does not block or take damage; enemy takes damage once");
        Check(impacts == 1, "exactly one impact for one shot");
        for (int i = 0; i < 10; i++)
        {
            shot.Step(DT);
        }
        Check(enemy.Health == ENEMY_HP - 25f && impacts == 1, "no further damage after projectile resolved");
        ClearProjectiles();
    }

    private static void CheckFastThinWall()
    {
        var scene = NewScene();
        var player = MakePlayer(scene, PLAYER_Z);
        MakeWall(scene, 5f, 0.05f);
        // 속도 2000m/s, 60fps면 한 스텝에 약 33m. 얇은 벽을 건너뛰면 안 된다.
        Projectile shot = LaunchEnemyShot(0f, 2000f, 60f, 5f, 8f);
        bool blocked = false;
        shot.Impacted += _impact => blocked = !_impact.damagedEnemy && _impact.team == Projectile.Team.Enemy;
        for (int i = 0; i < 3; i++)
        {
            shot.Step(1f / 60f);
        }
        Check(blocked && player.CurrentHealth == PLAYER_HP, "fast projectile does not tunnel through thin wall");
        ClearProjectiles();
    }

    private static void CheckExpiryAndMaxDistance()
    {
        var scene = NewScene();
        var player = MakePlayer(scene, PLAYER_Z);
        int impacts = 0;

        Projectile byLifetime = LaunchEnemyShot(0f, 10f, 30f, 0.3f, 8f);
        byLifetime.Impacted += _impact => impacts++;
        byLifetime.Step(0.5f);
        Check(byLifetime.IsResolved && impacts == 0, "projectile expires at lifetime without impact");

        Projectile byDistance = LaunchEnemyShot(0f, 10f, 2f, 5f, 8f);
        byDistance.Impacted += _impact => impacts++;
        byDistance.Step(0.5f);
        Check(byDistance.IsResolved && impacts == 0 && player.CurrentHealth == PLAYER_HP, "projectile expires at max distance without impact");
        ClearProjectiles();
    }

    private static void CheckCancelAll()
    {
        var scene = NewScene();
        var player = MakePlayer(scene, PLAYER_Z);
        LaunchEnemyShot(0f, 20f, 30f, 5f, 8f);
        LaunchEnemyShot(0f, 20f, 30f, 5f, 8f);
        LaunchPlayerShot(0f, 20f, 30f, 5f, 25f);
        Check(Projectile.ActiveCount == 3, "three projectiles in flight before cancel");
        Projectile.CancelAll();
        Check(Projectile.ActiveCount == 0, "CancelAll clears every in-flight projectile");
        Check(player.CurrentHealth == PLAYER_HP, "CancelAll deals no damage");
        ClearProjectiles();
    }

    private static void CheckEnemyFireDoesNotDamageDirectly()
    {
        var scene = NewScene();
        var player = MakePlayer(scene, PLAYER_Z);
        var enemy = MakeEnemy(scene, 0f, true);
        // 원거리 적의 발사 메서드를 직접 호출한다. Update 없이 발사 경로만 확인한다.
        MethodInfo fire = typeof(Enemy).GetMethod("FireProjectile", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(fire != null, "Enemy exposes FireProjectile path");
        fire?.Invoke(enemy, null);
        Check(player.CurrentHealth == PLAYER_HP, "ranged enemy firing does not call ApplyDamage");
        Check(Projectile.ActiveCount == 1, "ranged enemy firing spawns one projectile instance");
        ClearProjectiles();
    }

    // ---------- 헬퍼 ----------

    // 시나리오가 끝나면 날아가는 탄을 모두 치운다. 다음 시나리오의 ActiveCount가 섞이지 않게 한다.
    private static void ClearProjectiles()
    {
        Projectile.CancelAll();
    }

    private static Scene NewScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Physics.SyncTransforms();
        return scene;
    }

    private static void CancelAndClose()
    {
        Projectile.CancelAll();
    }

    private static PlayerHealth MakePlayer(Scene _scene, float _z)
    {
        var go = new GameObject("Player");
        go.transform.position = new Vector3(0f, 1f, _z);
        go.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        var health = go.AddComponent<PlayerHealth>();
        health.Configure(PLAYER_HP);
        Physics.SyncTransforms();
        return health;
    }

    private static Enemy MakeEnemy(Scene _scene, float _z, bool _ranged = false)
    {
        var go = new GameObject("Enemy");
        go.transform.position = new Vector3(0f, 1f, _z);
        go.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        var enemy = go.AddComponent<Enemy>();
        var stats = new EnemyData.Entry { id = _ranged ? "rifleman" : "rusher", maxHealth = ENEMY_HP, ranged = _ranged, stopDistance = 0f };
        EnemyWeaponData.Entry weapon = null;
        if (_ranged)
        {
            weapon = new EnemyWeaponData.Entry { id = "wwii_rifle", attackDamage = 8f, attackRange = 50f, attackInterval = 1.2f, projectileSpeed = 80f, projectileLifetime = 1f, projectileRadius = 0.05f };
        }
        enemy.Configure(stats, weapon, 1f, Color.white, FindPlayerHealthOrNull());
        Physics.SyncTransforms();
        return enemy;
    }

    private static PlayerHealth FindPlayerHealthOrNull()
    {
        var go = GameObject.Find("Player");
        return go != null ? go.GetComponent<PlayerHealth>() : null;
    }

    private static void MakeWall(Scene _scene, float _z, float _thickness)
    {
        var wall = new GameObject("Wall");
        wall.transform.position = new Vector3(0f, 1.5f, _z);
        wall.AddComponent<BoxCollider>().size = new Vector3(10f, 3f, _thickness);
        Physics.SyncTransforms();
    }

    // 적 탄: 적 소유자 없이 원점에서 +z로 쏜다. 소유자는 없으므로 자기 몸 예외는 적용되지 않는다.
    private static Projectile LaunchEnemyShot(float _z, float _speed, float _range, float _lifetime, float _damage)
    {
        var shot = new Projectile.Shot { team = Projectile.Team.Enemy, damage = _damage, speed = _speed, maxDistance = _range, lifetime = _lifetime, radius = 0.05f };
        Projectile projectile = ProjectileSpawner.Launch(ProjectileSpawner.ENEMY_BULLET, shot, new Vector3(0f, 1f, _z), Vector3.forward, Physics.DefaultRaycastLayers, null);
        Check(projectile != null, "enemy bullet launches from prefab");
        return projectile;
    }

    private static Projectile LaunchPlayerShot(float _z, float _speed, float _range, float _lifetime, float _damage)
    {
        return LaunchPlayerShotFrom(null, new Vector3(0f, 1f, _z), _speed, _range, _lifetime, _damage);
    }

    private static Projectile LaunchPlayerShotFrom(Transform _owner, Vector3 _origin, float _speed, float _range, float _lifetime, float _damage)
    {
        var shot = new Projectile.Shot { team = Projectile.Team.Player, damage = _damage, speed = _speed, maxDistance = _range, lifetime = _lifetime, radius = 0.05f };
        Projectile projectile = ProjectileSpawner.Launch(ProjectileSpawner.PLAYER_BULLET, shot, _origin, Vector3.forward, Physics.DefaultRaycastLayers, _owner);
        Check(projectile != null, "player bullet launches from prefab");
        return projectile;
    }

    private static void Check(bool _condition, string _name)
    {
        if (_condition)
        {
            Debug.Log($"{TAG} PASS: {_name}");
        }
        else
        {
            failures++;
            Debug.LogError($"{TAG} FAIL: {_name}");
        }
    }
}
