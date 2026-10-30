using System;
using UnityEngine;

// 조준 기준점은 손 모델 로컬 좌표다. 교체 총열은 앞가늠쇠 좌표를 따로 지정한다.
[Serializable] public sealed class WeaponAimData
{
    [Serializable] public sealed class Variant { public string partId; public Vector3 front; }
    [Serializable] public sealed class Profile
    {
        public string modelId;
        public Vector3 rear;
        public Vector3 front;
        public float eyeDistance;
        public Variant[] variants = Array.Empty<Variant>();
    }
    public Profile[] profiles = Array.Empty<Profile>();
}
