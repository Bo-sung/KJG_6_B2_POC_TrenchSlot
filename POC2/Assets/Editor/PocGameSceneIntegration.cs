using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 저장된 Game.unity에 이미 구현된 기능 연결(웅크리기·피격 방향, 바닥 무기, 전방 스폰, 원본 슬롯 효과음)을 적용하고 검증한다.
// 씬을 새로 만들지 않고 각 선택적 저작 메서드를 순서대로 호출한 뒤 슬롯 효과음 큐만 덮어쓴다.
// 실행: Unity -batchmode -projectPath <프로젝트> -executeMethod PocGameSceneIntegration.WireAndVerify
//       Unity -batchmode -projectPath <프로젝트> -executeMethod PocGameSceneIntegration.RenderSlotShot
public static class PocGameSceneIntegration
{
    private const string TAG = "[PocGameSceneIntegration]";
    private const string GAME_SCENE = "Assets/Scenes/Game.unity";
    private const string PACHINKO = "Assets/Resources/Sound/SFX/Pachinko/";
    private const int EXPECTED_SPAWNS = 6;
    private const float SLOT_MODEL_SCALE = 2.5f;

    // 원본 SlotMachineSound 매핑: 레버=lever, 회전=spin1, 릴 정지=slot, 지급=money, 꽝=fail. 니어미스는 원본이 꽝음으로 처리한다.
    private static readonly (SfxType type, string file, float volume)[] SLOT_CUES =
    {
        (SfxType.LeverPull, "Pachinko-lever.wav", 0.8f),
        (SfxType.ReelSpin, "Pachinko-spin1.wav", 0.6f),
        (SfxType.ReelStop, "Pachinko-slot.mp3", 0.6f),
        (SfxType.Payout, "Pachinko-money.mp3", 0.9f),
        (SfxType.Fail, "Pachinko-fail.mp3", 0.6f),
        (SfxType.NearMiss, "Pachinko-fail.mp3", 0.8f),
    };

    private static int failures;

    public static void WireAndVerify()
    {
        failures = 0;
        PocPlayerFeedbackAuthoring.WireSavedGame();
        PocWeaponDropAuthoring.WireSavedGame();
        PocEnemyVisibilityAuthoring.AuthorAheadSpawnPoints();
        WireSlotCues();
        FitSlotVisual();
        Verify();
        Debug.Log($"{TAG} WireAndVerify finished: failures={failures}");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    public static void ReimportScripts()
    {
        AssetDatabase.ImportAsset("Assets/Scripts", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Resources/Sound/SFX/Pachinko", ImportAssetOptions.ImportRecursive);
        EditorApplication.Exit(0);
    }

    public static void VerifyOnly()
    {
        failures = 0;
        Verify();
        Debug.Log($"{TAG} VerifyOnly finished: failures={failures}");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void WireSlotCues()
    {
        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var player = Object.FindFirstObjectByType<SoundPlayer>();
        var so = new SerializedObject(player);
        var cues = so.FindProperty("cues");
        foreach (var cue in SLOT_CUES)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PACHINKO + cue.file);
            if (clip == null)
            {
                Debug.LogError($"{TAG} missing clip {cue.file}");
                failures++;
                continue;
            }
            SerializedProperty element = null;
            for (int i = 0; i < cues.arraySize; i++)
            {
                var candidate = cues.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("type").intValue == (int)cue.type)
                {
                    element = candidate;
                    break;
                }
            }
            if (element == null)
            {
                cues.InsertArrayElementAtIndex(cues.arraySize);
                element = cues.GetArrayElementAtIndex(cues.arraySize - 1);
                element.FindPropertyRelative("type").intValue = (int)cue.type;
            }
            element.FindPropertyRelative("clip").objectReferenceValue = clip;
            element.FindPropertyRelative("volume").floatValue = cue.volume;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"{TAG} slot SoundPlayer cues wired to original Pachinko clips");
    }

