using System.Collections.Generic;
using UnityEngine;

// Resources/Effects 아래 연출 프리팹을 처음 쓸 때 한 번 읽어 두고, 필요할 때 복제한다.
// 연출 프리팹은 PocAssetBuilder가 에디터에서 만든다. 여기서는 메시를 만들지 않는다.
public static class EffectSpawner
{
    public const string MUZZLE_FLASH = "Effects/MuzzleFlash";
    public const string HIT_SPARK = "Effects/HitSpark";

    private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

    public static void Spawn(string _path, Vector3 _position, Quaternion _rotation)
    {
        if (!cache.TryGetValue(_path, out GameObject prefab))
        {
            prefab = Resources.Load<GameObject>(_path);
            cache[_path] = prefab;
            if (prefab == null)
            {
                Debug.LogWarning($"연출 프리팹 {_path}를 Resources에서 찾지 못했습니다.");
            }
        }
        if (prefab != null)
        {
            Object.Instantiate(prefab, _position, _rotation);
        }
    }

    public static bool Exists(string _path)
    {
        return Resources.Load<GameObject>(_path) != null;
    }
}
