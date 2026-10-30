using System;

// Resources/GameConfigData.json의 직렬화 자료. 로직 없음. 조회는 GameConfigInfo가 담당한다.
[Serializable]
public sealed class GameConfigData
{
    public int maxHealth;
    public int startMoney;
    // 웨이브 시간 만료 후 생존 정비 진입 보상(잠정 밸런스).
    public int waveClearHealAmount;
    public int waveClearBonusBase;
    public int waveClearBonusPerWave;
    // 정비 시간(초). 0 이하이면 자동 진행 없이 버튼으로만 다음 웨이브를 시작한다.
    public float intermissionSeconds;
    // 슬롯 상호작용 거리(m).
    public float interactDistance;
    // 휴대 무기 슬롯 수(1·2번 키 전환).
    public int weaponSlotCount = 2;
    // Provisional crouch / presentation tuning; defaults preserve older table callers.
    public float crouchHeightRatio = 0.55f;
    public float crouchSpeedRatio = 0.5f;
    public float crouchCameraSmoothSeconds = 0.12f;
    public float damageDirectionHoldSeconds = 0.2f;
    public float damageDirectionFadeSeconds = 0.8f;
    // 조준 시 기본 카메라 시야각의 비율과 전환 시간(초).
    public float aimFovRatio = 0.7f;
    public float aimTransitionSeconds = 0.15f;
    public float recoilMaxPitchDegrees = 15f;
    public float recoilMaxYawDegrees = 8f;
}
