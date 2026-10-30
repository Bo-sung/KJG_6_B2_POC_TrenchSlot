using System;
using UnityEngine;

// 플레이어가 든 무기 외형. 사용 중인 무기의 외형 id(modelId)에 맞는 모델만 켠다.
// 아킴보 왼손 외형은 offHandEntries에 따로 배치한다. 비어 있으면 아킴보 중에도 오른손 모델만 보인다.
public sealed class WeaponHeldModels : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public string id;
        public GameObject model;
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();
    [SerializeField] private Entry[] offHandEntries = Array.Empty<Entry>();

    public bool Has(string _id) => Has(entries, _id);

    public bool HasOffHand(string _id) => Has(offHandEntries, _id);

    // 외형 id 모델이 없으면 대체 외형을 켠다. 왼손 외형은 끈다.
    public void Show(string _id, string _fallbackId = null)
    {
        ShowIn(entries, Has(_id) ? _id : _fallbackId);
        ShowIn(offHandEntries, null);
    }

    // 아킴보: 양손에 같은 외형을 켠다. 왼손 외형이 없는 id면 대체 외형의 왼손 모델을 쓰고, 그것도 없으면 왼손은 비운다.
    public void ShowAkimbo(string _id, string _fallbackId = null)
    {
        ShowIn(entries, Has(_id) ? _id : _fallbackId);
        ShowIn(offHandEntries, HasOffHand(_id) ? _id : _fallbackId);
    }

    private static bool Has(Entry[] _entries, string _id)
    {
        foreach (var entry in _entries)
        {
            if (entry.id == _id && entry.model != null)
            {
                return true;
            }
        }
        return false;
    }

    private static void ShowIn(Entry[] _entries, string _shown)
    {
        foreach (var entry in _entries)
        {
            if (entry.model != null)
            {
                entry.model.SetActive(entry.id == _shown);
            }
        }
    }
}
