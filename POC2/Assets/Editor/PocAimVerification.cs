using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// 우클릭 바인딩, 시야 전환, 아킴보 및 입력 차단을 편집 모드에서 검사한다.
public static class PocAimVerification
{
    [MenuItem("POC/Verify Aim")]
    public static void Run()
    {
        TableManager.ReloadAfterTableEdit();
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var aim = actions.FindAction("Player/Aim");
        var attack = actions.FindAction("Player/Attack");
        int failures = 0;
        Check(aim != null && aim.bindings.Count == 1 && aim.bindings[0].path == "<Mouse>/rightButton", "우클릭 바인딩", ref failures);
        var root = new GameObject("AimVerification");
        root.SetActive(false);
        var camera = root.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        var weapon = root.AddComponent<PlayerWeapon>();
        var reference = InputActionReference.Create(attack);
        var so = new SerializedObject(weapon);
        so.FindProperty("aimCamera").objectReferenceValue = camera;
        so.FindProperty("fireAction").objectReferenceValue = reference;
        so.ApplyModifiedPropertiesWithoutUndo();
        var awake = typeof(PlayerWeapon).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        var updateAim = typeof(PlayerWeapon).GetMethod("UpdateAim", BindingFlags.Instance | BindingFlags.NonPublic);
        bool oldGate = InputGate.PlayerEnabled;
        bool? aimEvent = null;
        weapon.AimChanged += value => aimEvent = value;
        try
        {
            InputGate.PlayerEnabled = true;
            awake.Invoke(weapon, null);
            var resolvedAim = (InputAction)typeof(PlayerWeapon).GetField("aimInput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(weapon);
            Check(resolvedAim == aim, "사격 액션 맵에서 조준 액션 연결", ref failures);
            var main = new WeaponInstance("m1873", 6);
            var off = new WeaponInstance("m1873", 6);
            var settings = new PlayerWeapon.WeaponSettings { magazineSize = 6 };
            weapon.Configure(main, settings, null, "colt45");
            updateAim.Invoke(weapon, new object[] { true, 1f });
            float expected = 60f * GameConfigInfo.AimFovRatio(TableManager.Instance.GameConfig);
            Check(weapon.IsAiming && aimEvent == true && Mathf.Approximately(camera.fieldOfView, expected), "단일 무기 조준과 UI 이벤트", ref failures);
            updateAim.Invoke(weapon, new object[] { false, 1f });
            Check(!weapon.IsAiming && aimEvent == false && Mathf.Approximately(camera.fieldOfView, 60f), "우클릭 해제와 UI 이벤트", ref failures);
            updateAim.Invoke(weapon, new object[] { true, 1f });
            weapon.ConfigureAkimbo(main, settings, off, settings, null, "colt45", 1f);
            Check(!weapon.IsAiming && Mathf.Approximately(camera.fieldOfView, 60f), "아킴보 전환 시 해제", ref failures);
            updateAim.Invoke(weapon, new object[] { true, 1f });
            Check(!weapon.IsAiming && Mathf.Approximately(camera.fieldOfView, 60f), "아킴보 조준 금지", ref failures);
            weapon.Configure(main, settings, null, "colt45");
            updateAim.Invoke(weapon, new object[] { true, 1f });
            InputGate.PlayerEnabled = false;
            updateAim.Invoke(weapon, new object[] { true, 1f });
            Check(!weapon.IsAiming && Mathf.Approximately(camera.fieldOfView, 60f), "입력 차단 시 해제", ref failures);
            InputGate.PlayerEnabled = true;
            CheckSightAlignment(camera, ref failures);
        }
        finally
        {
            InputGate.PlayerEnabled = oldGate;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(reference);
        }
        Debug.Log($"[PocAimVerification] failures={failures}");
    }

    private static void CheckSightAlignment(Camera _camera, ref int _failures)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PocWeaponDropAuthoring.HELD_PREFAB_PATH);
        var instance = Object.Instantiate(asset);
        instance.transform.SetParent(_camera.transform, false);
        instance.transform.localPosition = new Vector3(0.25f, -0.22f, 0.6f);
        var held = instance.GetComponent<WeaponHeldModels>();
        var inventory = new PartInventory(TableManager.Instance.Part);
        try
        {
            foreach (var pair in new[] { ("colt_saa", "m1873"), ("colt_1878_shotgun", "colt1878"), ("winchester_1886", "win1886"), ("revolver", "p08"), ("bolt", "smle"), ("smg", "mp18"), ("shotgun", "m1897") })
            {
                var weapon = new WeaponInstance(pair.Item2, 0);
                var profile = WeaponAimInfo.Find(TableManager.Instance.WeaponAim, pair.Item1);
                Check(profile != null, pair.Item1 + " 조준 프로필", ref _failures);
                if (profile == null) continue;
                held.Show(pair.Item1);
                held.ApplyParts(weapon, null);
                CheckAlignment(held, _camera, profile, weapon, pair.Item1, ref _failures);
                if (profile.variants != null) foreach (var variant in profile.variants)
                {
                    Check(inventory.Attach(inventory.Add(variant.partId), weapon), variant.partId + " 장착", ref _failures);
                    held.ApplyParts(weapon, null);
                    CheckAlignment(held, _camera, profile, weapon, variant.partId, ref _failures);
                }
                held.SetAim(_camera, weapon, 0f);
                Check(instance.transform.Find(pair.Item1).localPosition.sqrMagnitude < 0.000001f, pair.Item1 + " 기본 자세 복원", ref _failures);
            }
        }
        finally { Object.DestroyImmediate(instance); }
    }

    private static void CheckAlignment(WeaponHeldModels _held, Camera _camera, WeaponAimData.Profile _profile, WeaponInstance _weapon, string _label, ref int _failures)
    {
        _held.SetAim(_camera, _weapon, 1f);
        var model = _held.transform.Find(_profile.modelId);
        WeaponHeldModels.GetSightPoints(model, _profile, _weapon, out Vector3 rearPoint, out Vector3 frontPoint);
        var rear = _camera.transform.InverseTransformPoint(model.TransformPoint(rearPoint));
        var front = _camera.transform.InverseTransformPoint(model.TransformPoint(frontPoint));
        Check(Mathf.Abs(rear.x) < 0.0001f && Mathf.Abs(rear.y) < 0.0001f &&
            Mathf.Abs(front.x) < 0.0001f && Mathf.Abs(front.y) < 0.0001f && front.z > rear.z && rear.z > 0f,
            _label + " 앞·뒤 조준점 정렬", ref _failures);
    }

    private static void Check(bool _value, string _message, ref int _failures)
    {
        if (_value) return;
        _failures++;
        Debug.LogError("[PocAimVerification] FAIL " + _message);
    }
}
