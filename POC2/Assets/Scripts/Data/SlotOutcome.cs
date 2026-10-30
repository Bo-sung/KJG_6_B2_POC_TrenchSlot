// 한 번의 회전 결과 판정. SlotMachineInfo.Evaluate가 만든다.
public struct SlotOutcome
{
    // 3릴 일치면 해당 면 번호, 아니면 -1.
    public int tripleFace;
    // 2릴 일치 면 번호(3릴 일치 제외), 없으면 -1.
    public int pairFace;
    // 배수. 3줄이면 multiplier, 2줄이면 pairMultiplier, 없으면 0.
    public int multiplier;
    // 탄약상자 3줄은 예비탄 전량 충전이다.
    public bool hasAmmo;
    public int partCount;
    public int nextEraPartCount;
    public bool isMiss;
    // 2릴이 맞고 나머지 하나가 빗나간 경우. 효과음 연출에 쓴다.
    public bool isNearMiss;
}
