using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

// 무기 바닥 드롭 전용 저작 도구. 다른 Poc 빌더의 헬퍼는 쓰지 않고, 이 파일 안에서만 프리팹·머티리얼을 만든다.
// CLI: -batchmode -nographics -executeMethod PocWeaponDropAuthoring.AuthorAll -quit
// 프리팹과 입력 참조를 만든다. WireSavedGame은 Game.unity에 바닥 무기 연결을 멱등하게 적용한다(같은 씬에 여러 번 돌려도 한 벌만 남는다).
public static class PocWeaponDropAuthoring
{
    public const string PREFAB_DIR = "Assets/Prefabs/Weapons";
    public const string MATERIAL_DIR = "Assets/Materials/Weapons";
    public const string INPUT_REF_PATH = "Assets/Input/PickupRef.asset";
    public const string GAME_SCENE = "Assets/Scenes/Game.unity";
    public const string HELD_PREFAB_PATH = PREFAB_DIR + "/HeldWeapons.prefab";
    public const string CATALOG_PREFAB_PATH = PREFAB_DIR + "/WeaponPickupCatalog.prefab";
    public const string PICKUP_PREFAB_FORMAT = PREFAB_DIR + "/Pickup_{0}.prefab";

    // PlayerInteractor의 interactLayers(비트 9)와 맞춘다. 이 레이어에 있어야 조준 질의가 픽업을 찾는다.
    private const int PICKUP_LAYER = 9;
    // 들고 있는 무기 모델 위치. PlayerWeapon의 MUZZLE_OFFSET 기준과 맞춘다.
    private static readonly Vector3 HELD_LOCAL_POSITION = new Vector3(0.25f, -0.22f, 0.6f);
    private const string VIEW_PIVOT = "ViewPivot";
    private const string DROP_ROOT_NAME = "WeaponDrops";
    private const string DROPPED_NAME = "Dropped";
    private const string OLD_REVOLVER_NAME = "Revolver";

    // 외형 id(WeaponData.modelId). 같은 계열 총기는 외형을 공유한다.
    public static readonly string[] MODEL_IDS = { "revolver", "bolt", "smg", "shotgun", "colt_saa", "colt_1878", "winchester_1886", "colt_1878_shotgun" };
    private static readonly string[] WEAPON_IDS = MODEL_IDS;

    private static Material metal;
    private static Material wood;
    private static Material accent;

    public static void AuthorAll()
    {
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets/Prefabs", "Weapons");
        EnsureFolder("Assets/Materials", "Weapons");
        EnsureFolder("Assets", "Input");
        LoadMaterials();

        var pickups = new GameObject[WEAPON_IDS.Length];
        for (int i = 0; i < WEAPON_IDS.Length; i++)
        {
            pickups[i] = BuildPickupPrefab(WEAPON_IDS[i]);
        }
        BuildHeldPrefab();
        BuildCatalogPrefab(pickups);
        EnsurePickupReference();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("PocWeaponDropAuthoring: 프리팹과 입력 참조를 만들었습니다.");
    }

    // Game.unity에 바닥 무기 연결을 적용한다. 이미 적용되어 있으면 아무것도 바꾸지 않는다.
    // 적용 항목: PlayerInteractor 픽업 입력, WeaponDrops(카탈로그·Dropped 부모), ViewPivot 아래 HeldWeapons, 구 Revolver 큐브 비활성, GameManager 참조.
    public static void WireSavedGame()
    {
        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var player = GameObject.Find("Player");
        var interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
        var gameManager = Object.FindFirstObjectByType<GameManager>();
        var pivot = player != null ? player.transform.Find(VIEW_PIVOT) : null;
        if (interactor == null || gameManager == null || pivot == null)
        {
            Debug.LogError("PocWeaponDropAuthoring.WireSavedGame: Player/PlayerInteractor/GameManager/ViewPivot 중 찾지 못한 것이 있습니다.");
            return;
        }

        var interactorSo = new SerializedObject(interactor);
        interactorSo.FindProperty("pickupAction").objectReferenceValue = EnsurePickupReference();
        interactorSo.ApplyModifiedPropertiesWithoutUndo();

        var root = GameObject.Find(DROP_ROOT_NAME);
        if (root == null)
        {
            root = new GameObject(DROP_ROOT_NAME);
        }
        var catalog = root.GetComponentInChildren<WeaponPickupCatalog>();
        if (catalog == null)
        {
            var catalogAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CATALOG_PREFAB_PATH);
            catalog = ((GameObject)PrefabUtility.InstantiatePrefab(catalogAsset, root.scene)).GetComponent<WeaponPickupCatalog>();
            catalog.transform.SetParent(root.transform, false);
        }
        var dropped = root.transform.Find(DROPPED_NAME);
        if (dropped == null)
        {
            dropped = new GameObject(DROPPED_NAME).transform;
            dropped.SetParent(root.transform, false);
        }

