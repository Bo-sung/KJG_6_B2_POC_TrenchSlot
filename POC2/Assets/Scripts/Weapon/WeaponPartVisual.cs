using System;
using UnityEngine;

// 프리팹에 조립된 파츠를 개별 무기 상태에 맞춰 표시한다.
public sealed class WeaponPartVisual : MonoBehaviour
{
    [Serializable] public sealed class Slot
    {
        public string partId;
        public GameObject standard;
        public GameObject replacement;
    }
    [Serializable] public sealed class Finish
    {
        public Renderer renderer;
        public Material[] standard;
        public Material[] nickel;
    }
    public string weaponId;
    public string nickelPart;
    public Slot[] slots = Array.Empty<Slot>();
    public Finish[] finishes = Array.Empty<Finish>();

    public void Apply(WeaponInstance _weapon)
    {
        bool matches = _weapon != null && _weapon.WeaponId == weaponId;
        foreach (var slot in slots)
        {
            bool equipped = matches && _weapon.HasPart(slot.partId);
            slot.standard.SetActive(!equipped);
            slot.replacement.SetActive(equipped);
        }
        bool nickel = matches && _weapon.HasPart(nickelPart);
        foreach (var finish in finishes)
        {
            if (finish.renderer != null) finish.renderer.sharedMaterials = nickel ? finish.nickel : finish.standard;
        }
    }
}
