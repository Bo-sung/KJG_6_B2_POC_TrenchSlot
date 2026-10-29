using System;

// Resources/SlotMachineData.json을 옮긴 직렬화 자료다. 여기에 로직을 두지 않는다.
// 조회·판정은 SlotMachineInfo, 로딩과 보유는 TableManager가 담당한다.
// prize 대신 배수(multiplier)를 쓴다. 지급액 = 배수 x 기본 베팅(spinCost), 할인과 무관.
[Serializable]
public sealed class SlotMachineData
{
    [Serializable]
    public sealed class Symbol
    {
        // 식별자. 표시·로그용이며 조회 키는 reelIndex다.
        public string id;
        public string name;
        // 릴 면 번호(0 기반). 결과 배열과 릴 회전 각도(면 번호 x 60도)가 이 값을 쓴다.
        public int reelIndex;
        // 릴 한 칸의 당첨 가중치. 릴마다 독립 추첨하며 전체 가중치로 나눠 확률을 얻는다.
        public int weight;
        // 3릴 일치 시 배수. 0이면 3줄 지급 없음.
        public int multiplier;
        // 2릴 일치 시 배수. 0이면 2줄 지급 없음.
        public int pairMultiplier;
    }

    // 기본 베팅. 할인 퍽이 있어도 지급 기준은 이 값이다.
    public int spinCost;
    // 미스 가중치. v0는 0이며 모든 릴이 면 중 하나를 반드시 낸다.
    public int missWeight;
    // 기본 회전 시간(초). 고속 스핀들 퍽이 이 값에 배율을 곱한다.
    public float spinSeconds;
    public Symbol[] symbols = Array.Empty<Symbol>();
}
