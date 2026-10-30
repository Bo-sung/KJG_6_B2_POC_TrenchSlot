using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 입력 참조·재질·프리팹을 에디터에서 만든다. 씬 조립은 PocSceneBuilder가 맡는다.
// 런타임에 레벨·UI를 생성하지 않도록, 모든 뷰 구조는 여기서 프리팹으로 굽는다.
public static class PocAssetBuilder
{
    public const string PREFAB_DIR = "Assets/Prefabs";
    public const string GAMEPLAY_PREFAB_DIR = "Assets/Prefabs/Gameplay";
    public const string EFFECT_PREFAB_DIR = "Assets/Resources/Effects";
    public const string PROJECTILE_PREFAB_DIR = "Assets/Resources/Projectiles";
    public const string UI_PREFAB_DIR = "Assets/Prefabs/UI";
    public const string MATERIAL_DIR = "Assets/Materials";
    public const string INPUT_DIR = "Assets/Input";
    public const string FONT_BOLD = "Assets/Fonts/NanumSquareNeo/TMP/NanumSquareNeo-Bold Dynamic.asset";
    public const string FONT_REGULAR = "Assets/Fonts/NanumSquareNeo/TMP/NanumSquareNeo-Regular Dynamic.asset";
    public const string URP_LIT = "Universal Render Pipeline/Lit";

    public const int ENEMY_LAYER = 8;
    public const int INTERACT_LAYER = 9;

    public static TMP_FontAsset BoldFont;
    public static TMP_FontAsset RegularFont;

    public static void EnsureFolders()
    {
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets/Prefabs", "Gameplay");
        EnsureFolder("Assets/Prefabs", "UI");
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets", "Input");
        EnsureFolder("Assets", "Scenes");
    }

    public static void EnsureFolder(string _parent, string _name)
    {
        if (!AssetDatabase.IsValidFolder(_parent + "/" + _name))
        {
            AssetDatabase.CreateFolder(_parent, _name);
        }
    }

