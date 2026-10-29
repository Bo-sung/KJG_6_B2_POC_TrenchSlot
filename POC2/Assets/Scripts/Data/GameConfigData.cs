using System;

// Resources/GameConfigData.json의 직렬화 자료. 로직 없음. 조회는 GameConfigInfo가 담당한다.
[Serializable]
public sealed class GameConfigData
{
    public int maxHealth;
    public int startMoney;
    // 정비 상점의 회복 아이템이 회복하는 체력.
    public int healAmount;
    // 웨이브 시간 만료 후 생존 정비 진입 보상(잠정 밸런스).
    public int waveClearHealAmount;
    public int waveClearBonusBase;
    public int waveClearBonusPerWave;
    // 정비 시간(초). 0 이하이면 자동 진행 없이 버튼으로만 다음 웨이브를 시작한다.
    public float intermissionSeconds;
    // 슬롯 상호작용 거리(m).
    public float interactDistance;
}
