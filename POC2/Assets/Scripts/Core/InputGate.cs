// 플레이어 조작(이동·시점·사격) 허용 여부. 정비 상점과 게임 오버 화면에서만 끈다.
// 슬롯 회전 중에는 끄지 않는다(작업지침 §6: 회전 중 카메라 고정·입력 차단 금지).
public static class InputGate
{
    public static bool PlayerEnabled { get; set; } = true;
}
