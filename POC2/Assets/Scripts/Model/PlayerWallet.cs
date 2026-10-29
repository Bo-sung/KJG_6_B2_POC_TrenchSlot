using System;

// 플레이어 소지금(화폐). 처치 보상과 슬롯 지급은 여기로 들어오고, 슬롯 회전 비용과 상점 구매는 여기서 나간다.
public sealed class PlayerWallet
{
    public int Balance { get; private set; }

    public event Action<int> BalanceChanged;

    public PlayerWallet(int _startMoney)
    {
        Balance = Math.Max(0, _startMoney);
    }

    public bool CanAfford(int _price) => _price >= 0 && Balance >= _price;

    public void Add(int _amount)
    {
        if (_amount <= 0)
        {
            return;
        }
        Balance += _amount;
        BalanceChanged?.Invoke(Balance);
    }

    // 금액이 부족하면 아무 것도 바꾸지 않고 false를 준다.
    public bool TrySpend(int _price)
    {
        if (!CanAfford(_price))
        {
            return false;
        }
        Balance -= _price;
        BalanceChanged?.Invoke(Balance);
        return true;
    }
}
