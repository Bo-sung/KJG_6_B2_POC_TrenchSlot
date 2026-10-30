using System.Reflection;
using UnityEditor;
using UnityEngine;

// 탄별 재장전과 중단 시 장전탄 보존을 편집 모드에서 확인한다.
public static class PocPerRoundReloadVerification
{
    [MenuItem("POC/Verify Per-Round Reload")]
    public static void Run()
    {
        TableManager.ReloadAfterTableEdit();
        var tables = TableManager.Instance;
        var inventory = new AmmoInventory(tables.Ammo);
        var root = new GameObject("PerRoundReloadVerification");
        var weapon = root.AddComponent<PlayerWeapon>();
        var complete = typeof(PlayerWeapon).GetMethod("CompleteReload", BindingFlags.Instance | BindingFlags.NonPublic);
        bool oldGate = InputGate.PlayerEnabled;
        int failures = 0;
        try
        {
            InputGate.PlayerEnabled = true;
            foreach (var id in new[] { "m1873", "colt1878", "win1886", "m1897", "m12", "p08", "smle" })
            {
                var entry = WeaponInfo.Find(tables.Weapon, id);
                var settings = WeaponInfo.Resolve(entry, null, 1f);
                var instance = new WeaponInstance(id, 0);
                inventory.Add(entry.ammoId, 50);
                weapon.Configure(instance, settings, inventory, entry.ammoId);
                int before = inventory.GetReserve(entry.ammoId);
                weapon.StartReload();
                complete.Invoke(weapon, null);
                bool perRound = id != "p08" && id != "smle";
                int expected = perRound ? 1 : settings.magazineSize;
                bool valid = settings.reloadPerRound == perRound && instance.Magazine == expected &&
                    before - inventory.GetReserve(entry.ammoId) == expected && weapon.IsReloading == perRound;
                weapon.CancelReload();
                valid &= instance.Magazine == expected;
                if (!valid)
                {
                    failures++;
                    Debug.LogError($"[PocPerRoundReloadVerification] FAIL {id}");
                }
            }
        }
        finally
        {
            InputGate.PlayerEnabled = oldGate;
            Object.DestroyImmediate(root);
        }
        Debug.Log($"[PocPerRoundReloadVerification] failures={failures}");
    }
}
