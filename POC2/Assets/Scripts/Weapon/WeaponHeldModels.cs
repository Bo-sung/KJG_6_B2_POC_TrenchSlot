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
    private string shownId;
    private string shownOffHandId;
    private Vector3[] initialPositions;
    private Quaternion[] initialRotations;

    // 모든 외형에 사격 동작 재생기를 붙이고, 외형 id에 맞는 부품 동작을 넣는다.
    private void Awake()
    {
        AttachFireMotion(entries);
        AttachFireMotion(offHandEntries);
    }

    private static void AttachFireMotion(Entry[] _entries)
    {
        foreach (var entry in _entries)
        {
            if (entry.model == null) continue;
            var motion = entry.model.GetComponent<WeaponFireMotion>();
            if (motion == null) motion = entry.model.AddComponent<WeaponFireMotion>();
            motion.Setup(WeaponFireMotionTable.Find(entry.id));
        }
    }

    // hand 0은 주 외형, hand 1은 아킴보 왼손 외형을 쏜다. 보이는 외형에만 재생한다.
    public void PlayFire(int _hand, float _shotIntervalSeconds)
    {
        var list = _hand == 0 ? entries : offHandEntries;
        string id = _hand == 0 ? shownId : shownOffHandId;
        foreach (var entry in list)
        {
            if (entry.id != id || entry.model == null || !entry.model.activeInHierarchy) continue;
            var motion = entry.model.GetComponent<WeaponFireMotion>();
            if (motion != null) motion.PlayFire(_shotIntervalSeconds);
        }
    }

    // 게임 시작 위치를 보존하고, 해당 모델의 앞·뒤 조준점을 카메라 시선에 맞춘다.
    public void SetAim(Camera _camera, WeaponInstance _weapon, float _amount)
    {
        if (initialPositions == null)
        {
            initialPositions = new Vector3[entries.Length];
            initialRotations = new Quaternion[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].model == null) continue;
                initialPositions[i] = entries[i].model.transform.localPosition;
                initialRotations[i] = entries[i].model.transform.localRotation;
            }
        }
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.model == null) continue;
            var model = entry.model.transform;
            var position = initialPositions[i];
            var rotation = initialRotations[i];
            var profile = entry.id == shownId ? WeaponAimInfo.Find(TableManager.Instance.WeaponAim, entry.id) : null;
            if (_amount > 0f && _camera != null && profile != null && profile.eyeDistance > 0f)
            {
                GetSightPoints(model, profile, _weapon, out Vector3 rearPoint, out Vector3 front);
                Vector3 line = rotation * (front - rearPoint);
                if (line.sqrMagnitude > 0.000001f)
                {
                    Vector3 forward = transform.InverseTransformDirection(_camera.transform.forward).normalized;
                    Quaternion aimedRotation = Quaternion.FromToRotation(line, forward) * rotation;
                    Vector3 rear = transform.InverseTransformPoint(_camera.transform.TransformPoint(0f, 0f, profile.eyeDistance));
                    Vector3 aimedPosition = rear - aimedRotation * rearPoint;
                    position = Vector3.Lerp(position, aimedPosition, _amount);
                    rotation = Quaternion.Slerp(rotation, aimedRotation, _amount);
                }
            }
            model.localPosition = position;
            model.localRotation = rotation;
        }
    }

    public bool TryGetAimFront(WeaponInstance _weapon, out Vector3 _point)
    {
        var profile = WeaponAimInfo.Find(TableManager.Instance.WeaponAim, shownId);
        foreach (var entry in entries)
        {
            if (entry.id == shownId && entry.model != null && profile != null)
            {
                var model = entry.model.transform;
                GetSightPoints(model, profile, _weapon, out _, out Vector3 front);
                _point = model.TransformPoint(front);
                return true;
            }
        }
        _point = Vector3.zero;
        return false;
    }

    // 조립형 키트는 활성화된 총열의 소켓을 읽는다. 소켓이 없는 임시 외형은 테이블 좌표를 사용한다.
    public static void GetSightPoints(Transform _model, WeaponAimData.Profile _profile, WeaponInstance _weapon, out Vector3 _rear, out Vector3 _front)
    {
        _rear = _profile.rear;
        _front = WeaponAimInfo.Front(_profile, _weapon);
        Transform rear = null;
        Transform front = null;
        foreach (var child in _model.GetComponentsInChildren<Transform>(true))
        {
            if (child.name != "RearSight" && child.name != "FrontSight") continue;
            bool active = true;
            for (var current = child; current != null && current != _model; current = current.parent)
                if (!current.gameObject.activeSelf) { active = false; break; }
            if (!active) continue;
            if (child.name == "RearSight") rear = child;
            else front = child;
        }
        if (rear == null || front == null) return;
        // 제작 검증의 조준선은 가늠자 바닥에서 1.5mm 위다.
        Vector3 lift = Vector3.up * 0.0015f;
        _rear = _model.InverseTransformPoint(rear.position) + lift;
        _front = _model.InverseTransformPoint(front.position) + lift;
    }

    public bool Has(string _id) => Has(entries, _id);

    public bool HasOffHand(string _id) => Has(offHandEntries, _id);

    public void ApplyParts(WeaponInstance _main, WeaponInstance _offHand)
    {
        ApplyParts(entries, _main);
        ApplyParts(offHandEntries, _offHand);
    }

    private static void ApplyParts(Entry[] _entries, WeaponInstance _weapon)
    {
        foreach (var entry in _entries)
        {
            if (entry.model == null) continue;
            foreach (var visual in entry.model.GetComponentsInChildren<WeaponPartVisual>(true)) visual.Apply(_weapon);
        }
    }

    // 외형 id 모델이 없으면 대체 외형을 켠다. 왼손 외형은 끈다.
    public void Show(string _id, string _fallbackId = null)
    {
        shownId = Has(_id) ? _id : _fallbackId;
        ShowIn(entries, shownId);
        shownOffHandId = null;
        ShowIn(offHandEntries, null);
    }

    // 아킴보: 양손에 같은 외형을 켠다. 왼손 외형이 없는 id면 대체 외형의 왼손 모델을 쓰고, 그것도 없으면 왼손은 비운다.
    public void ShowAkimbo(string _id, string _fallbackId = null)
    {
        shownId = Has(_id) ? _id : _fallbackId;
        ShowIn(entries, shownId);
        shownOffHandId = HasOffHand(_id) ? _id : _fallbackId;
        ShowIn(offHandEntries, shownOffHandId);
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
