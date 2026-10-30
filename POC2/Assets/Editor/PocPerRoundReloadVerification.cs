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
                bool perRound = id != "p08" && id != "smle" && id != "colt1878";
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

            // 쌍열은 완료 시에만 예비탄을 소비한다. 부족하면 남은 한 발만 넣고, 중단 시 아무 탄도 추가하지 않는다.
            var coltEntry = WeaponInfo.Find(tables.Weapon, "colt1878");
            var coltSettings = WeaponInfo.Resolve(coltEntry, null, 1f);
            inventory.Consume(coltEntry.ammoId, inventory.GetReserve(coltEntry.ammoId));
            inventory.Add(coltEntry.ammoId, 1);
            var colt = new WeaponInstance("colt1878", 0);
            weapon.Configure(colt, coltSettings, inventory, coltEntry.ammoId);
            weapon.StartReload();
            bool beforeCompletion = weapon.IsReloading && colt.Magazine == 0 && inventory.GetReserve(coltEntry.ammoId) == 1;
            weapon.CancelReload();
            bool cancelled = colt.Magazine == 0 && inventory.GetReserve(coltEntry.ammoId) == 1;
            weapon.StartReload();
            complete.Invoke(weapon, null);
            if (!beforeCompletion || !cancelled || colt.Magazine != 1 || inventory.GetReserve(coltEntry.ammoId) != 0 || weapon.IsReloading)
            {
                failures++;
                Debug.LogError("[PocPerRoundReloadVerification] FAIL colt1878 부족 탄약 또는 중단");
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
