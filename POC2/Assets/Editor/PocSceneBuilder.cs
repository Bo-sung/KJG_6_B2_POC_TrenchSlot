using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Unity CLI 진입점(-executeMethod PocSceneBuilder.BuildAll). 프리팹과 씬을 에디터에서 굽고 빌드 설정을 맞춘다.
// 런타임 레벨·UI 생성은 하지 않으며, 여기서 만든 자산을 씬이 참조한다.
public static class PocSceneBuilder
{
    private const string SCENE_PRELOAD = "Assets/Scenes/Preload.unity";
    private const string SCENE_TITLE = "Assets/Scenes/Title.unity";
    private const string SCENE_GAME = "Assets/Scenes/Game.unity";
    private const string AUDIO_DIR = "Assets/Audio";
    private const string PACHINKO_CLIP = "Assets/Resources/Sound/SFX/Pachinko/Pachinko-slot.mp3";

    public static void BuildAll()
    {
        PocAssetBuilder.EnsureFolders();
        PocAssetBuilder.ConfigureLayers();
        PocAssetBuilder.LoadFonts();
        AssetDatabase.Refresh();

        PocAssetBuilder.InputRef("Player/Move", "MoveRef");
        PocAssetBuilder.InputRef("Player/Look", "LookRef");
        PocAssetBuilder.InputRef("Player/Attack", "AttackRef");
        PocAssetBuilder.InputRef("Player/Interact", "InteractRef");
        PocAssetBuilder.InputRef("Player/Reload", "ReloadRef");

        PocAssetBuilder.BuildEnemyPrefab();
        PocAssetBuilder.BuildEffectPrefabs();
        PocAssetBuilder.BuildProjectilePrefabs();
        PocAssetBuilder.BuildSlotMachinePrefab();
        BuildHudPrefab();
        BuildShopPrefab();
        BuildGameOverPrefab();
        BuildTitlePrefab();

        BuildPreloadScene();
        BuildTitleScene();
        BuildGameScene();
        // 향후 명시적인 전체 재생성에도 엄폐/베이크 연결이 빠지지 않게 한다.
        // 기존 씬에 추가할 때는 이 BuildAll 대신 PocCoverAuthoring.AuthorSavedGame만 호출한다.
        PocCoverAuthoring.AuthorSavedGame();

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(SCENE_PRELOAD, true),
            new EditorBuildSettingsScene(SCENE_TITLE, true),
            new EditorBuildSettingsScene(SCENE_GAME, true)
        };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PocSceneBuilder] BuildAll complete");
    }

    // 씬을 건드리지 않고 HUD 프리팹만 다시 굽는다(Unity CLI -executeMethod PocSceneBuilder.BuildHudPrefabOnly).
    // 전체 BuildAll은 씬까지 덮어쓰므로, 사용자가 열어 둔 씬을 보호해야 할 때는 이 진입점만 쓴다.
    public static void BuildHudPrefabOnly()
    {
        PocAssetBuilder.EnsureFolders();
        PocAssetBuilder.LoadFonts();
        AssetDatabase.Refresh();
        BuildHudPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[PocSceneBuilder] BuildHudPrefabOnly complete");
    }

    // ---------- UI prefabs ----------

    private static void BuildHudPrefab()
    {
        var canvas = PocAssetBuilder.CanvasRoot("HUD", 0);
        var view = canvas.gameObject.AddComponent<HudView>();
        var root = canvas.transform;

        // 피해 점멸은 맨 아래 층(다른 HUD 글자 뒤)에 두고, 클릭을 막지 않는다.
        var damageFlash = PocAssetBuilder.Panel(root, "DamageFlash", new Color(0.8f, 0.05f, 0.05f, 0f));
        damageFlash.raycastTarget = false;

        PocAssetBuilder.Label(root, "Crosshair", "+", 48, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 80f));
        var hitObject = PocAssetBuilder.UiObject("HitMarker", root);
        var hitGroup = hitObject.AddComponent<CanvasGroup>();
        hitGroup.alpha = 0f;
        hitGroup.blocksRaycasts = false;
        hitGroup.interactable = false;
        PocAssetBuilder.Place(hitObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 120f));
        var hitMarker = PocAssetBuilder.Label(hitObject.transform, "HitMarkerText", "", 56, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 120f));
        var health =PocAssetBuilder.Label(root, "Health", "HP", 40, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(600f, 50f));
        var barBg = PocAssetBuilder.Panel(root, "HealthBarBg", new Color(0.15f, 0.15f, 0.15f, 0.8f));
        PocAssetBuilder.Place(barBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -100f), new Vector2(400f, 24f));
        var fillObject = PocAssetBuilder.UiObject("HealthFill", barBg.transform);
        var fill = fillObject.AddComponent<Image>();
        fill.color = new Color(0.85f, 0.15f, 0.15f);
        fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        PocAssetBuilder.Stretch(fill.rectTransform);

        var wave = PocAssetBuilder.Label(root, "Wave", "WAVE 1", 44, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(800f, 60f));
        var timer = PocAssetBuilder.Label(root, "Timer", "0초", 40, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(400f, 50f));
        var money = PocAssetBuilder.Label(root, "Money", "소지금", 40, TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(600f, 50f));
        var kills = PocAssetBuilder.Label(root, "Kills", "처치 0", 32, TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -100f), new Vector2(600f, 40f));
        var ammo = PocAssetBuilder.Label(root, "Ammo", "-- / --", 56, TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(600f, 140f));
        ammo.verticalAlignment = VerticalAlignmentOptions.Bottom;
        var prompt = PocAssetBuilder.Label(root, "Prompt", "", 36, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(1000f, 60f));

        var bannerObject = PocAssetBuilder.UiObject("Banner", root);
        var bannerGroup = bannerObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
        var banner = PocAssetBuilder.Label(bannerObject.transform, "BannerText", "", 52, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1400f, 80f));
        PocAssetBuilder.Place(bannerObject.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1400f, 80f));

        var resultObject = PocAssetBuilder.UiObject("Result", root);
        var resultGroup = resultObject.AddComponent<CanvasGroup>();
        resultGroup.alpha = 0f;
        var result = PocAssetBuilder.Label(resultObject.transform, "ResultText", "", 44, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(1200f, 80f));
        PocAssetBuilder.Place(resultObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(1200f, 80f));

        var so = new SerializedObject(view);
        so.FindProperty("healthText").objectReferenceValue = health;
        so.FindProperty("healthFill").objectReferenceValue = fill;
        so.FindProperty("moneyText").objectReferenceValue = money;
        so.FindProperty("waveText").objectReferenceValue = wave;
        so.FindProperty("timerText").objectReferenceValue = timer;
        so.FindProperty("ammoText").objectReferenceValue = ammo;
        so.FindProperty("killsText").objectReferenceValue = kills;
        so.FindProperty("promptText").objectReferenceValue = prompt;
        so.FindProperty("bannerText").objectReferenceValue = banner;
        so.FindProperty("bannerGroup").objectReferenceValue = bannerGroup;
        so.FindProperty("resultText").objectReferenceValue = result;
        so.FindProperty("resultGroup").objectReferenceValue = resultGroup;
        so.FindProperty("hitMarkerGroup").objectReferenceValue = hitGroup;
        so.FindProperty("hitMarkerText").objectReferenceValue = hitMarker;
        so.FindProperty("damageFlash").objectReferenceValue = damageFlash;
        so.ApplyModifiedPropertiesWithoutUndo();

        PocAssetBuilder.SavePrefab(canvas.gameObject, $"{PocAssetBuilder.UI_PREFAB_DIR}/HUD.prefab");
    }

    private static void BuildShopPrefab()
    {
        var canvas = PocAssetBuilder.CanvasRoot("Shop", 10);
        var view = canvas.gameObject.AddComponent<ShopView>();
        var root = canvas.transform;

        var panel = PocAssetBuilder.Panel(root, "Panel", new Color(0.05f, 0.05f, 0.07f, 0.88f));
        var title = PocAssetBuilder.Label(panel.transform, "Title", "정비 시간", 60, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(800f, 90f));
        var wallet = PocAssetBuilder.Label(panel.transform, "Wallet", "소지금", 40, TextAlignmentOptions.Right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -60f), new Vector2(600f, 50f));
        var intermission = PocAssetBuilder.Label(panel.transform, "Intermission", "", 36, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(800f, 50f));
        var message = PocAssetBuilder.Label(panel.transform, "Message", "", 36, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(1200f, 50f));

        var containerObject = PocAssetBuilder.UiObject("Slots", panel.transform);
        PocAssetBuilder.Place(containerObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(1400f, 560f));
        var grid = containerObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(320f, 260f);
        grid.spacing = new Vector2(24f, 24f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.MiddleCenter;

        const int SLOT_COUNT = 7;
        var buttons = new Button[SLOT_COUNT];
        var labels = new TextMeshProUGUI[SLOT_COUNT];
        var prices = new TextMeshProUGUI[SLOT_COUNT];
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var slot = PocAssetBuilder.UiObject($"Slot{i}", containerObject.transform);
            var image = slot.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.24f, 0.95f);
            var button = slot.AddComponent<Button>();
            button.targetGraphic = image;
            buttons[i] = button;
            labels[i] = PocAssetBuilder.Label(slot.transform, "Label", "", 34, TextAlignmentOptions.Top, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(300f, 170f));
            PocAssetBuilder.Place(labels[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(300f, 180f));
            prices[i] = PocAssetBuilder.Label(slot.transform, "Price", "", 40, TextAlignmentOptions.Bottom, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(300f, 50f));
        }

        var nextWave = PocAssetBuilder.MakeButton(panel.transform, "NextWave", "다음 웨이브", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 60f), new Vector2(360f, 90f));

        var so = new SerializedObject(view);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("walletText").objectReferenceValue = wallet;
        so.FindProperty("intermissionText").objectReferenceValue = intermission;
        so.FindProperty("messageText").objectReferenceValue = message;
        so.FindProperty("nextWaveButton").objectReferenceValue = nextWave;
        AssignArray(so.FindProperty("slotButtons"), buttons);
        AssignArray(so.FindProperty("slotLabels"), labels);
        AssignArray(so.FindProperty("slotPrices"), prices);
        so.ApplyModifiedPropertiesWithoutUndo();

        PocAssetBuilder.SavePrefab(canvas.gameObject, $"{PocAssetBuilder.UI_PREFAB_DIR}/Shop.prefab");
    }

    private static void BuildGameOverPrefab()
    {
        var canvas = PocAssetBuilder.CanvasRoot("GameOver", 20);
        var view = canvas.gameObject.AddComponent<GameOverView>();
        var root = canvas.transform;

        var panel = PocAssetBuilder.Panel(root, "Panel", new Color(0f, 0f, 0f, 0.85f));
        PocAssetBuilder.Label(panel.transform, "Title", "사망", 96, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(800f, 140f));
        var summary = PocAssetBuilder.Label(panel.transform, "Summary", "", 48, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(900f, 180f));
        var titleButton = PocAssetBuilder.MakeButton(panel.transform, "TitleButton", "타이틀로", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(400f, 100f));

        var so = new SerializedObject(view);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("summaryText").objectReferenceValue = summary;
        so.FindProperty("titleButton").objectReferenceValue = titleButton;
        so.ApplyModifiedPropertiesWithoutUndo();

        PocAssetBuilder.SavePrefab(canvas.gameObject, $"{PocAssetBuilder.UI_PREFAB_DIR}/GameOver.prefab");
    }

    private static void BuildTitlePrefab()
    {
        var canvas = PocAssetBuilder.CanvasRoot("Title", 0);
        var view = canvas.gameObject.AddComponent<TitleView>();
        var root = canvas.transform;

        PocAssetBuilder.Panel(root, "Background", new Color(0.08f, 0.08f, 0.1f, 1f));
        PocAssetBuilder.Label(root, "Title", "POC2 · 참호 슬롯", 96, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), new Vector2(1400f, 140f));
        PocAssetBuilder.Label(root, "Body", "WASD 이동 · 마우스 조준 · 좌클릭 사격 · R 재장전\n웨이브 중 E로 슬롯 회전 (회전 중에도 사격 가능)\n정비 시간에 상점에서 무기 · 체력 · 퍽 구매", 40, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1400f, 240f), false);
        var start = PocAssetBuilder.MakeButton(root, "StartButton", "시작", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -140f), new Vector2(360f, 100f));
        var quit = PocAssetBuilder.MakeButton(root, "QuitButton", "종료", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -270f), new Vector2(360f, 100f));

        var so = new SerializedObject(view);
        so.FindProperty("startButton").objectReferenceValue = start;
        so.FindProperty("quitButton").objectReferenceValue = quit;
        so.ApplyModifiedPropertiesWithoutUndo();

        PocAssetBuilder.SavePrefab(canvas.gameObject, $"{PocAssetBuilder.UI_PREFAB_DIR}/Title.prefab");
    }

    // 배열 직렬화 필드에 오브젝트 목록을 넣는다.
    private static void AssignArray<T>(SerializedProperty _property, T[] _values) where T : Object
    {
        _property.arraySize = _values.Length;
        for (int i = 0; i < _values.Length; i++)
        {
            _property.GetArrayElementAtIndex(i).objectReferenceValue = _values[i];
        }
    }

    // ---------- Scenes ----------

    private static void BuildPreloadScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CreateMainCamera(new Color(0.02f, 0.02f, 0.02f));
        new GameObject("Bootstrap").AddComponent<PreloadController>();
        EditorSceneManager.SaveScene(scene, SCENE_PRELOAD);
    }

    private static void BuildTitleScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CreateMainCamera(new Color(0.08f, 0.08f, 0.1f));
        CreateEventSystem();
        var titleInstance = InstantiatePrefab($"{PocAssetBuilder.UI_PREFAB_DIR}/Title.prefab");
        var presenters = new GameObject("Presenters");
        var titlePresenter = presenters.AddComponent<TitlePresenter>();
        var titleSo = new SerializedObject(titlePresenter);
        titleSo.FindProperty("view").objectReferenceValue = titleInstance.GetComponent<TitleView>();
        titleSo.ApplyModifiedPropertiesWithoutUndo();
        CreateSoundPlayer();
        EditorSceneManager.SaveScene(scene, SCENE_TITLE);
    }

    private static void BuildGameScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sun = new GameObject("Directional Light");
        var light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var environment = new GameObject("Environment");
        var ground = PocAssetBuilder.Primitive(PrimitiveType.Cube, "Ground", environment.transform, new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f), PocAssetBuilder.Mat("Ground", new Color(0.23f, 0.25f, 0.16f)));
        ground.layer = 0;
        var wallMaterial = PocAssetBuilder.Mat("Wall", new Color(0.42f, 0.35f, 0.25f));
        CreateWall(environment.transform, "TrenchNorth", new Vector3(-12f, 0.6f, -10f), new Vector3(14f, 1.2f, 1f), wallMaterial);
        CreateWall(environment.transform, "TrenchNorthEast", new Vector3(12f, 0.6f, -10f), new Vector3(14f, 1.2f, 1f), wallMaterial);
        CreateWall(environment.transform, "TrenchSouth", new Vector3(0f, 0.6f, 6f), new Vector3(30f, 1.2f, 1f), wallMaterial);
        CreateWall(environment.transform, "TrenchWestSide", new Vector3(-26f, 0.6f, -3f), new Vector3(1f, 1.2f, 10f), wallMaterial);
        CreateWall(environment.transform, "TrenchEastSide", new Vector3(26f, 0.6f, -3f), new Vector3(1f, 1.2f, 10f), wallMaterial);

        var spawnLeft = PocAssetBuilder.NewEmpty("SpawnLeft", environment.transform, new Vector3(-26f, 0f, 4f));
        var spawnRight = PocAssetBuilder.NewEmpty("SpawnRight", environment.transform, new Vector3(26f, 0f, 4f));

        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0f, -6f);
        var controller = player.AddComponent<CharacterController>();
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.height = 1.8f;
        controller.radius = 0.4f;
        var health = player.AddComponent<PlayerHealth>();
        var movement = player.AddComponent<PlayerMovement>();
        var weapon = player.AddComponent<PlayerWeapon>();
        var interactor = player.AddComponent<PlayerInteractor>();

        var pivot = PocAssetBuilder.NewEmpty("ViewPivot", player.transform, new Vector3(0f, 1.6f, 0f));
        var camera = CreateMainCamera(new Color(0.55f, 0.6f, 0.65f), pivot.transform);
        var gun = PocAssetBuilder.Primitive(PrimitiveType.Cube, "Revolver", pivot.transform, new Vector3(0.25f, -0.22f, 0.6f), new Vector3(0.12f, 0.15f, 0.5f), PocAssetBuilder.Mat("Revolver", new Color(0.2f, 0.2f, 0.2f)));
        PocAssetBuilder.StripCollider(gun);

        var movementSo = new SerializedObject(movement);
        movementSo.FindProperty("moveAction").objectReferenceValue = PocAssetBuilder.InputRef("Player/Move", "MoveRef");
        movementSo.FindProperty("lookAction").objectReferenceValue = PocAssetBuilder.InputRef("Player/Look", "LookRef");
        movementSo.FindProperty("viewPivot").objectReferenceValue = pivot.transform;
        movementSo.FindProperty("health").objectReferenceValue = health;
        movementSo.FindProperty("moveSpeed").floatValue = 5f;
        movementSo.FindProperty("gravity").floatValue = 20f;
        movementSo.FindProperty("mouseSensitivity").floatValue = 0.12f;
        movementSo.FindProperty("pitchLimit").floatValue = 80f;
        movementSo.ApplyModifiedPropertiesWithoutUndo();

        var weaponSo = new SerializedObject(weapon);
        weaponSo.FindProperty("fireAction").objectReferenceValue = PocAssetBuilder.InputRef("Player/Attack", "AttackRef");
        weaponSo.FindProperty("reloadAction").objectReferenceValue = PocAssetBuilder.InputRef("Player/Reload", "ReloadRef");
        weaponSo.FindProperty("aimCamera").objectReferenceValue = camera;
        weaponSo.FindProperty("health").objectReferenceValue = health;
        weaponSo.FindProperty("hitLayers").intValue = (1 << 0) | (1 << PocAssetBuilder.ENEMY_LAYER);
        weaponSo.ApplyModifiedPropertiesWithoutUndo();

        var interactorSo = new SerializedObject(interactor);
        interactorSo.FindProperty("interactAction").objectReferenceValue = PocAssetBuilder.InputRef("Player/Interact", "InteractRef");
        interactorSo.FindProperty("viewCamera").objectReferenceValue = camera;
        interactorSo.FindProperty("health").objectReferenceValue = health;
        interactorSo.FindProperty("interactLayers").intValue = 1 << PocAssetBuilder.INTERACT_LAYER;
        interactorSo.ApplyModifiedPropertiesWithoutUndo();

        var waves = new GameObject("Waves");
        var waveController = waves.AddComponent<WaveController>();
        var waveSo = new SerializedObject(waveController);
        waveSo.FindProperty("enemyPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>($"{PocAssetBuilder.GAMEPLAY_PREFAB_DIR}/Enemy.prefab");
        AssignArray(waveSo.FindProperty("spawnPoints"), new[] { spawnLeft.transform, spawnRight.transform });
        waveSo.FindProperty("spawnJitter").floatValue = 1.5f;
        waveSo.ApplyModifiedPropertiesWithoutUndo();

        var gameManagerObject = new GameObject("GameManager");
        var gameManager = gameManagerObject.AddComponent<GameManager>();
        var gmSo = new SerializedObject(gameManager);
        gmSo.FindProperty("waves").objectReferenceValue = waveController;
        gmSo.FindProperty("playerHealth").objectReferenceValue = health;
        gmSo.FindProperty("weapon").objectReferenceValue = weapon;
        gmSo.FindProperty("interactor").objectReferenceValue = interactor;
        gmSo.ApplyModifiedPropertiesWithoutUndo();

        var slotInstance = InstantiatePrefab($"{PocAssetBuilder.GAMEPLAY_PREFAB_DIR}/SlotMachine.prefab");
        slotInstance.transform.position = new Vector3(0f, 0f, -2f);
        var slotPresenter = slotInstance.GetComponent<SlotMachinePresenter>();
        var slotSo = new SerializedObject(slotPresenter);
        slotSo.FindProperty("gameManager").objectReferenceValue = gameManager;
        slotSo.FindProperty("weapon").objectReferenceValue = weapon;
        slotSo.ApplyModifiedPropertiesWithoutUndo();

        var hudInstance = InstantiatePrefab($"{PocAssetBuilder.UI_PREFAB_DIR}/HUD.prefab");
        var shopInstance = InstantiatePrefab($"{PocAssetBuilder.UI_PREFAB_DIR}/Shop.prefab");
        var gameOverInstance = InstantiatePrefab($"{PocAssetBuilder.UI_PREFAB_DIR}/GameOver.prefab");

        var presenters = new GameObject("Presenters");
        var hudPresenter = presenters.AddComponent<HudPresenter>();
        var hudSo = new SerializedObject(hudPresenter);
        hudSo.FindProperty("view").objectReferenceValue = hudInstance.GetComponent<HudView>();
        hudSo.FindProperty("gameManager").objectReferenceValue = gameManager;
        hudSo.FindProperty("playerHealth").objectReferenceValue = health;
        hudSo.FindProperty("weapon").objectReferenceValue = weapon;
        hudSo.FindProperty("interactor").objectReferenceValue = interactor;
        hudSo.FindProperty("slot").objectReferenceValue = slotPresenter;
        hudSo.ApplyModifiedPropertiesWithoutUndo();

        var shopPresenter = presenters.AddComponent<ShopPresenter>();
        var shopSo = new SerializedObject(shopPresenter);
        shopSo.FindProperty("view").objectReferenceValue = shopInstance.GetComponent<ShopView>();
        shopSo.FindProperty("gameManager").objectReferenceValue = gameManager;
        shopSo.ApplyModifiedPropertiesWithoutUndo();

        var gameOverPresenter = presenters.AddComponent<GameOverPresenter>();
        var gameOverSo = new SerializedObject(gameOverPresenter);
        gameOverSo.FindProperty("view").objectReferenceValue = gameOverInstance.GetComponent<GameOverView>();
        gameOverSo.FindProperty("gameManager").objectReferenceValue = gameManager;
        gameOverSo.ApplyModifiedPropertiesWithoutUndo();

        CreateEventSystem();
        CreateSoundPlayer();
        EditorSceneManager.SaveScene(scene, SCENE_GAME);
    }

    private static void CreateWall(Transform _parent, string _name, Vector3 _position, Vector3 _scale, Material _material)
    {
        var wall = PocAssetBuilder.Primitive(PrimitiveType.Cube, _name, _parent, _position, _scale, _material);
        wall.layer = 0;
    }

    private static Camera CreateMainCamera(Color _background, Transform _parent = null)
    {
        var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        go.tag = "MainCamera";
        if (_parent != null)
        {
            go.transform.SetParent(_parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
        }
        var camera = go.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = _background;
        camera.nearClipPlane = 0.05f;
        camera.fieldOfView = 70f;
        return camera;
    }

    private static void CreateEventSystem()
    {
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        go.transform.SetAsLastSibling();
    }

    private static GameObject InstantiatePrefab(string _path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_path);
        if (prefab == null)
        {
            Debug.LogError($"PocSceneBuilder: 프리팹 {_path}을 찾지 못했습니다.");
            return new GameObject("MissingPrefab");
        }
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    private static void CreateSoundPlayer()
    {
        var go = new GameObject("SoundPlayer");
        var oneShot = go.AddComponent<AudioSource>();
        oneShot.playOnAwake = false;
        var loop = go.AddComponent<AudioSource>();
        loop.playOnAwake = false;
        loop.loop = true;

        var player = go.AddComponent<SoundPlayer>();
        var so = new SerializedObject(player);
        so.FindProperty("oneShotSource").objectReferenceValue = oneShot;
        so.FindProperty("loopSource").objectReferenceValue = loop;

        var cues = new List<(SfxType type, string clipPath, float volume)>
        {
            (SfxType.Shot, $"{AUDIO_DIR}/shot.wav", 0.6f),
            (SfxType.EnemyShot, $"{AUDIO_DIR}/enemy_shot.wav", 0.5f),
            (SfxType.EnemyHit, $"{AUDIO_DIR}/hit.wav", 0.5f),
            (SfxType.EnemyDown, $"{AUDIO_DIR}/enemy_down.wav", 0.6f),
            (SfxType.Explosion, $"{AUDIO_DIR}/explosion.wav", 0.8f),
            (SfxType.PlayerHurt, $"{AUDIO_DIR}/player_hurt.wav", 0.7f),
            (SfxType.Reload, $"{AUDIO_DIR}/reload.wav", 0.7f),
            (SfxType.AmmoRefill, $"{AUDIO_DIR}/ammo.wav", 0.7f),
            (SfxType.LeverPull, "Assets/Resources/Sound/SFX/Pachinko/Pachinko-lever.wav", 0.8f),
            (SfxType.ReelSpin, "Assets/Resources/Sound/SFX/Pachinko/Pachinko-spin.mp3", 0.6f),
            (SfxType.ReelStop, $"{AUDIO_DIR}/reel_stop.wav", 0.6f),
            (SfxType.NearMiss, $"{AUDIO_DIR}/near_miss.wav", 0.8f),
            (SfxType.Payout, "Assets/Resources/Sound/SFX/Pachinko/Pachinko-money.mp3", 0.9f),
            (SfxType.Fail, "Assets/Resources/Sound/SFX/Pachinko/Pachinko-fail.mp3", 0.6f),
            (SfxType.WaveStart, $"{AUDIO_DIR}/wave_start.wav", 0.8f),
            (SfxType.WaveClear, $"{AUDIO_DIR}/wave_clear.wav", 0.8f),
            (SfxType.ShopOpen, $"{AUDIO_DIR}/shop_open.wav", 0.6f),
            (SfxType.Click, $"{AUDIO_DIR}/click.wav", 0.5f)
        };

        var cueArray = so.FindProperty("cues");
        cueArray.arraySize = cues.Count;
        for (int i = 0; i < cues.Count; i++)
        {
            var element = cueArray.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("type").enumValueIndex = (int)cues[i].type;
            element.FindPropertyRelative("clip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(cues[i].clipPath);
            element.FindPropertyRelative("volume").floatValue = cues[i].volume;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
