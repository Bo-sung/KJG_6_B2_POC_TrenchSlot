using UnityEditor;
using UnityEngine;

// 기존 적 프리팹의 AI·머즐·콜라이더를 보존하면서 플레이어와 동일한 무기 외형을 연결한다.
public static class PocEnemyWeaponAuthoring
{
    private const string ENEMY_PATH = "Assets/Prefabs/Gameplay/Enemy.prefab";
    private const string VISUAL_NAME = "PlayerWeaponVisual";

    [MenuItem("POC/Connect Enemy Weapon Models")]
    public static void Apply()
    {
        TableManager.ReloadAfterTableEdit();
        var tables = TableManager.Instance;
        var held = AssetDatabase.LoadAssetAtPath<GameObject>(PocWeaponDropAuthoring.HELD_PREFAB_PATH);
        if (held == null || tables.EnemyWeapon == null || tables.Weapon == null)
        {
            throw new System.InvalidOperationException("적 무기 연결에 필요한 프리팹 또는 테이블이 없습니다.");
        }
        var enemy = PrefabUtility.LoadPrefabContents(ENEMY_PATH);
        try
        {
            var meleeWeapon = EnsureMeleeWeapon(enemy);
            var enemyComponent = enemy.GetComponent<Enemy>();
            var enemyProperties = new SerializedObject(enemyComponent);
            enemyProperties.FindProperty("meleeWeapon").objectReferenceValue = meleeWeapon;
            enemyProperties.ApplyModifiedPropertiesWithoutUndo();
            var mount = enemy.transform.Find("WeaponMount");
            if (mount == null)
            {
                throw new System.InvalidOperationException("적 프리팹에 WeaponMount가 없습니다.");
            }
            foreach (var entry in tables.EnemyWeapon.weapons)
            {
                var weapon = entry != null ? WeaponInfo.Find(tables.Weapon, entry.playerWeaponId) : null;
                var target = entry != null ? mount.Find(entry.id) : null;
                var source = weapon != null ? held.transform.Find(weapon.modelId) : null;
                if (target == null || source == null)
                {
                    throw new System.InvalidOperationException($"적 무기 외형 연결 실패: {entry?.id}/{entry?.playerWeaponId}");
                }
                // 기존 파트는 삭제하지 않는다. 머즐 위치와 이전 저작 데이터는 그대로 보존한다.
                foreach (Transform child in target)
                {
                    if (child.name != VISUAL_NAME)
                    {
                        foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                        {
                            renderer.enabled = false;
                        }
                    }
                }
                var previous = target.Find(VISUAL_NAME);
                if (previous != null)
                {
                    Object.DestroyImmediate(previous.gameObject);
                }
                var visual = Object.Instantiate(source.gameObject, target, false);
                visual.name = VISUAL_NAME;
                visual.SetActive(true);
            }
            PrefabUtility.SaveAsPrefabAsset(enemy, ENEMY_PATH);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(enemy);
        }
        AssetDatabase.SaveAssets();
    }

    // 각목은 외형만 제공한다. 물리 콜라이더를 추가하지 않아 적 이동과 공격 판정을 바꾸지 않는다.
    public static GameObject EnsureMeleeWeapon(GameObject _enemy)
    {
        var existing = _enemy.transform.Find("MeleeWeapon");
        if (existing != null)
        {
            return existing.gameObject;
        }
        var stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stick.name = "MeleeWeapon";
        stick.transform.SetParent(_enemy.transform, false);
        stick.transform.localPosition = new Vector3(0.48f, 1.05f, 0.25f);
        stick.transform.localRotation = Quaternion.Euler(-35f, 0f, -12f);
        stick.transform.localScale = new Vector3(0.12f, 0.12f, 0.9f);
        var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/EnemyGunWood.mat");
        if (wood != null)
        {
            stick.GetComponent<Renderer>().sharedMaterial = wood;
        }
        Object.DestroyImmediate(stick.GetComponent<Collider>());
        stick.SetActive(false);
        return stick;
    }
}
