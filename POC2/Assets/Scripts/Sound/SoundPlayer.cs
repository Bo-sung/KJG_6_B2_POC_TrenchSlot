using System;
using UnityEngine;

// 효과음 재생 창구. 음원은 씬에 연결하며, 2D 원샷과 루프 소스 하나씩으로 재생한다.
// 런타임에 AudioSource를 새로 만들지 않는다.
public sealed class SoundPlayer : MonoBehaviour
{
    [Serializable]
    public struct Cue
    {
        public SfxType type;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    [SerializeField] private AudioSource oneShotSource;
    [SerializeField] private AudioSource loopSource;
    [SerializeField] private Cue[] cues = Array.Empty<Cue>();

    private static SoundPlayer instance;

    public static void Play(SfxType _type)
    {
        if (instance == null)
        {
            return;
        }
        instance.PlayOneShot(_type);
    }

    public static void StartLoop(SfxType _type)
    {
        if (instance == null)
        {
            return;
        }
        instance.PlayLoop(_type);
    }

    public static void StopLoop()
    {
        if (instance == null || instance.loopSource == null)
        {
            return;
        }
        instance.loopSource.Stop();
    }

    private void OnEnable()
    {
        instance = this;
    }

    private void OnDisable()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void PlayOneShot(SfxType _type)
    {
        if (!TryFind(_type, out Cue cue) || oneShotSource == null)
        {
            return;
        }
        oneShotSource.PlayOneShot(cue.clip, cue.volume);
    }

    private void PlayLoop(SfxType _type)
    {
        if (!TryFind(_type, out Cue cue) || loopSource == null)
        {
            return;
        }
        loopSource.clip = cue.clip;
        loopSource.volume = cue.volume;
        loopSource.loop = true;
        loopSource.Play();
    }

    private bool TryFind(SfxType _type, out Cue _cue)
    {
        foreach (var cue in cues)
        {
            if (cue.type == _type && cue.clip != null)
            {
                _cue = cue;
                return true;
            }
        }
        _cue = default;
        return false;
    }
}

public enum SfxType
{
    Shot,
    EnemyShot,
    EnemyHit,
    EnemyDown,
    Explosion,
    PlayerHurt,
    Reload,
    AmmoRefill,
    LeverPull,
    ReelSpin,
    ReelStop,
    NearMiss,
    Payout,
    Fail,
    WaveStart,
    WaveClear,
    ShopOpen,
    Click
}
