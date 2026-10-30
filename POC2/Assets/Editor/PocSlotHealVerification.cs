using UnityEditor;
using UnityEngine;

// 회전 회복 설정·최대 체력·사망 처리를 편집 모드에서 검사한다.
public static class PocSlotHealVerification
{
    [MenuItem("POC/Verify Slot Heal")]
    public static void Run()
    {
        TableManager.ReloadAfterTableEdit();
        int failures = 0;
        var table = TableManager.Instance.SlotMachine;
        float amount = SlotMachineInfo.SpinHealAmount(table);
        Check(amount > 0f, "슬롯 회복 테이블 값", ref failures);
        var root = new GameObject("SlotHealVerification");
        root.SetActive(false);
        var health = root.AddComponent<PlayerHealth>();
        var game = root.AddComponent<GameManager>();
        var so = new SerializedObject(game);
        so.FindProperty("playerHealth").objectReferenceValue = health;
        so.ApplyModifiedPropertiesWithoutUndo();
        try
        {
            health.Configure(100f);
            health.ApplyDamage(30f);
            float actual = game.RecoverForSlotSpin(amount);
            Check(Mathf.Approximately(actual, Mathf.Min(30f, amount)) && Mathf.Approximately(health.CurrentHealth, 70f + actual), "체력 회복", ref failures);
            health.Configure(100f);
            health.ApplyDamage(2f);
            actual = game.RecoverForSlotSpin(amount);
            Check(Mathf.Approximately(actual, Mathf.Min(2f, amount)) && health.CurrentHealth <= health.MaxHealth, "최대 체력 제한", ref failures);
            health.Configure(100f);
            Check(Mathf.Approximately(game.RecoverForSlotSpin(amount), 0f), "최대 체력에서는 회복 없음", ref failures);
            health.ApplyDamage(1000f);
            Check(Mathf.Approximately(game.RecoverForSlotSpin(amount), 0f) && !health.IsAlive, "사망 시 부활하지 않음", ref failures);
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log($"[PocSlotHealVerification] failures={failures}");
    }

    private static void Check(bool _value, string _message, ref int _failures)
    {
        if (_value) return;
        _failures++;
        Debug.LogError("[PocSlotHealVerification] FAIL " + _message);
    }
}
