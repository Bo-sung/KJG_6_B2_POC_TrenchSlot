using System;
using UnityEngine;

// 플레이어가 든 무기 외형. 장착 무기 id에 맞는 모델만 켜서, 보이는 총과 발사 스탯이 같은 무기를 가리키게 한다.
public sealed class WeaponHeldModels : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public string id;
        public GameObject model;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();

    public void Show(string _id)
    {
        foreach (var entry in entries)
        {
            if (entry.model != null)
            {
                entry.model.SetActive(entry.id == _id);
            }
        }
    }
}
