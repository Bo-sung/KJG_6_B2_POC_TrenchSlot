using System;
using System.Collections.Generic;

public sealed class AmmoInventory
{
    private sealed class Stock
    {
        public int reserve;
        public int maximum;
    }

    private readonly Dictionary<string, Stock> stocks = new Dictionary<string, Stock>(StringComparer.Ordinal);

    // 탄종 ID, 현재 예비탄, 현재 최대량. 실제 상태가 변할 때만 알린다.
    public event Action<string, int, int> Changed;

    public LogAmmoCollector AmmoLog { get; set; }

    public AmmoInventory(AmmoData _table)
    {
        if (_table == null || _table.ammoTypes == null)
        {
            return;
        }
        foreach (var entry in _table.ammoTypes)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || stocks.ContainsKey(entry.id) ||
                entry.referenceMagazineSize <= 0 || entry.maxReserve < 0 || entry.startingReserve < 0 || entry.startingReserve > entry.maxReserve)
            {
                continue;
            }
            stocks.Add(entry.id, new Stock { reserve = entry.startingReserve, maximum = entry.maxReserve });
        }
    }

    public bool Contains(string _id)
    {
        return _id != null && stocks.ContainsKey(_id);
    }

    public int GetReserve(string _id)
    {
        return _id != null && stocks.TryGetValue(_id, out var stock) ? stock.reserve : 0;
    }

    public int GetMaximum(string _id)
    {
        return _id != null && stocks.TryGetValue(_id, out var stock) ? stock.maximum : 0;
    }

    public int Add(string _id, int _amount, string _source = "unknown", string _causeId = null)
    {
        if (_amount < 0 || _id == null || !stocks.TryGetValue(_id, out var stock))
        {
            return 0;
        }
        int added = Math.Min(_amount, stock.maximum - stock.reserve);
        if (added > 0)
        {
            stock.reserve += added;
        }
        AmmoLog?.Change(new LogAmmoChange { source = _source, causeId = _causeId,
            ammoTypeId = _id, reserveDelta = added, acquired = _source == "capacity_return" ? 0 : added });
        if (added > 0)
        {
            Changed?.Invoke(_id, stock.reserve, stock.maximum);
        }
        return added;
    }

    public int Consume(string _id, int _amount)
    {
        if (_amount <= 0 || _id == null || !stocks.TryGetValue(_id, out var stock))
        {
            return 0;
        }
        int consumed = Math.Min(_amount, stock.reserve);
        if (consumed > 0)
        {
            stock.reserve -= consumed;
            Changed?.Invoke(_id, stock.reserve, stock.maximum);
        }
        return consumed;
    }

    // 초과분 폐기 여부는 호출자가 명시한다. false일 때 현재량보다 낮추는 요청은 상태 변경 없이 거절한다.
    public bool SetMaximum(string _id, int _maximum, bool _discardExcess)
    {
        if (_maximum < 0 || _id == null || !stocks.TryGetValue(_id, out var stock) ||
            (!_discardExcess && stock.reserve > _maximum))
        {
            return false;
        }
        if (stock.maximum == _maximum)
        {
            return true;
        }
        int previousMaximum = stock.maximum;
        int previousReserve = stock.reserve;
        stock.maximum = _maximum;
        stock.reserve = Math.Min(stock.reserve, _maximum);
        AmmoLog?.Change(new LogAmmoChange { source = "maximum_changed", ammoTypeId = _id,
            reserveDelta = stock.reserve - previousReserve, discarded = previousReserve - stock.reserve,
            reserveMaximumBefore = previousMaximum, reserveMaximumAfter = stock.maximum });
        Changed?.Invoke(_id, stock.reserve, stock.maximum);
        return true;
    }
}
