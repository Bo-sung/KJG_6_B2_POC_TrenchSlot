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
            Check(weapon.IsAiming && Mathf.Approximately(camera.fieldOfView, expected), "단일 무기 조준", ref failures);
            updateAim.Invoke(weapon, new object[] { false, 1f });
            Check(!weapon.IsAiming && Mathf.Approximately(camera.fieldOfView, 60f), "우클릭 해제", ref failures);
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
        }
        finally
        {
            InputGate.PlayerEnabled = oldGate;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(reference);
        }
        Debug.Log($"[PocAimVerification] failures={failures}");
    }

    private static void Check(bool _value, string _message, ref int _failures)
    {
        if (_value) return;
        _failures++;
        Debug.LogError("[PocAimVerification] FAIL " + _message);
    }
}
