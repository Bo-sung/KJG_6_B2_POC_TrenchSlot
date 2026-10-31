using System;
using System.Collections.Generic;
using UnityEngine;

// 사격 시 외형 부품을 움직인다(노리쇠 후퇴·전진, 방아쇠 후퇴·전진, 공이치기 등).
// 한 발마다 한 사이클을 처음부터 재생하고, 끝나면 부품을 원래 자세로 되돌린다.
// 사이클 길이는 발사 간격에 맞춰 줄어들어 자동 사격에서도 연사를 따라간다.
public sealed class WeaponFireMotion : MonoBehaviour
{
    public enum Kind { Slide, Turn }

    // Slide: axis 방향으로 amount(m)만큼 이동. Turn: axis 축으로 amount(°)만큼 회전.
    // start·window는 한 사이클 안의 구간(0~1). 구간 안에서 원위치→최대→원위치로 움직인다.
    public struct Motion
    {
        public string node;
        public Kind kind;
        public Vector3 axis;
        public float amount;
        public float start;
        public float window;
    }

    private const float MAX_CYCLE_SECONDS = 0.4f;
    // 다음 발이 나가기 전에 사이클이 끝나도록 발사 간격의 일부만 쓴다.
    private const float CYCLE_SHARE = 0.8f;

    private sealed class Track
    {
        public Transform node;
        public Vector3 restPosition;
        public Quaternion restRotation;
        public Motion motion;
    }

    private readonly List<Track> tracks = new List<Track>();
    private float cycleSeconds;
    // 0 이상이면 재생 중인 경과 시간, 음수면 대기.
    private float elapsed = -1f;

    public void Setup(Motion[] _motions)
    {
        tracks.Clear();
        if (_motions == null || _motions.Length == 0) return;
        var transforms = GetComponentsInChildren<Transform>(true);
        foreach (var motion in _motions)
        {
            Transform node = null;
            foreach (var candidate in transforms)
            {
                if (candidate.name == motion.node)
                {
                    node = candidate;
                    break;
                }
            }
            if (node == null) continue;
            tracks.Add(new Track
            {
                node = node,
                restPosition = node.localPosition,
                restRotation = node.localRotation,
                motion = motion,
            });
        }
    }

    public void PlayFire(float _shotIntervalSeconds)
    {
        if (tracks.Count == 0 || _shotIntervalSeconds <= 0f) return;
        cycleSeconds = Mathf.Min(MAX_CYCLE_SECONDS, CYCLE_SHARE * _shotIntervalSeconds);
        elapsed = 0f;
    }

    private void Update()
    {
        if (elapsed < 0f) return;
        elapsed += Time.deltaTime;
        float t = cycleSeconds > 0f ? elapsed / cycleSeconds : 1f;
        if (t >= 1f)
        {
            Stop();
            return;
        }
        foreach (var track in tracks)
        {
            var m = track.motion;
            float hump = 0f;
            if (m.window > 0f)
            {
                float u = Mathf.Clamp01((t - m.start) / m.window);
                hump = Mathf.Sin(Mathf.PI * u);
            }
            if (m.kind == Kind.Slide)
            {
                track.node.localPosition = track.restPosition + m.axis.normalized * (m.amount * hump);
            }
            else
            {
                track.node.localRotation = track.restRotation * Quaternion.AngleAxis(m.amount * hump, m.axis.normalized);
            }
        }
    }

    private void OnDisable()
    {
        Stop();
    }

    private void Stop()
    {
        elapsed = -1f;
        foreach (var track in tracks)
        {
            track.node.localPosition = track.restPosition;
            track.node.localRotation = track.restRotation;
        }
    }
}

// 외형 id(WeaponData.modelId)별 사격 동작. 부품 이름은 외형 프리팹·FBX 노드 이름과 맞아야 한다.
// 축 규칙: 총구는 +Z, 후방은 -Z. Turn의 양수 각도는 부품 윗부분을 후방(-Z)으로 넘긴다.
public static class WeaponFireMotionTable
{
    private static readonly WeaponFireMotion.Motion[] NONE = Array.Empty<WeaponFireMotion.Motion>();

    public static WeaponFireMotion.Motion[] Find(string _modelId)
    {
        switch (_modelId)
        {
            // 볼트액션: 손잡이가 후퇴했다가 전진한다.
            case "bolt":
                return new[] { Slide("BoltHandle", 0.08f, 0.05f, 0.9f) };
            // 펌프액션 산탄총: 앞 손잡이가 후퇴했다가 전진한다.
            case "shotgun":
                return new[] { Slide("Pump", 0.12f, 0.1f, 0.7f) };
            // 기관단총: 노리쇠 상자가 짧게 후퇴한다(전용 노리쇠 부품이 없어 임시 동작).
            case "smg":
                return new[] { Slide("Body", 0.02f, 0f, 0.9f) };
            // 리볼버: 공이치기가 젖혀졌다 떨어지고 방아쇠가 후퇴한다.
            case "colt_saa":
                return new[] { Turn("hammer", 30f, 0f, 0.5f), Slide("trigger", 0.004f, 0f, 0.6f) };
            // 쌍열 산탄총: 양쪽 공이치기와 방아쇠.
            case "colt_1878_shotgun":
                return new[]
                {
                    Turn("hammer_l", 30f, 0f, 0.5f), Turn("hammer_r", 30f, 0f, 0.5f),
                    Slide("trigger_front", 0.004f, 0f, 0.6f), Slide("trigger_rear", 0.004f, 0f, 0.6f),
                };
            // 레버액션: 공이치기와 방아쇠. 레버 부품은 기준점 확인 후 추가한다.
            case "winchester_1886":
                return new[] { Turn("hammer", 30f, 0f, 0.5f), Slide("trigger", 0.004f, 0f, 0.6f) };
            // MP18: 장전 손잡이(노리쇠 연동)가 후퇴했다가 전진하고 방아쇠가 후퇴한다.
            case "mp18":
                return new[]
                {
                    Slide("cocking_handle_stem", 0.06f, 0.05f, 0.85f),
                    Slide("cocking_handle_hook", 0.06f, 0.05f, 0.85f),
                    Slide("trigger", 0.004f, 0f, 0.6f),
                };
            // SMLE: 방아쇠. 볼트 부품 이름은 외형에서 확인되지 않아 제외했다.
            case "smle_mk3":
                return new[] { Slide("trigger.004", 0.004f, 0f, 0.6f) };
            // 루거 P08·위체스터 1897: 부품 이름을 외형에서 확인하지 못해 아직 동작이 없다.
            default:
                return NONE;
        }
    }

    private static WeaponFireMotion.Motion Slide(string _node, float _meters, float _start, float _window)
    {
        return new WeaponFireMotion.Motion
        {
            node = _node,
            kind = WeaponFireMotion.Kind.Slide,
            axis = Vector3.back,
            amount = _meters,
            start = _start,
            window = _window,
        };
    }

    private static WeaponFireMotion.Motion Turn(string _node, float _degrees, float _start, float _window)
    {
        return new WeaponFireMotion.Motion
        {
            node = _node,
            kind = WeaponFireMotion.Kind.Turn,
            axis = Vector3.left,
            amount = _degrees,
            start = _start,
            window = _window,
        };
    }
}
