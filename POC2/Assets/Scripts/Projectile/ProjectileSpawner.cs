using System.Collections.Generic;
using UnityEngine;

// 발사체 프리팹 인스턴스를 만든다. 프리팹은 PocAssetBuilder가 에디터에서 만들고, 여기서는 메시를 만들지 않는다.
// 발사체는 게임플레이 스폰이므로 런타임 Instantiate를 쓴다(작업지침 §3 예외).
public static class ProjectileSpawner
{
    public const string PLAYER_BULLET = "Projectiles/PlayerBullet";
    public const string ENEMY_BULLET = "Projectiles/EnemyBullet";

    private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

    // 발사체를 만들고 발사 상태로 둔다. 프리팹이 없으면 에러를 남기고 null을 준다.
    public static Projectile Launch(string _path, Projectile.Shot _shot, Vector3 _origin, Vector3 _direction, LayerMask _mask, Transform _owner)
    {
        if (!cache.TryGetValue(_path, out GameObject prefab))
        {
            prefab = Resources.Load<GameObject>(_path);
            cache[_path] = prefab;
        }
        if (prefab == null)
        {
            Debug.LogError($"발사체 프리팹 {_path}를 Resources에서 찾지 못했습니다.");
            return null;
        }

        GameObject instance = Object.Instantiate(prefab, _origin, Quaternion.LookRotation(_direction));
        Projectile projectile = instance.GetComponent<Projectile>();
        if (projectile == null)
        {
            Debug.LogError($"발사체 프리팹 {_path}에 Projectile 컴포넌트가 없습니다.");
            Object.Destroy(instance);
            return null;
        }
        projectile.Launch(_shot, _origin, _direction, _mask, _owner);
        return projectile;
    }
}
