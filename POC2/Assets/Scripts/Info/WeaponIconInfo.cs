using System.Collections.Generic;
using UnityEngine;

// 총기 이미지(상점 상세용). Resources/WeaponIcons/{modelId}.png를 읽어 스프라이트로 만든다. 파일이 없으면 null이다.
public static class WeaponIconInfo
{
    private const string FOLDER = "WeaponIcons/";

    private static readonly Dictionary<string, Sprite> CACHE = new Dictionary<string, Sprite>();

    public static Sprite Find(string _modelId)
    {
        if (string.IsNullOrEmpty(_modelId))
        {
            return null;
        }
        if (CACHE.TryGetValue(_modelId, out Sprite cached))
        {
            return cached;
        }
        Texture2D texture = Resources.Load<Texture2D>(FOLDER + _modelId);
        Sprite sprite = texture != null
            ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f))
            : null;
        CACHE[_modelId] = sprite;
        return sprite;
    }
}