    public static void LoadFonts()
    {
        BoldFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_BOLD);
        RegularFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_REGULAR);
        if (BoldFont == null || RegularFont == null)
        {
            Debug.LogError("PocAssetBuilder: NanumSquareNeo TMP 폰트를 찾지 못했습니다.");
        }
    }

    // 레이어 8=Enemy, 9=Interactable을 TagManager에 등록한다.
    public static void ConfigureLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        SetLayer(layers, ENEMY_LAYER, "Enemy");
        SetLayer(layers, INTERACT_LAYER, "Interactable");
        tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetLayer(SerializedProperty _layers, int _index, string _name)
    {
        var element = _layers.GetArrayElementAtIndex(_index);
        if (element.stringValue != _name)
        {
            element.stringValue = _name;
        }
    }

    // 플레이어 입력을 InputActionReference 에셋으로 만들어 PlayerMovement 등이 참조하게 한다.
    public static InputActionReference InputRef(string _actionPath, string _assetName)
    {
        string path = $"{INPUT_DIR}/{_assetName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<InputActionReference>(path);
        if (existing != null)
        {
            return existing;
        }
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var action = asset.FindAction(_actionPath);
        if (action == null)
        {
            Debug.LogError($"PocAssetBuilder: 입력 액션 {_actionPath}를 찾지 못했습니다.");
            return null;
        }
        var reference = ScriptableObject.CreateInstance<InputActionReference>();
        reference.Set(action);
        AssetDatabase.CreateAsset(reference, path);
        return reference;
    }

    public static Material Mat(string _name, Color _color, bool _emissive = false, Color _emission = default)
    {
        string path = $"{MATERIAL_DIR}/{_name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(URP_LIT));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", _color);
        if (_emissive)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", _emission);
        }
        else
        {
            material.DisableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    public static void SavePrefab(GameObject _root, string _path)
    {
        PrefabUtility.SaveAsPrefabAsset(_root, _path);
        Object.DestroyImmediate(_root);
    }

    // 원시 도형의 콜라이더는 물리 판정을 따로 두므로 시각용 자식에서는 제거한다.
    public static GameObject Primitive(PrimitiveType _type, string _name, Transform _parent, Vector3 _position, Vector3 _scale, Material _material)
    {
        var go = GameObject.CreatePrimitive(_type);
        go.name = _name;
        go.transform.SetParent(_parent, false);
        go.transform.localPosition = _position;
        go.transform.localScale = _scale;
        go.GetComponent<Renderer>().sharedMaterial = _material;
        return go;
    }

    public static void StripCollider(GameObject _go)
    {
        var collider = _go.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }
    }

    public static GameObject NewEmpty(string _name, Transform _parent, Vector3 _position)
    {
        var go = new GameObject(_name);
        go.transform.SetParent(_parent, false);
        go.transform.localPosition = _position;
        return go;
    }

    // 프리팹 세부 구조: 적 한 명. 본체는 캡슐이며 색은 Enemy가 런타임에 프로퍼티 블록으로 입힌다.
    // 손에 드는 총은 WeaponMount 아래에 무기 id 이름의 모델 3개로 두고, Enemy가 웨이브 무기 하나만 켠다(EnemyWeaponData.json).
    public static void BuildEnemyPrefab()
    {
        var root = new GameObject("Enemy");
        root.layer = ENEMY_LAYER;
        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.4f;
        root.AddComponent<EnemyNavigation>();
        var enemy = root.AddComponent<Enemy>();

        var body = Primitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.8f, 0.9f, 0.8f), Mat("EnemyBody", Color.white));
        StripCollider(body);
        body.layer = ENEMY_LAYER;

        var mount = NewEmpty("WeaponMount", root.transform, new Vector3(0.2f, 1.15f, 0.2f));
        var wood = Mat("EnemyGunWood", new Color(0.35f, 0.22f, 0.12f));
        var metal = Mat("EnemyGunMetal", new Color(0.18f, 0.18f, 0.18f));
        // 머스켓: 긴 총신, 나무 개머리판. 1880년대 전장식 단발 총.
        BuildGunModel(mount.transform, "musket", 1.0f, false, wood, metal);
        // WWI 볼트액션 소총: 머스켓보다 짧은 총신, 탄창 상자 없음. 외형 차이는 총신 길이로만 둔다.
        BuildGunModel(mount.transform, "wwi_rifle", 0.9f, false, wood, metal);
        // WWII 소총: 탄창 상자와 조준경이 붙은 반자동 소총.
        BuildGunModel(mount.transform, "wwii_rifle", 0.95f, true, wood, metal);

        var meleeWeapon = PocEnemyWeaponAuthoring.EnsureMeleeWeapon(root);

        var so = new SerializedObject(enemy);
        so.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<Renderer>();
        so.FindProperty("weaponMount").objectReferenceValue = mount.transform;
        so.FindProperty("meleeWeapon").objectReferenceValue = meleeWeapon;
        so.ApplyModifiedPropertiesWithoutUndo();
        SavePrefab(root, $"{GAMEPLAY_PREFAB_DIR}/Enemy.prefab");
        // 전체 프리팹 재생성 시에도 적이 플레이어 무기 외형을 공유한다.
        PocEnemyWeaponAuthoring.Apply();
    }

    // 총 모델 하나: 손(부모 원점) 기준 +Z 방향으로 총신이 뻗는다. 적은 플레이어를 향해 회전하므로 +Z가 정면이다.
    private static void BuildGunModel(Transform _mount, string _id, float _barrelLength, bool _hasMagazine, Material _wood, Material _metal)
    {
        var gun = NewEmpty(_id, _mount, Vector3.zero);
        var barrel = Primitive(PrimitiveType.Cube, "Barrel", gun.transform, new Vector3(0f, 0f, _barrelLength * 0.5f), new Vector3(0.04f, 0.04f, _barrelLength), _metal);
        StripCollider(barrel);
        var stock = Primitive(PrimitiveType.Cube, "Stock", gun.transform, new Vector3(0f, -0.03f, -0.15f), new Vector3(0.07f, 0.12f, 0.3f), _wood);
        StripCollider(stock);
        if (_hasMagazine)
        {
            var magazine = Primitive(PrimitiveType.Cube, "Magazine", gun.transform, new Vector3(0f, -0.08f, 0.35f), new Vector3(0.06f, 0.1f, 0.12f), _metal);
            StripCollider(magazine);
            var sight = Primitive(PrimitiveType.Cube, "Sight", gun.transform, new Vector3(0f, 0.08f, 0.5f), new Vector3(0.03f, 0.05f, 0.12f), _metal);
            StripCollider(sight);
        }
    }

    // 연출 프리팹(임시 에셋): 머즐 플래시·타격 스파크. 발광 구체 한 개씩이며 Resources/Effects에 둬서 런타임이 이름으로 불러온다.
    // 정식 이펙트 에셋이 생기면 이 두 프리팹만 교체한다.
    public static void BuildEffectPrefabs()
    {
        EnsureFolder("Assets/Resources", "Effects");
        var glow = Mat("EffectMuzzleGlow", new Color(1f, 0.85f, 0.4f), true, new Color(1f, 0.7f, 0.2f) * 3f);
        var spark = Mat("EffectHitSpark", new Color(1f, 0.9f, 0.5f), true, new Color(1f, 0.8f, 0.3f) * 2f);

        var flash = new GameObject("MuzzleFlash");
        flash.AddComponent<TransientEffect>();
        var flashBall = Primitive(PrimitiveType.Sphere, "Flash", flash.transform, Vector3.zero, new Vector3(0.12f, 0.12f, 0.12f), glow);
        StripCollider(flashBall);
        SavePrefab(flash, $"{EFFECT_PREFAB_DIR}/MuzzleFlash.prefab");

        var hit = new GameObject("HitSpark");
        hit.AddComponent<TransientEffect>();
        var sparkBall = Primitive(PrimitiveType.Sphere, "Spark", hit.transform, Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f), spark);
        StripCollider(sparkBall);
        SavePrefab(hit, $"{EFFECT_PREFAB_DIR}/HitSpark.prefab");
    }

    // 발사체 프리팹: 작은 구체 + 트레일. 콜라이더는 두지 않는다(충돌은 Projectile의 스윕이 판정).
    // 탄 아트가 생기면 이 프리팹만 교체한다. 탄 수치는 테이블(WeaponData·EnemyWeaponData)에 있다.
    public static void BuildProjectilePrefabs()
    {
        EnsureFolder("Assets/Resources", "Projectiles");
        BuildProjectilePrefab("PlayerBullet", new Color(1f, 0.9f, 0.4f));
        BuildProjectilePrefab("EnemyBullet", new Color(1f, 0.35f, 0.2f));
    }

    private static void BuildProjectilePrefab(string _name, Color _color)
    {
        var root = new GameObject(_name);
        root.AddComponent<Projectile>();
        var glow = Mat($"Projectile{_name}", _color, true, _color * 2f);
        var ball = Primitive(PrimitiveType.Sphere, "Bullet", root.transform, Vector3.zero, new Vector3(0.08f, 0.08f, 0.08f), glow);
        StripCollider(ball);
        var trail = root.AddComponent<TrailRenderer>();
        trail.time = 0.12f;
        trail.startWidth = 0.05f;
        trail.endWidth = 0f;
        trail.sharedMaterial = glow;
        SavePrefab(root, $"{PROJECTILE_PREFAB_DIR}/{_name}.prefab");
    }

    // 프리팹 세부 구조: 슬롯머신. 캐비닛·마키·릴 3개(면 회전 축 X)·레버·지급 파티클. 씬 참조(게임 매니저·무기)는 씬 인스턴스에서 연결한다.
    public static void BuildSlotMachinePrefab()
    {
        var root = new GameObject("SlotMachine");
        root.layer = INTERACT_LAYER;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.2f, 0f);
        box.size = new Vector3(2.4f, 2.4f, 1.4f);
        var presenter = root.AddComponent<SlotMachinePresenter>();
        var reels = root.AddComponent<SlotMachineReels>();
        var presenterSo = new SerializedObject(presenter);
        presenterSo.FindProperty("reels").objectReferenceValue = reels;
        presenterSo.ApplyModifiedPropertiesWithoutUndo();

        var cabinetMaterial = Mat("Cabinet", new Color(0.35f, 0.1f, 0.1f));
        var marqueeMaterial = Mat("Marquee", new Color(0.15f, 0.15f, 0.15f));
        var glowMaterial = Mat("MarqueeGlow", new Color(1f, 0.85f, 0.2f), true, new Color(1f, 0.8f, 0.1f) * 3f);
        var leverMaterial = Mat("Lever", new Color(0.55f, 0.55f, 0.55f));
        var reelMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Textures/SlotMachine/ReelAtlas.mat");

        var cabinet = Primitive(PrimitiveType.Cube, "Cabinet", root.transform, new Vector3(0f, 1.2f, 0f), new Vector3(2.2f, 2.4f, 1.2f), cabinetMaterial);
        StripCollider(cabinet);
        var marquee = Primitive(PrimitiveType.Cube, "Marquee", root.transform, new Vector3(0f, 2.55f, -0.62f), new Vector3(2.0f, 0.4f, 0.05f), marqueeMaterial);
        StripCollider(marquee);

        var reelTransforms = new Transform[SlotMachineInfo.REEL_COUNT];
        float[] reelX = { -0.6f, 0f, 0.6f };
        for (int i = 0; i < SlotMachineInfo.REEL_COUNT; i++)
        {
            var pivot = NewEmpty($"Reel{i}", root.transform, new Vector3(reelX[i], 1.4f, -0.62f));
            reelTransforms[i] = pivot.transform;
            var drum = Primitive(PrimitiveType.Cylinder, "Drum", pivot.transform, Vector3.zero, new Vector3(0.5f, 0.2f, 0.5f), reelMaterial);
            drum.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            StripCollider(drum);
        }

        var lever = NewEmpty("Lever", root.transform, new Vector3(1.25f, 1.4f, -0.62f));
        var arm = Primitive(PrimitiveType.Cube, "LeverArm", lever.transform, new Vector3(0f, 0.4f, 0f), new Vector3(0.08f, 0.8f, 0.08f), leverMaterial);
        StripCollider(arm);

        var fxObject = NewEmpty("PayoutFx", root.transform, new Vector3(0f, 2.2f, -0.8f));
        var particles = fxObject.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.5f;
        main.startLifetime = 1.2f;
        main.startSpeed = 2.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var reelSo = new SerializedObject(reels);
        var reelArray = reelSo.FindProperty("reels");
        reelArray.arraySize = SlotMachineInfo.REEL_COUNT;
        for (int i = 0; i < SlotMachineInfo.REEL_COUNT; i++)
        {
            reelArray.GetArrayElementAtIndex(i).objectReferenceValue = reelTransforms[i];
        }
        reelSo.FindProperty("lever").objectReferenceValue = lever.transform;
        reelSo.FindProperty("marquee").objectReferenceValue = marquee.GetComponent<Renderer>();
        reelSo.FindProperty("marqueeGlow").objectReferenceValue = glowMaterial;
        reelSo.FindProperty("payoutParticles").objectReferenceValue = particles;
        reelSo.ApplyModifiedPropertiesWithoutUndo();

        SavePrefab(root, $"{GAMEPLAY_PREFAB_DIR}/SlotMachine.prefab");
    }

    // ---------- UI helpers ----------

    public static Canvas CanvasRoot(string _name, int _sortOrder)
    {
        var go = new GameObject(_name, typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = _sortOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        Stretch(go.GetComponent<RectTransform>());
        return canvas;
    }

    public static GameObject UiObject(string _name, Transform _parent)
    {
        var go = new GameObject(_name, typeof(RectTransform));
        go.transform.SetParent(_parent, false);
        return go;
    }

    public static void Stretch(RectTransform _rect)
    {
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.one;
        _rect.offsetMin = Vector2.zero;
        _rect.offsetMax = Vector2.zero;
    }

    public static void Place(RectTransform _rect, Vector2 _anchor, Vector2 _pivot, Vector2 _position, Vector2 _box)
    {
        _rect.anchorMin = _anchor;
        _rect.anchorMax = _anchor;
        _rect.pivot = _pivot;
        _rect.anchoredPosition = _position;
        _rect.sizeDelta = _box;
    }

    public static Image Panel(Transform _parent, string _name, Color _color)
    {
        var go = UiObject(_name, _parent);
        var image = go.AddComponent<Image>();
        image.color = _color;
        Stretch(go.GetComponent<RectTransform>());
        return image;
    }

    public static TextMeshProUGUI Label(Transform _parent, string _name, string _text, int _fontSize, TextAlignmentOptions _align, Vector2 _anchor, Vector2 _pivot, Vector2 _position, Vector2 _box, bool _bold = true)
    {
        var go = UiObject(_name, _parent);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = _bold ? BoldFont : RegularFont;
        text.text = _text;
        text.fontSize = _fontSize;
        text.alignment = _align;
        text.color = Color.white;
        Place(go.GetComponent<RectTransform>(), _anchor, _pivot, _position, _box);
        return text;
    }

    public static Button MakeButton(Transform _parent, string _name, string _label, Vector2 _anchor, Vector2 _pivot, Vector2 _position, Vector2 _box)
    {
        var go = UiObject(_name, _parent);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.22f, 0.22f, 0.22f, 0.95f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        Place(go.GetComponent<RectTransform>(), _anchor, _pivot, _position, _box);
        Label(go.transform, "Label", _label, 40, TextAlignmentOptions.Center, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, _box);
        Stretch(go.transform.Find("Label").GetComponent<RectTransform>());
        return button;
    }
}
