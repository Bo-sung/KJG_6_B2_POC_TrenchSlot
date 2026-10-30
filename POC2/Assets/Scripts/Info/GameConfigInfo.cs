using UnityEngine;

// 게임 공통 설정 조회. 테이블이 없으면 0 값을 주며 Validate가 로그로 알린다.
public static class GameConfigInfo
{
    public static int MaxHealth(GameConfigData _table) => _table != null ? _table.maxHealth : 0;

    public static int StartMoney(GameConfigData _table) => _table != null ? _table.startMoney : 0;

    public static int WaveClearHealAmount(GameConfigData _table) => _table != null ? Mathf.Max(0, _table.waveClearHealAmount) : 0;

    public static int WaveClearBonus(GameConfigData _table, int _wave)
    {
        if (_table == null || _wave < 1)
        {
            return 0;
        }
        // 무한 웨이브에서도 곱셈 오버플로로 음수 보상이 되지 않게 한다.
        long bonus = Mathf.Max(0, _table.waveClearBonusBase) + (long)(_wave - 1) * Mathf.Max(0, _table.waveClearBonusPerWave);
        return (int)System.Math.Min(int.MaxValue, bonus);
    }

    public static float IntermissionSeconds(GameConfigData _table) => _table != null ? _table.intermissionSeconds : 0f;

    public static float InteractDistance(GameConfigData _table) => _table != null ? _table.interactDistance : 0f;

    public static int WeaponSlotCount(GameConfigData _table) => _table != null && _table.weaponSlotCount > 0 ? _table.weaponSlotCount : 2;

    // Defaults are compatibility fallbacks for old tables / isolated component tests.
    public static float CrouchHeightRatio(GameConfigData _table) => _table != null && _table.crouchHeightRatio > 0f ? Mathf.Clamp(_table.crouchHeightRatio, 0.1f, 1f) : 0.55f;
    public static float CrouchSpeedRatio(GameConfigData _table) => _table != null && _table.crouchSpeedRatio > 0f ? Mathf.Clamp01(_table.crouchSpeedRatio) : 0.5f;
    public static float CrouchCameraSmoothSeconds(GameConfigData _table) => _table != null && _table.crouchCameraSmoothSeconds > 0f ? _table.crouchCameraSmoothSeconds : 0.12f;
    public static float DamageDirectionHoldSeconds(GameConfigData _table) => _table != null ? Mathf.Max(0f, _table.damageDirectionHoldSeconds) : 0.2f;
    public static float DamageDirectionFadeSeconds(GameConfigData _table) => _table != null && _table.damageDirectionFadeSeconds > 0f ? _table.damageDirectionFadeSeconds : 0.8f;
    public static float AimFovRatio(GameConfigData _table) => _table != null && _table.aimFovRatio > 0f ? Mathf.Clamp(_table.aimFovRatio, 0.1f, 1f) : 0.7f;
    public static float AimTransitionSeconds(GameConfigData _table) => _table != null && _table.aimTransitionSeconds > 0f ? _table.aimTransitionSeconds : 0.15f;
    public static float RecoilMaxPitchDegrees(GameConfigData _table) => _table != null && _table.recoilMaxPitchDegrees > 0f ? _table.recoilMaxPitchDegrees : 15f;
    public static float RecoilMaxYawDegrees(GameConfigData _table) => _table != null && _table.recoilMaxYawDegrees > 0f ? _table.recoilMaxYawDegrees : 8f;

    public static void Validate(GameConfigData _table)
    {
        if (_table == null)
        {
            Debug.LogError("게임 설정 테이블이 없습니다.");
            return;
        }
        if (_table.maxHealth <= 0)
        {
            Debug.LogError("maxHealth는 1 이상이어야 합니다.");
        }
        if (_table.crouchHeightRatio <= 0f || _table.crouchHeightRatio >= 1f || _table.crouchSpeedRatio <= 0f || _table.crouchSpeedRatio > 1f || _table.crouchCameraSmoothSeconds <= 0f || _table.damageDirectionHoldSeconds < 0f || _table.damageDirectionFadeSeconds <= 0f)
        {
            Debug.LogError("Crouch / damage direction tuning is invalid.");
        }
        if (_table.aimFovRatio <= 0f || _table.aimFovRatio > 1f || _table.aimTransitionSeconds <= 0f)
        {
            Debug.LogError("조준 시야각 비율 또는 전환 시간이 올바르지 않습니다.");
        }
        if (_table.startMoney < 0)
        {
            Debug.LogError("startMoney가 음수입니다.");
        }
        if (_table.waveClearHealAmount < 0 || _table.waveClearBonusBase < 0 || _table.waveClearBonusPerWave < 0)
        {
            Debug.LogError("waveClearHealAmount, waveClearBonusBase, waveClearBonusPerWave는 0 이상이어야 합니다.");
        }
    }
}
