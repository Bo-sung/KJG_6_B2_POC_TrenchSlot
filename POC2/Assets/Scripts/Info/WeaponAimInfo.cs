using UnityEngine;

public static class WeaponAimInfo
{
    public static WeaponAimData.Profile Find(WeaponAimData _table, string _modelId)
    {
        if (_table?.profiles == null) return null;
        foreach (var profile in _table.profiles)
            if (profile != null && profile.modelId == _modelId) return profile;
        return null;
    }

    public static Vector3 Front(WeaponAimData.Profile _profile, WeaponInstance _weapon)
    {
        if (_profile?.variants != null && _weapon != null)
            foreach (var variant in _profile.variants)
                if (variant != null && _weapon.HasPart(variant.partId)) return variant.front;
        return _profile != null ? _profile.front : Vector3.zero;
    }
}
