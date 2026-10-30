using System;
using UnityEngine;

// 외형 id(modelId) → 바닥 픽업 프리팹. 떨어뜨리기 전에 존재 여부를 확인해, 실패 시 돈과 현재 무기를 건드리지 않게 한다.
public sealed class WeaponPickupCatalog : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public string id;
        public WeaponPickup prefab;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public bool Has(string _id) => Find(_id) != null;

    public WeaponPickup Find(string _id)
    {
        foreach (var entry in entries)
        {
            if (entry.id == _id && entry.prefab != null)
            {
                return entry.prefab;
            }
        }
        return null;
    }

    // 외형 id 프리팹이 없으면 대체 외형을 쓴다.
    public WeaponPickup Find(string _id, string _fallbackId)
    {
        return Find(_id) ?? Find(_fallbackId);
    }
}
