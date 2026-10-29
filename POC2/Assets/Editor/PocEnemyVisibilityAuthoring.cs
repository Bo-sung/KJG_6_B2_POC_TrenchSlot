using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Selective, repeatable CLI authoring only. Never runs scene builders or creates runtime geometry.
public static class PocEnemyVisibilityAuthoring
{
    public const string PREFAB = "Assets/Prefabs/Gameplay/Enemy.prefab";

    public static void AuthorWeapons()
    {
        RequireBatch();
        var wood = Material("EnemyGunWood", new Color(0.85f, 0.47f, 0.13f));
        var metal = Material("EnemyGunMetal", new Color(0.68f, 0.79f, 0.88f));
        var root = PrefabUtility.LoadPrefabContents(PREFAB);
        try
        {
            var mount = root.transform.Find("WeaponMount");
            mount.localPosition = new Vector3(0.62f, 1.22f, 0.12f);
            foreach (Transform gun in mount)
            {
                float length = gun.name == "musket" ? 1.3f : gun.name == "wwi_rifle" ? 1.05f : 1.15f;
                Part(gun, "Barrel", new Vector3(0f, 0f, length * 0.5f), new Vector3(0.13f, 0.13f, length), metal);
                Part(gun, "Stock", new Vector3(0f, -0.04f, -0.16f), new Vector3(0.2f, 0.24f, 0.5f), wood);
                Part(gun, "Forestock", new Vector3(0f, -0.07f, 0.35f), new Vector3(0.18f, 0.14f, 0.65f), wood);
                Part(gun, "RearGrip", new Vector3(-0.18f, -0.12f, -0.03f), new Vector3(0.45f, 0.16f, 0.18f), wood);
                Part(gun, "SupportGrip", new Vector3(-0.18f, -0.14f, 0.38f), new Vector3(0.45f, 0.16f, 0.2f), wood);
                Part(gun, "SupportForearm", new Vector3(-0.27f, -0.2f, 0.15f), new Vector3(0.18f, 0.18f, 0.6f), wood);
                if (gun.name == "wwi_rifle")
                {
                    Part(gun, "Bolt", new Vector3(0.13f, 0.03f, 0.08f), new Vector3(0.18f, 0.09f, 0.13f), metal);
                }
                if (gun.name == "wwii_rifle")
                {
                    Part(gun, "Magazine", new Vector3(0f, -0.24f, 0.22f), new Vector3(0.16f, 0.32f, 0.22f), metal);
                    Part(gun, "Sight", new Vector3(0f, 0.13f, 0.52f), new Vector3(0.09f, 0.15f, 0.2f), metal);
                }
                var muzzle = gun.Find("Muzzle");
                if (muzzle == null)
                {
                    muzzle = new GameObject("Muzzle").transform;
                    muzzle.SetParent(gun, false);
                }
                muzzle.localPosition = new Vector3(0f, 0f, length + 0.025f);
                muzzle.localRotation = Quaternion.identity;
                gun.gameObject.SetActive(false);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PREFAB);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[PocEnemyVisibilityAuthoring] selective held weapons saved");
    }

    // Integrator opt-in: adds anchors only, preserves original two anchors, cover and baked navigation.
    public static void AuthorAheadSpawnPoints()
    {
        RequireBatch();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
        var wave = UnityEngine.Object.FindFirstObjectByType<WaveController>();
        var so = new SerializedObject(wave);
        var points = so.FindProperty("spawnPoints");
        var root = GameObject.Find("EnemyFacingSpawns");
        if (root == null)
        {
            root = new GameObject("EnemyFacingSpawns");
        }
        var positions = new[] { new Vector3(-26f, 0f, -12f), new Vector3(26f, 0f, -12f),
            new Vector3(-12f, 0f, 12f), new Vector3(12f, 0f, 12f) };
        for (int i = 0; i < positions.Length; i++)
        {
            string name = "FacingAnchor" + i;
            var point = root.transform.Find(name);
            if (point == null)
            {
                point = new GameObject(name).transform;
                point.SetParent(root.transform, false);
            }
            if (!EnemyNavigation.TryStandPoint(positions[i], 2f, out Vector3 safe))
            {
                throw new InvalidOperationException("Authored facing spawn is not on safe baked navigation: " + positions[i]);
            }
            point.position = safe;
            bool found = false;
            for (int j = 0; j < points.arraySize; j++)
            {
                found |= points.GetArrayElementAtIndex(j).objectReferenceValue == point;
            }
            if (!found)
            {
                points.InsertArrayElementAtIndex(points.arraySize);
                points.GetArrayElementAtIndex(points.arraySize - 1).objectReferenceValue = point;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PocEnemyVisibilityAuthoring] selective facing spawn anchors saved");
    }

    private static void RequireBatch()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException("Use an isolated Unity CLI project, not the open user editor.");
        }
    }

    private static Material Material(string _name, Color _color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + _name + ".mat");
        if (material == null)
        {
            throw new InvalidOperationException("Expected existing enemy material: " + _name);
        }
        material.SetColor("_BaseColor", _color);
        material.SetFloat("_Smoothness", 0.25f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Part(Transform _parent, string _name, Vector3 _position, Vector3 _size, Material _material)
    {
        var part = _parent.Find(_name);
        if (part == null)
        {
            part = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            part.name = _name;
            part.SetParent(_parent, false);
        }
        foreach (var collider in part.GetComponents<Collider>())
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }
        part.localPosition = _position;
        part.localRotation = Quaternion.identity;
        part.localScale = _size;
        part.GetComponent<Renderer>().sharedMaterial = _material;
    }
}
