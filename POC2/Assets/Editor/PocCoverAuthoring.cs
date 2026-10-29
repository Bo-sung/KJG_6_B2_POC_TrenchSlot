using System;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// CLI 선택적 추가. BuildAll/씬 재생성 없이 기존 HUD·슬롯·배치를 보존한다.
public static class PocCoverAuthoring
{
    public const string SCENE = "Assets/Scenes/Game.unity";
    public const string NAV_DATA = "Assets/Scenes/GameEnemyNavigation.asset";
    public const string ENEMY_PREFAB = "Assets/Prefabs/Gameplay/Enemy.prefab";

    public static void AuthorSavedGame()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException("Use an isolated batch project; never replace an open unsaved user scene.");
        }
        var scene = EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
        foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            Debug.Log($"[CoverGeometry] {collider.name} center={collider.bounds.center} size={collider.bounds.size} trigger={collider.isTrigger}");
        }
        foreach (var name in new[] { "SpawnLeft", "SpawnRight", "Player" })
        {
            Debug.Log($"[CoverGeometry] {name} position={GameObject.Find(name)?.transform.position}");
        }
        var environment = GameObject.Find("Environment");
        if (environment == null)
        {
            throw new InvalidOperationException("Expected saved Game Environment, refusing scene regeneration.");
        }
        var root = GameObject.Find("EnemyCover");
        if (root == null)
        {
            root = new GameObject("EnemyCover");
            root.transform.SetParent(environment.transform, false);
            AddCover(root.transform, "LeftOuter", new Vector3(-18f, 0f, -2f), -1f);
            AddCover(root.transform, "RightOuter", new Vector3(18f, 0f, -2f), 1f);
            AddCover(root.transform, "LeftInner", new Vector3(-9f, 0f, -3f), -1f);
            AddCover(root.transform, "RightInner", new Vector3(9f, 0f, -3f), 1f);
        }
        var player = GameObject.Find("Player");
        var ignore = player.GetComponent<NavMeshModifier>();
        if (ignore == null)
        {
            ignore = player.AddComponent<NavMeshModifier>();
        }
        ignore.ignoreFromBuild = true;

        var navigation = GameObject.Find("EnemyNavigationSurface");
        if (navigation == null)
        {
            navigation = new GameObject("EnemyNavigationSurface");
        }
        var surface = navigation.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = navigation.AddComponent<NavMeshSurface>();
        }
        surface.collectObjects = CollectObjects.Volume;
        surface.center = Vector3.zero;
        surface.size = new Vector3(90f, 12f, 60f);
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.1f;
        surface.BuildNavMesh();
        if (surface.navMeshData == null || NavMesh.CalculateTriangulation().vertices.Length == 0)
        {
            throw new InvalidOperationException("NavMesh bake failed.");
        }
        var previous = AssetDatabase.LoadAssetAtPath<NavMeshData>(NAV_DATA);
        if (previous == null)
        {
            AssetDatabase.CreateAsset(surface.navMeshData, NAV_DATA);
        }
        else
        {
            surface.RemoveData();
            EditorUtility.CopySerialized(surface.navMeshData, previous);
            surface.navMeshData = previous;
            surface.AddData();
            EditorUtility.SetDirty(previous);
        }

        var prefab = PrefabUtility.LoadPrefabContents(ENEMY_PREFAB);
        try
        {
            var motor = prefab.GetComponent<EnemyNavigation>();
            if (motor == null)
            {
                prefab.AddComponent<EnemyNavigation>();
            }
            var agent = prefab.GetComponent<NavMeshAgent>();
            agent.radius = EnemyNavigation.RADIUS;
            agent.height = EnemyNavigation.HEIGHT;
            agent.baseOffset = 0f;
            agent.acceleration = 20f;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = 0.2f;
            PrefabUtility.SaveAsPrefabAsset(prefab, ENEMY_PREFAB);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PocCoverAuthoring] saved selective cover + NavMesh, vertices={NavMesh.CalculateTriangulation().vertices.Length}");
    }

    private static void AddCover(Transform _parent, string _name, Vector3 _position, float _side)
    {
        var nodeObject = new GameObject(_name);
        nodeObject.transform.SetParent(_parent, false);
        nodeObject.transform.position = _position;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "SolidCover";
        wall.transform.SetParent(nodeObject.transform, false);
        wall.transform.localPosition = Vector3.up * 1.1f;
        wall.transform.localScale = new Vector3(1f, 2.2f, 4f);
        wall.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wall.mat");
        var hidden = Child(nodeObject.transform, "Hidden", new Vector3(_side * 1.3f, 0f, 0f));
        var first = Child(nodeObject.transform, "PeekSouth", new Vector3(_side * 1.3f, 0f, -3.5f));
        var second = Child(nodeObject.transform, "PeekNorth", new Vector3(_side * 1.3f, 0f, 3.5f));
        var node = nodeObject.AddComponent<CoverNode>();
        var so = new SerializedObject(node);
        so.FindProperty("solid").objectReferenceValue = wall.GetComponent<Collider>();
        so.FindProperty("hidden").objectReferenceValue = hidden;
        var peeks = so.FindProperty("peeks");
        peeks.arraySize = 2;
        peeks.GetArrayElementAtIndex(0).objectReferenceValue = first;
        peeks.GetArrayElementAtIndex(1).objectReferenceValue = second;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform Child(Transform _parent, string _name, Vector3 _position)
    {
        var go = new GameObject(_name);
        go.transform.SetParent(_parent, false);
        go.transform.localPosition = _position;
        return go.transform;
    }
}
