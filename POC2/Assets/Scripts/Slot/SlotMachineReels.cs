using System;
using System.Collections;
using UnityEngine;

// 슬롯머신 3D 연출(View). 결과 면을 받아 릴을 돌리고 순서대로 멈춘다. 판정·지급은 SlotMachinePresenter가 한다.
// 회전 중에도 카메라·입력을 잠그지 않는다. 연출은 시간만 쓰고 플레이어 조작과 무관하게 끝까지 진행된다.
public sealed class SlotMachineReels : MonoBehaviour
{
    private const float FACE_ANGLE = 360f / SlotMachineInfo.REEL_FACE_COUNT;
    private const int BASE_EXTRA_TURNS = 8;
    private const int EXTRA_TURNS_PER_REEL = 2;
    private const float LEVER_PULL_SECONDS = 0.35f;
    private const float FIRST_STOP_RATIO = 0.7f;
    private const float STOP_GAP_RATIO = 0.15f;

    [SerializeField] private Transform[] reels;
    [SerializeField] private Transform lever;
    [SerializeField] private Renderer marquee;
    [SerializeField] private Material marqueeGlow;
    [SerializeField] private ParticleSystem payoutParticles;

    private Material marqueeBase;
    private Quaternion restingLever;
    private Coroutine spinRoutine;

    public bool IsSpinning => spinRoutine != null;

    private void Awake()
    {
        if (lever != null)
        {
            restingLever = lever.localRotation;
        }
        if (marquee != null)
        {
            marqueeBase = marquee.sharedMaterial;
        }
    }

    // 회전을 시작하면 true. 릴 연결이 없거나 이미 도는 중이면 false(호출측은 지급하지 않는다).
    public bool BeginSpin(int[] _faces, float _spinSeconds, Action _onStopped)
    {
        if (IsSpinning || reels == null || reels.Length != SlotMachineInfo.REEL_COUNT)
        {
            return false;
        }
        if (_faces == null || _faces.Length != SlotMachineInfo.REEL_COUNT)
        {
            return false;
        }
        spinRoutine = StartCoroutine(SpinRoutine(_faces, Mathf.Max(0.1f, _spinSeconds), _onStopped));
        return true;
    }

    public void PlayPayout(int _count)
    {
        if (payoutParticles != null)
        {
            payoutParticles.Emit(_count);
        }
    }

    private IEnumerator SpinRoutine(int[] _faces, float _spinSeconds, Action _onStopped)
    {
        SoundPlayer.Play(SfxType.LeverPull);
        SoundPlayer.StartLoop(SfxType.ReelSpin);
        SetMarqueeGlow(true);

        float[] startAngles = new float[SlotMachineInfo.REEL_COUNT];
        float[] totalAngles = new float[SlotMachineInfo.REEL_COUNT];
        float[] stopTimes = new float[SlotMachineInfo.REEL_COUNT];
        bool[] stopped = new bool[SlotMachineInfo.REEL_COUNT];
        for (int i = 0; i < SlotMachineInfo.REEL_COUNT; i++)
        {
            startAngles[i] = reels[i].localEulerAngles.x;
            float targetAngle = _faces[i] * FACE_ANGLE;
            float delta = Mathf.Repeat(targetAngle - startAngles[i], 360f);
            totalAngles[i] = 360f * (BASE_EXTRA_TURNS + i * EXTRA_TURNS_PER_REEL) + delta;
            stopTimes[i] = _spinSeconds * (FIRST_STOP_RATIO + STOP_GAP_RATIO * i);
        }

        float elapsed = 0f;
        float longest = stopTimes[SlotMachineInfo.REEL_COUNT - 1];
        while (elapsed < longest)
        {
            elapsed += Time.deltaTime;
            for (int i = 0; i < SlotMachineInfo.REEL_COUNT; i++)
            {
                if (stopped[i])
                {
                    continue;
                }
                float progress = Mathf.Clamp01(elapsed / stopTimes[i]);
                float angle = startAngles[i] + totalAngles[i] * EaseOutCubic(progress);
                reels[i].localRotation = Quaternion.Euler(angle, 0f, 0f);
                if (progress >= 1f)
                {
                    stopped[i] = true;
                    SoundPlayer.Play(SfxType.ReelStop);
                }
            }
            UpdateLever(elapsed);
            yield return null;
        }

        for (int i = 0; i < SlotMachineInfo.REEL_COUNT; i++)
        {
            reels[i].localRotation = Quaternion.Euler(_faces[i] * FACE_ANGLE, 0f, 0f);
        }
        SoundPlayer.StopLoop();
        if (lever != null)
        {
            lever.localRotation = restingLever;
        }
        SetMarqueeGlow(false);
        spinRoutine = null;
        _onStopped?.Invoke();
    }

    private void UpdateLever(float _elapsed)
    {
        if (lever == null)
        {
            return;
        }
        float pull = _elapsed < LEVER_PULL_SECONDS
            ? _elapsed / LEVER_PULL_SECONDS
            : Mathf.Clamp01(1f - (_elapsed - LEVER_PULL_SECONDS) / LEVER_PULL_SECONDS);
        lever.localRotation = restingLever * Quaternion.Euler(-60f * pull, 0f, 0f);
    }

    private void SetMarqueeGlow(bool _on)
    {
        if (marquee == null || marqueeGlow == null)
        {
            return;
        }
        marquee.sharedMaterial = _on ? marqueeGlow : marqueeBase;
    }

    private static float EaseOutCubic(float _t)
    {
        float inverse = 1f - _t;
        return 1f - inverse * inverse * inverse;
    }
}
