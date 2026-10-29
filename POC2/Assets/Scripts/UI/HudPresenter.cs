using UnityEngine;

// HUD 규칙(Presenter). 게임·플레이어·슬롯 이벤트를 받아 HudView 값을 바꾼다.
public sealed class HudPresenter : MonoBehaviour
{
    [SerializeField] private HudView view;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerWeapon weapon;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private SlotMachinePresenter slot;

    private float lastHealth = -1f;

    private void Start()
    {
        if (gameManager != null)
        {
            gameManager.WaveStarted += OnWaveStarted;
            gameManager.WaveTimeChanged += OnWaveTimeChanged;
            gameManager.EnemiesCleared += OnEnemiesCleared;
            gameManager.WaveEnded += OnWaveEnded;
            gameManager.KillsChanged += OnKillsChanged;
            gameManager.Wallet.BalanceChanged += OnMoneyChanged;
            gameManager.GameOver += OnGameOver;
            OnMoneyChanged(gameManager.Wallet.Balance);
            OnKillsChanged(gameManager.KillsThisWave);
        }
        if (playerHealth != null)
        {
            playerHealth.HealthChanged += OnHealthChanged;
            OnHealthChanged(playerHealth.CurrentHealth, playerHealth.MaxHealth);
        }
        if (interactor != null)
        {
            interactor.PromptChanged += view.SetPrompt;
        }
        if (slot != null)
        {
            slot.SpinResolved += OnSpinResolved;
        }
        if (weapon != null)
        {
            weapon.EnemyHit += view.ShowHitMarker;
        }
        if (gameManager != null && gameManager.CurrentWave.wave > 0)
        {
            OnWaveStarted(gameManager.CurrentWave);
        }
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.WaveStarted -= OnWaveStarted;
            gameManager.WaveTimeChanged -= OnWaveTimeChanged;
            gameManager.EnemiesCleared -= OnEnemiesCleared;
            gameManager.WaveEnded -= OnWaveEnded;
            gameManager.KillsChanged -= OnKillsChanged;
            gameManager.Wallet.BalanceChanged -= OnMoneyChanged;
            gameManager.GameOver -= OnGameOver;
        }
        if (playerHealth != null)
        {
            playerHealth.HealthChanged -= OnHealthChanged;
        }
        if (interactor != null)
        {
            interactor.PromptChanged -= view.SetPrompt;
        }
        if (slot != null)
        {
            slot.SpinResolved -= OnSpinResolved;
        }
        if (weapon != null)
        {
            weapon.EnemyHit -= view.ShowHitMarker;
        }
    }

    private void Update()
    {
        if (weapon != null)
        {
            view.SetAmmo(weapon.Ammo, weapon.MagazineSize, weapon.IsReloading, weapon.ReloadProgress);
        }
    }

    private void OnHealthChanged(float _current, float _max)
    {
        // 체력이 줄 때마다 붉은 피해 점멸. 사망(0)에서도 줄어든 것이므로 점멸한다.
        if (lastHealth > _current)
        {
            view.ShowDamageFlash();
        }
        if (lastHealth > _current && _current > 0f)
        {
            SoundPlayer.Play(SfxType.PlayerHurt);
        }
        lastHealth = _current;
        view.SetHealth(_current, _max);
    }

    private void OnMoneyChanged(int _amount)
    {
        view.SetMoney(_amount);
    }

    private void OnKillsChanged(int _kills)
    {
        view.SetKills(_kills);
    }

    private void OnWaveStarted(WaveSpec _spec)
    {
        view.SetWave(_spec.wave, _spec.era);
        view.ShowBanner($"WAVE {_spec.wave} 시작");
        SoundPlayer.Play(SfxType.WaveStart);
    }

    private void OnWaveTimeChanged(float _left, float _total)
    {
        view.SetTimer(_left);
    }

    private void OnEnemiesCleared(float _remainingSeconds)
    {
        view.ShowBanner($"전멸! 남은 {Mathf.CeilToInt(_remainingSeconds)}초 동안 슬롯 회전 가능");
        SoundPlayer.Play(SfxType.WaveClear);
    }

    private void OnWaveEnded(int _wave)
    {
        view.ShowBanner($"WAVE {_wave} 종료 · HP +{gameManager.WaveHealthRestored:0.##} · 보너스 +${gameManager.WaveBonusPaid:N0}");
    }

    private void OnGameOver(int _wave, int _kills)
    {
        view.ShowBanner($"사망 · 최종 WAVE {_wave}");
    }

    private void OnSpinResolved(SlotOutcome _outcome, int _payout, string _text)
    {
        view.ShowResult(_text);
    }
}