    // 원본 슬롯은 책상 위 소품(약 0.78m)이라 바닥 배치용 BoxCollider(2.4m)보다 작다.
    // 프리팹은 그대로 두고 Game 씬 인스턴스의 Model 스케일만 오버라이드해 콜라이더 높이에 맞춘다(NavMesh 영향 없음).
    private static void FitSlotVisual()
    {
        var scene = EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var presenter = Object.FindFirstObjectByType<SlotMachinePresenter>();
        var model = presenter.transform.Find("Model");
        if (model == null)
        {
            return;
        }
        model.localScale = Vector3.one * SLOT_MODEL_SCALE;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model);
        foreach (var collider in model.GetComponentsInChildren<Collider>(true))
        {
            Debug.Log($"{TAG} slot model collider {collider.name} enabled={collider.enabled}");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"{TAG} slot Model scale override={SLOT_MODEL_SCALE}");
    }

    private static void Verify()
    {
        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);

        // 모든 직렬화 참조가 실제 대상에 연결돼 있는지(누락 참조 없음) 확인한다.
        int missing = 0;
        foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null)
            {
                missing++;
                continue;
            }
            var iterator = new SerializedObject(behaviour).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference
                    && iterator.objectReferenceValue == null && iterator.objectReferenceInstanceIDValue != 0)
                {
                    Debug.LogError($"{TAG} broken ref {behaviour.name}.{behaviour.GetType().Name}.{iterator.propertyPath}");
                    missing++;
                }
            }
        }
        Check(missing == 0, $"no broken serialized refs (broken={missing})");
        foreach (string dependency in AssetDatabase.GetDependencies(GAME_SCENE, true))
        {
            if (AssetDatabase.LoadMainAssetAtPath(dependency) == null)
            {
                Check(false, "dependency loads: " + dependency);
            }
        }

        var wave = Object.FindFirstObjectByType<WaveController>();
        var spawns = new SerializedObject(wave).FindProperty("spawnPoints");
        int valid = 0;
        for (int i = 0; i < spawns.arraySize; i++)
        {
            valid += spawns.GetArrayElementAtIndex(i).objectReferenceValue != null ? 1 : 0;
        }
        Check(spawns.arraySize == EXPECTED_SPAWNS && valid == EXPECTED_SPAWNS, $"WaveController spawnPoints={spawns.arraySize} valid={valid}");

        var movement = Object.FindFirstObjectByType<PlayerMovement>();
        CheckRefs(movement, "crouchAction", "moveAction", "lookAction", "viewPivot");
        var hud = Object.FindFirstObjectByType<HudPresenter>();
        CheckRefs(hud, "view", "gameManager", "playerHealth", "weapon", "interactor", "slot", "damageView");
        var hudView = Object.FindFirstObjectByType<HudView>();
        CheckRefs(hudView, "damageDirectionGroup", "damageDirectionPivot", "damageFlash", "promptText");

        var weapon = Object.FindFirstObjectByType<PlayerWeapon>();
        CheckRefs(weapon, "interactor", "pickupAction", "fireAction", "reloadAction", "aimCamera");
        CheckRefs(Object.FindFirstObjectByType<PlayerInteractor>(), "pickupAction", "interactAction", "viewCamera");
        var gm = Object.FindFirstObjectByType<GameManager>();
        CheckRefs(gm, "pickups", "heldModels", "dropRoot", "weapon", "interactor");
        var catalog = Object.FindFirstObjectByType<WeaponPickupCatalog>();
        Check(catalog != null && HasNonEmptyRefArray(catalog), "WeaponPickupCatalog has entries");
        var held = Object.FindFirstObjectByType<WeaponHeldModels>();
        Check(held != null && held.GetComponentsInChildren<Renderer>(true).Length > 0, "WeaponHeldModels has model renderers");

        var presenter = Object.FindFirstObjectByType<SlotMachinePresenter>();
        CheckRefs(presenter, "reels", "gameManager", "weapon");
        var reels = presenter.GetComponent<SlotMachineReels>();
        var reelsSo = new SerializedObject(reels);
        var axes = reelsSo.FindProperty("reels");
        int axesValid = 0;
        for (int i = 0; i < axes.arraySize; i++)
        {
            axesValid += axes.GetArrayElementAtIndex(i).objectReferenceValue != null ? 1 : 0;
        }
        Check(axesValid == 3, $"slot reel axes={axesValid}");
        CheckRefs(reels, "lever", "marquee", "payoutParticles");
        var model = presenter.transform.Find("Model");
        Check(model != null, "slot Model child exists");
        if (model != null)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(false);
            int visible = 0;
            Bounds bounds = default;
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer.sharedMaterial == null)
                {
                    continue;
                }
                bounds = visible == 0 ? renderer.bounds : Encapsulate(bounds, renderer.bounds);
                visible++;
            }
            Check(visible > 0, $"slot visible renderers={visible}");
            var box = presenter.GetComponent<BoxCollider>();
            Check(box != null && box.enabled, "slot BoxCollider enabled");
            if (box != null && visible > 0)
            {
                Debug.Log($"{TAG} slot visual bounds center={bounds.center} size={bounds.size}; collider center={box.bounds.center} size={box.bounds.size}");
                Check(bounds.Contains(box.bounds.center) && bounds.size.y > box.bounds.size.y * 0.6f && bounds.min.y > -0.05f,
                    "slot visual fills collider and stands on floor");
            }
        }

        var sound = Object.FindFirstObjectByType<SoundPlayer>();
        var cues = new SerializedObject(sound).FindProperty("cues");
        foreach (var cue in SLOT_CUES)
        {
            string found = null;
            for (int i = 0; i < cues.arraySize; i++)
            {
                var element = cues.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("type").intValue == (int)cue.type)
                {
                    found = AssetDatabase.GetAssetPath(element.FindPropertyRelative("clip").objectReferenceValue);
                }
            }
            Check(found == PACHINKO + cue.file, $"SoundPlayer {cue.type} -> {found}");
        }
    }

    // 편집 모드에서 슬롯을 향한 임시 카메라로 렌더해 PNG로 저장한다. 씬은 저장하지 않는다.
    public static void RenderSlotShot()
    {
        failures = 0;
        EditorSceneManager.OpenScene(GAME_SCENE, OpenSceneMode.Single);
        var presenter = Object.FindFirstObjectByType<SlotMachinePresenter>();
        string folder = Path.Combine(Path.GetTempPath(), "opencode", "poc2-scene-final-shots");
        Directory.CreateDirectory(folder);

        var model = presenter.transform.Find("Model");
        Bounds bounds = new Bounds(presenter.transform.position + Vector3.up, Vector3.one * 2f);
        if (model != null)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                bounds = i == 0 ? renderers[i].bounds : Encapsulate(bounds, renderers[i].bounds);
            }
        }
        var go = new GameObject("PocShotCamera");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 50f;
        float distance = bounds.extents.magnitude * 2.6f + 1f;
        Vector3 forward = presenter.transform.forward;
        Capture(cam, bounds.center + forward * distance + Vector3.up * bounds.extents.y * 0.4f, bounds.center, Path.Combine(folder, "slot_front.png"));
        Capture(cam, bounds.center - forward * distance + Vector3.up * bounds.extents.y * 0.4f, bounds.center, Path.Combine(folder, "slot_back.png"));

        var mainCam = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var existing in mainCam)
        {
            if (existing != cam && existing.CompareTag("MainCamera"))
            {
                CaptureFrom(existing, Path.Combine(folder, "player_view.png"));
                break;
            }
        }
        Object.DestroyImmediate(go);
        Debug.Log($"{TAG} RenderSlotShot finished: failures={failures} folder={folder}");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void Capture(Camera _cam, Vector3 _position, Vector3 _target, string _path)
    {
        _cam.transform.position = _position;
        _cam.transform.LookAt(_target);
        CaptureFrom(_cam, _path);
    }

    private static void CaptureFrom(Camera _cam, string _path)
    {
        const int WIDTH = 960;
        const int HEIGHT = 540;
        var rt = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
        var previous = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
        texture.Apply();
        _cam.targetTexture = previous;
        RenderTexture.active = null;

        // 단색(빈 화면) 여부를 픽셀 분산으로 판정한다.
        var pixels = texture.GetPixels32();
        var distinct = new HashSet<int>();
        for (int i = 0; i < pixels.Length; i += 37)
        {
            distinct.Add((pixels[i].r >> 3) << 10 | (pixels[i].g >> 3) << 5 | (pixels[i].b >> 3));
        }
        File.WriteAllBytes(_path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        rt.Release();
        Object.DestroyImmediate(rt);
        Check(distinct.Count > 20, $"render not blank {Path.GetFileName(_path)} distinctColors={distinct.Count}");
    }

    private static Bounds Encapsulate(Bounds _a, Bounds _b)
    {
        _a.Encapsulate(_b);
        return _a;
    }

    private static bool HasNonEmptyRefArray(Object _target)
    {
        var iterator = new SerializedObject(_target).GetIterator();
        while (iterator.NextVisible(true))
        {
            if (iterator.isArray && iterator.propertyType == SerializedPropertyType.Generic && iterator.arraySize > 0)
            {
                return true;
            }
        }
        return false;
    }

    private static void CheckRefs(Object _target, params string[] _fields)
    {
        if (_target == null)
        {
            Check(false, "component present for " + string.Join(",", _fields));
            return;
        }
        var so = new SerializedObject(_target);
        foreach (string field in _fields)
        {
            var property = so.FindProperty(field);
            bool wired = property != null && (property.isArray ? property.arraySize > 0 : property.objectReferenceValue != null);
            Check(wired, $"{_target.GetType().Name}.{field} wired");
        }
    }

    private static void Check(bool _ok, string _label)
    {
        if (_ok)
        {
            Debug.Log($"{TAG} PASS {_label}");
        }
        else
        {
            failures++;
            Debug.LogError($"{TAG} FAIL {_label}");
        }
    }
}
