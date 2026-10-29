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
    [SerializeField] private Transform damageView;

    private float lastHealth = -1f;
    private Vector3 damageSource;
    private float directionElapsed = float.PositiveInfinity;
    private GameConfigData feedbackConfig;
    private string interactionPrompt;
    private string pickupPrompt;
    private AmmoInventory ammoInventory;

    private void Start()
    {
        feedbackConfig = TableManager.Instance.GameConfig;
        if (damageView == null && Camera.main != null)
        {
            damageView = Camera.main.transform;
        }
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
            ammoInventory = gameManager.Ammo;
            if (ammoInventory != null)
            {
                ammoInventory.Changed += OnReserveChanged;
            }
        }
        if (playerHealth != null)
        {
            playerHealth.HealthChanged += OnHealthChanged;
            playerHealth.DamagedFrom += OnDamagedFrom;
            OnHealthChanged(playerHealth.CurrentHealth, playerHealth.MaxHealth);
        }
        if (interactor != null)
        {
            interactor.PromptChanged += OnInteractionPromptChanged;
            interactor.PickupPromptChanged += OnPickupPromptChanged;
        }
        if (slot != null)
        {
            slot.SpinResolved += OnSpinResolved;
        }
        if (weapon != null)
        {
            weapon.EnemyHit += view.ShowHitMarker;
            weapon.AmmoChanged += OnAmmoChanged;
            RefreshAmmo();
        }
        if (gameManager != null && gameManager.CurrentWave.wave > 0)
        {
            OnWaveStarted(gameManager.CurrentWave);
        }
    }

    private void OnDestroy()
    {
        if (ammoInventory != null)
        {
            ammoInventory.Changed -= OnReserveChanged;
        }
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
            playerHealth.DamagedFrom -= OnDamagedFrom;
        }
        if (interactor != null)
        {
            interactor.PromptChanged -= OnInteractionPromptChanged;
            interactor.PickupPromptChanged -= OnPickupPromptChanged;
        }
        if (slot != null)
        {
            slot.SpinResolved -= OnSpinResolved;
        }
        if (weapon != null)
        {
            weapon.EnemyHit -= view.ShowHitMarker;
            weapon.AmmoChanged -= OnAmmoChanged;
        }
    }

    private void Update()
    {
        UpdateDamageDirection(Time.deltaTime);
        if (weapon != null && weapon.IsReloading)
        {
            RefreshAmmo();
        }
    }

    private void OnAmmoChanged(int _ammo, int _magazine)
    {
        RefreshAmmo();
    }

    private void OnReserveChanged(string _id, int _reserve, int _maximum)
    {
        if (weapon != null && weapon.AmmoId == _id)
        {
            RefreshAmmo();
        }
    }

    private void RefreshAmmo()
    {
        if (weapon != null && view != null)
        {
            view.SetAmmo(weapon.Ammo, weapon.ReserveAmmo, weapon.IsReloading, weapon.ReloadProgress);
        }
    }

    private void OnDamagedFrom(Vector3 _source)
    {
        damageSource = _source;
        directionElapsed = 0f;
        UpdateDamageDirection(0f);
    }

    private void OnInteractionPromptChanged(string _prompt)
    {
        interactionPrompt = _prompt;
        RefreshPrompt();
    }

    private void OnPickupPromptChanged(string _prompt)
    {
        pickupPrompt = _prompt;
        RefreshPrompt();
    }

    private void RefreshPrompt()
    {
        // Independent interactor events can arrive in either order in the same frame.
        view.SetPrompt(!string.IsNullOrEmpty(pickupPrompt) ? pickupPrompt : interactionPrompt);
    }

    public static float DamageBearing(Transform _view, Vector3 _source)
    {
        Vector3 forward = Vector3.ProjectOnPlane(_view.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(_view.up, Vector3.up);
        }
        Vector3 incoming = Vector3.ProjectOnPlane(_source - _view.position, Vector3.up);
        return Vector3.SignedAngle(forward, incoming, Vector3.up);
    }

    public void UpdateDamageDirection(float _deltaTime)
    {
        directionElapsed += _deltaTime;
        float hold = GameConfigInfo.DamageDirectionHoldSeconds(feedbackConfig);
        float fade = GameConfigInfo.DamageDirectionFadeSeconds(feedbackConfig);
        float alpha = 1f - Mathf.Clamp01((directionElapsed - hold) / fade);
        if (view != null && damageView != null)
        {
            view.SetDamageDirection(DamageBearing(damageView, damageSource), alpha);
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
