using System;
using UnityEditor;
using UnityEngine;

// 조립된 프리팹과 개별 무기 상태의 파츠 표시를 편집 모드에서 검사한다.
public static class PocWeaponPartsVerification
{
    [MenuItem("POC/Verify Weapon Parts Kit")]
    public static void Run()
    {
        TableManager.ReloadAfterTableEdit();
        var inventory = new PartInventory(TableManager.Instance.Part);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PocWeaponDropAuthoring.HELD_PREFAB_PATH);
        var held = UnityEngine.Object.Instantiate(asset);
        int failures = 0;
        try
        {
            CheckWeapon(held, inventory, "colt_saa", "m1873", new[] { "m1873_heavy_barrel", "m1873_fast_cylinder", "m1873_nickel" }, ref failures);
            CheckWeapon(held, inventory, "winchester_1886", "win1886", new[] { "win1886_long_barrel", "win1886_long_tube", "win1886_lever_grip" }, ref failures);
            CheckWeapon(held, inventory, "colt_1878", "colt1878", new[] { "colt1878_short_barrel", "colt1878_ivory_grip", "colt1878_nickel" }, ref failures);
            var main = new WeaponInstance("m1873", 0);
            var off = new WeaponInstance("m1873", 0);
            inventory.Attach(inventory.Add("m1873_heavy_barrel"), main);
            held.GetComponent<WeaponHeldModels>().ApplyParts(main, off);
            var right = held.transform.Find("colt_saa").GetComponentInChildren<WeaponPartVisual>(true);
            var left = held.transform.Find("colt_saa_OffHand").GetComponentInChildren<WeaponPartVisual>(true);
            Check(right.slots[0].replacement.activeSelf && left.slots[0].standard.activeSelf, "아킴보 양손 파츠 독립", ref failures);
        }
        finally { UnityEngine.Object.DestroyImmediate(held); }
        Debug.Log($"[PocWeaponPartsVerification] failures={failures}");
    }

    private static void CheckWeapon(GameObject _held, PartInventory _inventory, string _modelId, string _weaponId, string[] _parts, ref int _failures)
    {
        var instance = new WeaponInstance(_weaponId, 0);
        foreach (var id in _parts) Check(_inventory.Attach(_inventory.Add(id), instance), id + " 장착", ref _failures);
        var visual = _held.transform.Find(_modelId).GetComponentInChildren<WeaponPartVisual>(true);
        visual.Apply(instance);
        foreach (var slot in visual.slots) Check(!slot.standard.activeSelf && slot.replacement.activeSelf, slot.partId + " 외형", ref _failures);
        if (!string.IsNullOrEmpty(visual.nickelPart))
        {
            Check(visual.finishes.Length > 0, _modelId + " 니켈 재질", ref _failures);
            foreach (var finish in visual.finishes)
                Check(Array.Exists(finish.renderer.sharedMaterials, m => Array.Exists(finish.nickel, n => n == m && Array.IndexOf(finish.standard, m) < 0)), _modelId + " 니켈 표시", ref _failures);
        }
        var pickupAsset = AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(PocWeaponDropAuthoring.PICKUP_PREFAB_FORMAT, _modelId));
        var pickup = UnityEngine.Object.Instantiate(pickupAsset);
        try
        {
            pickup.GetComponent<WeaponPickup>().Initialize(instance, "검사");
            foreach (var slot in pickup.GetComponentInChildren<WeaponPartVisual>(true).slots)
                Check(!slot.standard.activeSelf && slot.replacement.activeSelf, slot.partId + " 바닥 외형", ref _failures);
        }
        finally { UnityEngine.Object.DestroyImmediate(pickup); }
        visual.Apply(null);
        foreach (var slot in visual.slots) Check(slot.standard.activeSelf && !slot.replacement.activeSelf, slot.partId + " 기본 복원", ref _failures);
    }

    private static void Check(bool _condition, string _message, ref int _failures)
    {
        if (_condition) return;
        _failures++;
        Debug.LogError("[PocWeaponPartsVerification] FAIL " + _message);
    }
}