        var heldInstance = pivot.Find("HeldWeapons");
        if (heldInstance == null)
        {
            var heldAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HELD_PREFAB_PATH);
            heldInstance = ((GameObject)PrefabUtility.InstantiatePrefab(heldAsset, scene)).transform;
            heldInstance.SetParent(pivot, false);
            heldInstance.localPosition = HELD_LOCAL_POSITION;
            heldInstance.localRotation = Quaternion.identity;
        }
        var oldGun = pivot.Find(OLD_REVOLVER_NAME);
        if (oldGun != null && oldGun.gameObject.activeSelf)
        {
            oldGun.gameObject.SetActive(false);
        }

        // 줍기 X 우선순위 판정에 쓰이는 PlayerWeapon 참조(interactor·pickupAction)를 같이 연결한다.
        var playerWeapon = player.GetComponent<PlayerWeapon>();
        if (playerWeapon != null)
        {
            var weaponSo = new SerializedObject(playerWeapon);
            weaponSo.FindProperty("interactor").objectReferenceValue = interactor;
            weaponSo.FindProperty("pickupAction").objectReferenceValue = EnsurePickupReference();
            weaponSo.ApplyModifiedPropertiesWithoutUndo();
        }

        var gmSo = new SerializedObject(gameManager);
        gmSo.FindProperty("pickups").objectReferenceValue = catalog;
        gmSo.FindProperty("heldModels").objectReferenceValue = heldInstance.GetComponent<WeaponHeldModels>();
        gmSo.FindProperty("dropRoot").objectReferenceValue = dropped;
        gmSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("PocWeaponDropAuthoring.WireSavedGame: Game 씬 연결을 적용했습니다(이미 적용돼 있으면 변화 없음).");
    }

    private static GameObject BuildPickupPrefab(string _id)
    {
        var root = new GameObject($"Pickup_{_id}");
        root.layer = PICKUP_LAYER;
        var pickup = root.AddComponent<WeaponPickup>();
        var pickupSo = new SerializedObject(pickup);
        pickupSo.FindProperty("weaponId").stringValue = _id;
        pickupSo.ApplyModifiedPropertiesWithoutUndo();

        // 바닥 높이 0 기준. 모델은 살짝 띄워 바닥에 얹힌 것처럼 보이게 한다.
        var model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        model.localPosition = new Vector3(0f, 0.1f, 0f);
        BuildModel(_id, model);

        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.12f, 0f);
        if (_id == "colt_1878_shotgun") box.center = new Vector3(0f, 0.12f, 0.6f);
        box.size = BoundsSize(_id);
        string path = string.Format(PICKUP_PREFAB_FORMAT, _id);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void BuildHeldPrefab()
    {
        var root = new GameObject("HeldWeapons");
        var held = root.AddComponent<WeaponHeldModels>();
        var entries = new WeaponHeldModels.Entry[WEAPON_IDS.Length];
        for (int i = 0; i < WEAPON_IDS.Length; i++)
        {
            var model = new GameObject(WEAPON_IDS[i]).transform;
            model.SetParent(root.transform, false);
            BuildModel(WEAPON_IDS[i], model);
            entries[i] = new WeaponHeldModels.Entry { id = WEAPON_IDS[i], model = model.gameObject };
        }
        // 아킴보 왼손 외형: 아킴보 허용 무기가 쓰는 외형만, 오른손 모델을 화면 중심 기준 좌우 대칭 자리에 둔다.
        TableManager.ReloadAfterTableEdit();
        var offHand = new System.Collections.Generic.List<WeaponHeldModels.Entry>();
        foreach (string id in WEAPON_IDS)
        {
            if (!AkimboModel(id))
            {
                continue;
            }
            var model = new GameObject(id + "_OffHand").transform;
            model.SetParent(root.transform, false);
            model.localPosition = new Vector3(-2f * HELD_LOCAL_POSITION.x, 0f, 0f);
            BuildModel(id, model);
            model.gameObject.SetActive(false);
            offHand.Add(new WeaponHeldModels.Entry { id = id, model = model.gameObject });
        }
        var heldSo = new SerializedObject(held);
        AssignEntries(heldSo.FindProperty("entries"), entries);
        AssignEntries(heldSo.FindProperty("offHandEntries"), offHand.ToArray());
        heldSo.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, HELD_PREFAB_PATH);
        Object.DestroyImmediate(root);
    }

    // 이 외형을 쓰는 무기 중 아킴보 허용 무기가 있는지.
    private static bool AkimboModel(string _modelId)
    {
        foreach (var weapon in TableManager.Instance.Weapon.weapons)
        {
            if (weapon != null && weapon.modelId == _modelId && WeaponInfo.CanAkimbo(weapon))
            {
                return true;
            }
        }
        return false;
    }

    private static void AssignEntries(SerializedProperty _array, WeaponHeldModels.Entry[] _entries)
    {
        _array.arraySize = _entries.Length;
        for (int i = 0; i < _entries.Length; i++)
        {
            var element = _array.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("id").stringValue = _entries[i].id;
            element.FindPropertyRelative("model").objectReferenceValue = _entries[i].model;
        }
    }

    private static void BuildCatalogPrefab(GameObject[] _pickups)
    {
        var root = new GameObject("WeaponPickupCatalog");
        var catalog = root.AddComponent<WeaponPickupCatalog>();
        var catalogSo = new SerializedObject(catalog);
        var array = catalogSo.FindProperty("entries");
        array.arraySize = _pickups.Length;
        for (int i = 0; i < _pickups.Length; i++)
        {
            var element = array.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("id").stringValue = WEAPON_IDS[i];
            element.FindPropertyRelative("prefab").objectReferenceValue = _pickups[i].GetComponent<WeaponPickup>();
        }
        catalogSo.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, CATALOG_PREFAB_PATH);
        Object.DestroyImmediate(root);
    }

    // 무기 종류마다 알아볼 수 있는 실루엣을 만든다. 리볼버=드럼+짧은 총신, 볼트액션=긴 총신+개머리판+스코프, 기관단총=박스 몸체+탄창.
    private static void BuildModel(string _id, Transform _parent)
    {
        switch (_id)
        {
            case "colt_saa":
                PocWeaponPartsAuthoring.Build(_id, _parent);
                break;
            case "colt_1878":
                PocWeaponPartsAuthoring.Build(_id, _parent);
                break;
            case "winchester_1886":
                PocWeaponPartsAuthoring.Build(_id, _parent);
                break;
            case "revolver":
                Part(PrimitiveType.Cube, "Frame", _parent, new Vector3(0f, 0.05f, 0f), new Vector3(0.08f, 0.12f, 0.42f), metal);
                Part(PrimitiveType.Cylinder, "Drum", _parent, new Vector3(0f, 0.05f, -0.02f), new Vector3(0.11f, 0.04f, 0.11f), metal, new Vector3(90f, 0f, 0f));
                Part(PrimitiveType.Cube, "Barrel", _parent, new Vector3(0f, 0.06f, 0.3f), new Vector3(0.05f, 0.05f, 0.2f), metal);
                Part(PrimitiveType.Cube, "Grip", _parent, new Vector3(0f, -0.09f, -0.16f), new Vector3(0.07f, 0.18f, 0.09f), wood, new Vector3(-15f, 0f, 0f));
                break;
            case "bolt":
                Part(PrimitiveType.Cube, "Barrel", _parent, new Vector3(0f, 0.05f, 0.2f), new Vector3(0.04f, 0.05f, 0.9f), metal);
                Part(PrimitiveType.Cube, "Stock", _parent, new Vector3(0f, -0.02f, -0.35f), new Vector3(0.07f, 0.12f, 0.35f), wood);
                Part(PrimitiveType.Cylinder, "Scope", _parent, new Vector3(0f, 0.11f, 0.1f), new Vector3(0.03f, 0.12f, 0.03f), accent, new Vector3(90f, 0f, 0f));
                Part(PrimitiveType.Cube, "BoltHandle", _parent, new Vector3(0.05f, 0.03f, -0.1f), new Vector3(0.04f, 0.06f, 0.04f), accent);
                break;
            case "smg":
                Part(PrimitiveType.Cube, "Body", _parent, new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.1f, 0.42f), metal);
                Part(PrimitiveType.Cube, "Magazine", _parent, new Vector3(0f, -0.1f, 0.02f), new Vector3(0.06f, 0.16f, 0.06f), metal);
                Part(PrimitiveType.Cube, "Barrel", _parent, new Vector3(0f, 0.04f, 0.3f), new Vector3(0.04f, 0.04f, 0.2f), metal);
                Part(PrimitiveType.Cube, "Stock", _parent, new Vector3(0f, 0.02f, -0.3f), new Vector3(0.05f, 0.08f, 0.2f), wood);
                break;
            case "shotgun":
                Part(PrimitiveType.Cube, "Barrel", _parent, new Vector3(0f, 0.06f, 0.2f), new Vector3(0.05f, 0.05f, 0.75f), metal);
                Part(PrimitiveType.Cube, "Tube", _parent, new Vector3(0f, 0.01f, 0.18f), new Vector3(0.04f, 0.04f, 0.6f), metal);
                Part(PrimitiveType.Cube, "Pump", _parent, new Vector3(0f, 0.01f, 0.25f), new Vector3(0.07f, 0.07f, 0.16f), wood);
                Part(PrimitiveType.Cube, "Stock", _parent, new Vector3(0f, -0.02f, -0.32f), new Vector3(0.07f, 0.12f, 0.32f), wood);
                break;
            case "colt_1878_shotgun":
                BuildImportedModel("colt_1878_shotgun_reference_sightfix", _parent, 0.21f);
                break;
            default:
                Debug.LogError($"PocWeaponDropAuthoring: 알 수 없는 무기 {_id}");
                break;
        }
    }

    // 기존 프리팹의 다른 무기와 씬 참조는 그대로 두고 쌍열 산탄총만 추가한다.
    [MenuItem("POC/Add Colt 1878 Shotgun")]
    public static void AddColtShotgun()
    {
        const string id = "colt_1878_shotgun";
        LoadMaterials();
        var pickup = BuildPickupPrefab(id).GetComponent<WeaponPickup>();
        var heldRoot = PrefabUtility.LoadPrefabContents(HELD_PREFAB_PATH);
        try
        {
            var held = heldRoot.GetComponent<WeaponHeldModels>();
            var model = heldRoot.transform.Find(id);
            if (model == null)
            {
                model = new GameObject(id).transform;
                model.SetParent(heldRoot.transform, false);
                BuildModel(id, model);
                model.gameObject.SetActive(false);
            }
            else if (model.Find("BarrelLeft") != null && model.Find("BarrelRight") != null)
            {
                // 기존에 생성한 임시 쌍열 메시만 교체한다.
                foreach (string name in new[] { "BarrelLeft", "BarrelRight", "Receiver", "ForeEnd", "Stock" })
                {
                    var child = model.Find(name);
                    if (child != null) Object.DestroyImmediate(child.gameObject);
                }
                BuildModel(id, model);
            }
            var so = new SerializedObject(held);
            var entries = so.FindProperty("entries");
            bool found = false;
            for (int i = 0; i < entries.arraySize; i++)
            {
                var element = entries.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("id").stringValue != id) continue;
                element.FindPropertyRelative("model").objectReferenceValue = model.gameObject;
                found = true;
                break;
            }
            if (!found)
            {
                int index = entries.arraySize++;
                var element = entries.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("id").stringValue = id;
                element.FindPropertyRelative("model").objectReferenceValue = model.gameObject;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(heldRoot, HELD_PREFAB_PATH);
        }
        finally { PrefabUtility.UnloadPrefabContents(heldRoot); }

        var catalogRoot = PrefabUtility.LoadPrefabContents(CATALOG_PREFAB_PATH);
        try
        {
            var so = new SerializedObject(catalogRoot.GetComponent<WeaponPickupCatalog>());
            var entries = so.FindProperty("entries");
            int index = -1;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == id) { index = i; break; }
            if (index < 0) index = entries.arraySize++;
            var element = entries.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("id").stringValue = id;
            element.FindPropertyRelative("prefab").objectReferenceValue = pickup;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(catalogRoot, CATALOG_PREFAB_PATH);
        }
        finally { PrefabUtility.UnloadPrefabContents(catalogRoot); }
        AssetDatabase.SaveAssets();
    }

    // AssetFactory FBX는 총구가 -X 방향이므로 게임의 +Z 방향으로 회전한다.
    // 세로 중심을 기존 외형과 맞추고, FBX 자체는 수정하지 않은 채 중첩 프리팹으로 사용한다.
    private static void BuildImportedModel(string _name, Transform _parent, float _verticalCenter)
    {
        string path = $"Assets/Models/AssetFactory/{_name}.fbx";
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            throw new System.InvalidOperationException($"무기 모델을 찾지 못했습니다: {path}");
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        instance.transform.SetParent(_parent, false);
        instance.transform.localPosition = new Vector3(0f, -_verticalCenter, 0f);
        instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
    }

    private static void Part(PrimitiveType _type, string _name, Transform _parent, Vector3 _position, Vector3 _scale, Material _material, Vector3 _euler = default)
    {
        var go = GameObject.CreatePrimitive(_type);
        go.name = _name;
        go.transform.SetParent(_parent, false);
        go.transform.localPosition = _position;
        go.transform.localRotation = Quaternion.Euler(_euler);
        go.transform.localScale = _scale;
        go.GetComponent<Renderer>().sharedMaterial = _material;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    private static Vector3 BoundsSize(string _id)
    {
        switch (_id)
        {
            case "colt_saa":
            case "colt_1878":
                return new Vector3(0.14f, 0.2f, 0.4f);
            case "winchester_1886":
                return new Vector3(0.14f, 0.23f, 1.2f);
            case "bolt":
                return new Vector3(0.14f, 0.2f, 1f);
            case "smg":
                return new Vector3(0.14f, 0.2f, 0.5f);
            case "shotgun":
                return new Vector3(0.14f, 0.2f, 0.95f);
            case "colt_1878_shotgun":
                return new Vector3(0.14f, 0.23f, 1.25f);
            default:
                return new Vector3(0.14f, 0.2f, 0.56f);
        }
    }

    private static InputActionReference EnsurePickupReference()
    {
        var existing = AssetDatabase.LoadAssetAtPath<InputActionReference>(INPUT_REF_PATH);
        if (existing != null)
        {
            return existing;
        }
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var action = asset.FindAction("Player/Pickup");
        if (action == null)
        {
            Debug.LogError("PocWeaponDropAuthoring: Player/Pickup 액션을 찾지 못했습니다.");
            return null;
        }
        var reference = ScriptableObject.CreateInstance<InputActionReference>();
        reference.Set(action);
        AssetDatabase.CreateAsset(reference, INPUT_REF_PATH);
        return reference;
    }

    private static void LoadMaterials()
    {
        metal = Mat("WeaponMetal", new Color(0.2f, 0.2f, 0.22f));
        wood = Mat("WeaponWood", new Color(0.4f, 0.25f, 0.12f));
        accent = Mat("WeaponAccent", new Color(0.55f, 0.45f, 0.12f));
    }

    private static Material Mat(string _name, Color _color)
    {
        string path = $"{MATERIAL_DIR}/{_name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", _color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string _parent, string _name)
    {
        string path = $"{_parent}/{_name}";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(_parent, _name);
        }
    }
}
