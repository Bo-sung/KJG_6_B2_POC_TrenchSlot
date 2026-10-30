using System.Reflection;
using UnityEditor;
using UnityEngine;
using TMPro;

// 조준 표시·산포·시점 반동을 편집 모드에서 검사한다.
public static class PocShootingVerification
{
    [MenuItem("POC/Verify Shooting Feedback")]
    public static void Run()
    {
        TableManager.ReloadAfterTableEdit();
        int failures = 0;
        foreach (var entry in TableManager.Instance.Weapon.weapons)
        {
            var settings = WeaponInfo.Resolve(entry, null, 1f);
            Check(settings.hipSpreadDegrees > 0f && settings.aimSpreadMultiplier > 0f && settings.aimSpreadMultiplier < 1f &&
                settings.recoilPitchDegrees > 0f && settings.recoilRecoveryDegreesPerSecond > 0f,
                entry.id + " 무기별 산포·반동", ref failures);
        }

        var root = new GameObject("ShootingVerification");
        root.SetActive(false);
        var pivot = new GameObject("ViewPivot").transform;
        pivot.SetParent(root.transform, false);
        var movement = root.AddComponent<PlayerMovement>();
        var so = new SerializedObject(movement);
        so.FindProperty("viewPivot").objectReferenceValue = pivot;
        so.FindProperty("pitchLimit").floatValue = 85f;
        so.ApplyModifiedPropertiesWithoutUndo();
        bool oldGate = InputGate.PlayerEnabled;
        var oldRandom = Random.state;
        try
        {
            InputGate.PlayerEnabled = true;
            movement.AddRecoil(2f, 0.5f, 4f);
            Check(pivot.forward.y > 0f && pivot.forward.x > 0f, "반동이 시점을 위·옆으로 이동", ref failures);
            movement.AddRecoil(2f, 0.5f, 4f);
            Check(Vector3.Angle(Vector3.forward, pivot.forward) > 3.9f, "연속 발사 반동 누적", ref failures);
            movement.TickRecoil(2f);
            Check(Vector3.Angle(Vector3.forward, pivot.forward) < 0.01f, "반동 복귀", ref failures);
            InputGate.PlayerEnabled = false;
            movement.AddRecoil(2f, 1f, 4f);
            Check(Vector3.Angle(Vector3.forward, pivot.forward) < 0.01f, "입력 차단 중 반동 금지", ref failures);

            var spread = typeof(PlayerWeapon).GetMethod("SpreadDirection", BindingFlags.Static | BindingFlags.NonPublic);
            Random.InitState(1234);
            bool displaced = false;
            bool bounded = true;
            for (int i = 0; i < 128; i++)
            {
                var direction = (Vector3)spread.Invoke(null, new object[] { pivot, 2f });
                float angle = Vector3.Angle(pivot.forward, direction);
                displaced |= angle > 0.01f;
                bounded &= angle <= 2.01f;
            }
            Check(displaced && bounded, "단발 발사 방향은 산포 각도 내에서 달라진다", ref failures);
            var zero = (Vector3)spread.Invoke(null, new object[] { pivot, 0f });
            Check(Vector3.Angle(pivot.forward, zero) < 0.01f, "0도 산포는 조준선 유지", ref failures);
        }
        finally
        {
            InputGate.PlayerEnabled = oldGate;
            Random.state = oldRandom;
            Object.DestroyImmediate(root);
        }

        var hudAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/HUD.prefab");
        Check(hudAsset != null, "HUD 프리팹", ref failures);
        if (hudAsset != null)
        {
            var hud = Object.Instantiate(hudAsset);
            try
            {
                var view = hud.GetComponent<HudView>();
                var cross = hud.transform.Find("Crosshair").GetComponent<TMP_Text>();
                typeof(HudView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                view.SetCrosshairVisible(false);
                Check(!cross.enabled, "조준 중 크로스헤어 숨김", ref failures);
                view.SetCrosshairVisible(true);
                Check(cross.enabled, "조준 해제 시 크로스헤어 복원", ref failures);
            }
            finally { Object.DestroyImmediate(hud); }
        }
        Debug.Log($"[PocShootingVerification] failures={failures}");
    }

    private static void Check(bool _value, string _message, ref int _failures)
    {
        if (_value) return;
        _failures++;
        Debug.LogError("[PocShootingVerification] FAIL " + _message);
    }
}
