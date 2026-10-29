using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD 화면(View). 값은 HudPresenter가 넣고, 이 클래스는 표시와 짧은 페이드만 담당한다.
public sealed class HudView : MonoBehaviour
{
    private const float FADE_SECONDS = 0.5f;
    // 체력이 이 비율 이하이면 경고색으로 바꾼다. 표현 값이라 상수로 둔다(표현 튜닝 예외, 기획노트 §1-7).
    private const float LOW_HEALTH_RATIO = 0.3f;
    private const string HIT_TEXT = "X";
    private const string KILL_TEXT = "처치";
    private const string EMPTY_PROMPT = "빈 탄창 · R 재장전";
    private static readonly Color HIT_COLOR = Color.white;
    private static readonly Color KILL_COLOR = new Color(1f, 0.25f, 0.2f);
    private static readonly Color LOW_HEALTH_COLOR = new Color(1f, 0.55f, 0.1f);
    private static readonly Color EMPTY_AMMO_COLOR = new Color(1f, 0.3f, 0.25f);

    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image healthFill;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private TMP_Text waveText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private TMP_Text killsText;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private TMP_Text bannerText;
    [SerializeField] private CanvasGroup bannerGroup;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private CanvasGroup resultGroup;
    [SerializeField] private CanvasGroup hitMarkerGroup;
    [SerializeField] private TMP_Text hitMarkerText;
    [SerializeField] private Image damageFlash;
    [SerializeField] private CanvasGroup damageDirectionGroup;
    [SerializeField] private RectTransform damageDirectionPivot;
    [SerializeField, Min(0.1f)] private float bannerHoldSeconds = 2f;
    [SerializeField, Min(0.1f)] private float resultHoldSeconds = 2.5f;
    [SerializeField, Min(0.01f)] private float hitMarkerHoldSeconds = 0.08f;
    [SerializeField, Min(0.01f)] private float hitMarkerFadeSeconds = 0.15f;
    [SerializeField, Min(0.01f)] private float damageFlashSeconds = 0.35f;
    [SerializeField, Range(0f, 1f)] private float damageFlashAlpha = 0.45f;

    private Coroutine bannerRoutine;
    private Coroutine resultRoutine;
    private Coroutine hitMarkerRoutine;
    private Coroutine damageRoutine;
    private Color healthBaseColor;
    private Color ammoBaseColor;

    private void Awake()
    {
        healthBaseColor = healthFill.color;
        ammoBaseColor = ammoText.color;
        if (damageFlash != null)
        {
            damageFlash.color = new Color(damageFlash.color.r, damageFlash.color.g, damageFlash.color.b, 0f);
        }
        if (hitMarkerGroup != null)
        {
            hitMarkerGroup.alpha = 0f;
        }
        SetDamageDirection(0f, 0f);
    }

    public void SetDamageDirection(float _bearingDegrees, float _alpha)
    {
        if (damageDirectionGroup != null && damageDirectionPivot != null)
        {
            damageDirectionGroup.alpha = Mathf.Clamp01(_alpha);
            damageDirectionPivot.localRotation = Quaternion.Euler(0f, 0f, -_bearingDegrees);
        }
    }

    public void SetHealth(float _current, float _max)
    {
        healthText.text = $"HP {Mathf.CeilToInt(_current)} / {Mathf.CeilToInt(_max)}";
        float ratio = _max > 0f ? Mathf.Clamp01(_current / _max) : 0f;
        healthFill.fillAmount = ratio;
        bool low = ratio <= LOW_HEALTH_RATIO;
        healthFill.color = low ? LOW_HEALTH_COLOR : healthBaseColor;
        healthText.color = low ? LOW_HEALTH_COLOR : Color.white;
    }

    public void SetMoney(int _amount)
    {
        moneyText.text = $"소지금 ${_amount:N0}";
    }

    public void SetWave(int _wave, string _era)
    {
        waveText.text = $"WAVE {_wave}  ·  {_era}";
    }

    public void SetTimer(float _left)
    {
        timerText.text = $"{Mathf.CeilToInt(_left)}초";
    }

    public void SetAmmo(int _ammo, int _magazine, bool _reloading, float _reloadProgress)
    {
        if (_reloading)
        {
            ammoText.text = $"재장전 {Mathf.FloorToInt(Mathf.Clamp01(_reloadProgress) * 100f)}%";
            ammoText.color = ammoBaseColor;
            return;
        }
        bool empty = _ammo <= 0;
        ammoText.text = empty ? EMPTY_PROMPT : $"{_ammo} / {_magazine}";
        ammoText.color = empty ? EMPTY_AMMO_COLOR : ammoBaseColor;
    }

    public void SetKills(int _kills)
    {
        killsText.text = $"처치 {_kills}";
    }

    public void SetPrompt(string _prompt)
    {
        promptText.text = _prompt;
        promptText.enabled = !string.IsNullOrEmpty(_prompt);
    }

    public void ShowBanner(string _message)
    {
        bannerText.text = _message;
        if (bannerRoutine != null)
        {
            StopCoroutine(bannerRoutine);
        }
        bannerRoutine = StartCoroutine(Flash(bannerGroup, bannerHoldSeconds, FADE_SECONDS));
    }

    public void ShowResult(string _message)
    {
        resultText.text = _message;
        if (resultRoutine != null)
        {
            StopCoroutine(resultRoutine);
        }
        resultRoutine = StartCoroutine(Flash(resultGroup, resultHoldSeconds, FADE_SECONDS));
    }

    // 확정 적중 표시. 처치면 빨간 "처치", 아니면 흰 X. 호출은 PlayerWeapon.EnemyHit(실제 적 피해)에서만 온다.
    public void ShowHitMarker(bool _killed)
    {
        hitMarkerText.text = _killed ? KILL_TEXT : HIT_TEXT;
        hitMarkerText.color = _killed ? KILL_COLOR : HIT_COLOR;
        if (hitMarkerRoutine != null)
        {
            StopCoroutine(hitMarkerRoutine);
        }
        hitMarkerRoutine = StartCoroutine(Flash(hitMarkerGroup, hitMarkerHoldSeconds, hitMarkerFadeSeconds));
    }

    // 피해 화면 붉은 점멸. 체력이 줄 때마다 호출된다.
    public void ShowDamageFlash()
    {
        if (damageRoutine != null)
        {
            StopCoroutine(damageRoutine);
        }
        damageRoutine = StartCoroutine(FadeOutImage(damageFlash, damageFlashAlpha, damageFlashSeconds));
    }

    private static IEnumerator Flash(CanvasGroup _group, float _holdSeconds, float _fadeSeconds)
    {
        if (_group == null)
        {
            yield break;
        }
        _group.alpha = 1f;
        yield return new WaitForSeconds(_holdSeconds);
        float elapsed = 0f;
        while (elapsed < _fadeSeconds)
        {
            elapsed += Time.deltaTime;
            _group.alpha = 1f - Mathf.Clamp01(elapsed / _fadeSeconds);
            yield return null;
        }
        _group.alpha = 0f;
    }

    private static IEnumerator FadeOutImage(Image _image, float _startAlpha, float _seconds)
    {
        if (_image == null)
        {
            yield break;
        }
        Color color = _image.color;
        color.a = _startAlpha;
        _image.color = color;
        float elapsed = 0f;
        while (elapsed < _seconds)
        {
            elapsed += Time.deltaTime;
            color.a = _startAlpha * (1f - Mathf.Clamp01(elapsed / _seconds));
            _image.color = color;
            yield return null;
        }
        color.a = 0f;
        _image.color = color;
    }
}
