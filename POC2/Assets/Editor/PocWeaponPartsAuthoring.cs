using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// AssetFactory 키트 조립. 모델·파츠 매핑은 WeaponPartVisuals.json에서 읽는다.
public static class PocWeaponPartsAuthoring
{
    [Serializable] private sealed class Table { public Weapon[] weapons; }
    [Serializable] private sealed class Weapon
    {
        public string modelId, weaponId, nickelPart;
        public float verticalCenter;
        public string[] nickelMaterials;
        public Color[] nickelColors;
        public Slot[] slots;
    }
    [Serializable] private sealed class Slot { public string anchor, standard, replacement, partId; }
    private const string KIT = "Assets/Models/AssetFactory/firearms_parts_poc";
    private const string MATERIALS = "Assets/Materials/Weapons/PartFinishes";

    private static Table LoadTable()
    {
        var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/WeaponPartVisuals.json");
        if (json == null) throw new InvalidOperationException("파츠 외형 테이블이 없습니다.");
        return JsonUtility.FromJson<Table>(json.text);
    }

    public static bool Build(string _modelId, Transform _parent)
    {
        var table = LoadTable();
        var entry = Array.Find(table.weapons, w => w.modelId == _modelId);
        if (entry == null) return false;
        var root = new GameObject("PartsKit");
        root.transform.SetParent(_parent, false);
        root.transform.localPosition = new Vector3(0f, -entry.verticalCenter, 0f);
        root.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        var baseObject = InstantiateAsset($"{KIT}/{entry.modelId}/{entry.modelId}_base.fbx", root.transform);
        var visual = root.AddComponent<WeaponPartVisual>();
        visual.weaponId = entry.weaponId;
        visual.nickelPart = entry.nickelPart;
        var slots = new List<WeaponPartVisual.Slot>();
        foreach (var slot in entry.slots)
        {
            var anchor = baseObject.transform.Find(slot.anchor);
            if (anchor == null) throw new InvalidOperationException($"앵커가 없습니다: {entry.modelId}/{slot.anchor}");
            var standard = InstantiateAsset($"{KIT}/{entry.modelId}/parts/{slot.standard}.fbx", anchor);
            var replacement = InstantiateAsset($"{KIT}/{entry.modelId}/parts/{slot.replacement}.fbx", anchor);
            slots.Add(new WeaponPartVisual.Slot { partId = slot.partId, standard = standard, replacement = replacement });
        }
        visual.slots = slots.ToArray();
        var finishes = new List<WeaponPartVisual.Finish>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var standard = renderer.sharedMaterials;
            var nickel = (Material[])standard.Clone();
            bool changed = false;
            for (int i = 0; i < standard.Length; i++)
            {
                int index = standard[i] != null && entry.nickelMaterials != null ? Array.IndexOf(entry.nickelMaterials, standard[i].name) : -1;
                if (index < 0) continue;
                if (entry.nickelColors == null || index >= entry.nickelColors.Length) throw new InvalidOperationException("니켈 재질 색상 설정이 부족합니다.");
                nickel[i] = FinishMaterial(entry.modelId, standard[i], entry.nickelColors[index]);
                changed = true;
            }
            if (changed) finishes.Add(new WeaponPartVisual.Finish { renderer = renderer, standard = standard, nickel = nickel });
        }
        visual.finishes = finishes.ToArray();
        visual.Apply(null);
        return true;
    }

    private static GameObject InstantiateAsset(string _path, Transform _parent)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(_path);
        if (asset == null) throw new InvalidOperationException($"파츠 FBX가 없습니다: {_path}");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        instance.transform.SetParent(_parent, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        return instance;
    }

    private static Material FinishMaterial(string _modelId, Material _source, Color _color)
    {
        if (!AssetDatabase.IsValidFolder(MATERIALS)) AssetDatabase.CreateFolder("Assets/Materials/Weapons", "PartFinishes");
        string path = $"{MATERIALS}/{_modelId}_{_source.name}_nickel.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(_source);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", _color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", _color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // 기존 프리팹의 대상 모델만 바꾸며 다른 프리팹·씬 설정은 그대로 둔다.
    [MenuItem("POC/Apply Weapon Parts Kit")]
    public static void Apply()
    {
        foreach (var entry in LoadTable().weapons)
        {
            ReplaceInPrefab(string.Format(PocWeaponDropAuthoring.PICKUP_PREFAB_FORMAT, entry.modelId), new[] { "Model" }, entry.modelId);
        }
        var held = PrefabUtility.LoadPrefabContents(PocWeaponDropAuthoring.HELD_PREFAB_PATH);
        try
        {
            foreach (var entry in LoadTable().weapons)
            {
                ReplaceModel(held.transform.Find(entry.modelId), entry.modelId);
                var offhand = held.transform.Find(entry.modelId + "_OffHand");
                if (offhand != null) ReplaceModel(offhand, entry.modelId);
            }
            PrefabUtility.SaveAsPrefabAsset(held, PocWeaponDropAuthoring.HELD_PREFAB_PATH);
        }
        finally { PrefabUtility.UnloadPrefabContents(held); }
        AssetDatabase.SaveAssets();
    }

    private static void ReplaceInPrefab(string _path, string[] _pathParts, string _modelId)
    {
        var root = PrefabUtility.LoadPrefabContents(_path);
        try
        {
            var parent = root.transform;
            foreach (var part in _pathParts) parent = parent != null ? parent.Find(part) : null;
            ReplaceModel(parent, _modelId);
            PrefabUtility.SaveAsPrefabAsset(root, _path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ReplaceModel(Transform _parent, string _modelId)
    {
        if (_parent == null) throw new InvalidOperationException($"기존 무기 모델을 찾지 못했습니다: {_modelId}");
        var oldKit = _parent.Find("PartsKit");
        var oldReference = _parent.Find(_modelId + "_reference");
        if (oldKit != null) UnityEngine.Object.DestroyImmediate(oldKit.gameObject);
        if (oldReference != null) UnityEngine.Object.DestroyImmediate(oldReference.gameObject);
        if (!Build(_modelId, _parent)) throw new InvalidOperationException($"무기 외형 테이블에 없습니다: {_modelId}");
    }
}
