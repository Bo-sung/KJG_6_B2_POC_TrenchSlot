using System;
using UnityEngine;

// 같은 줄을 여러 저장소에 나란히 넘긴다. 현재는 로컬 JSONL(FileLogStore)과 LogKit 전송(LogKitStore).
// 스토어마다 try/catch로 따로 감싸서, 한쪽이 던진 예외가 다른 쪽이나 LogSession으로 번지지 않게 한다.
// 실패 경고는 스토어당 한 번만 남긴다.
public sealed class TeeLogStore : ILogStore
{
    private readonly ILogStore[] stores;
    private readonly bool[] warned;

    public TeeLogStore(params ILogStore[] _stores)
    {
        stores = _stores ?? throw new ArgumentNullException(nameof(_stores));
        warned = new bool[stores.Length];
    }

    public void Append(string _line)
    {
        for (int i = 0; i < stores.Length; i++)
        {
            try
            {
                stores[i].Append(_line);
            }
            catch (Exception exception)
            {
                Warn(i, "Append", exception);
            }
        }
    }

    public void Flush()
    {
        for (int i = 0; i < stores.Length; i++)
        {
            try
            {
                stores[i].Flush();
            }
            catch (Exception exception)
            {
                Warn(i, "Flush", exception);
            }
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < stores.Length; i++)
        {
            try
            {
                stores[i].Dispose();
            }
            catch (Exception exception)
            {
                Warn(i, "Dispose", exception);
            }
        }
    }

    private void Warn(int _index, string _operation, Exception _exception)
    {
        if (warned[_index])
        {
            return;
        }
        warned[_index] = true;
        Debug.LogWarning($"[TeeLogStore] {stores[_index].GetType().Name}.{_operation} 실패(다른 저장소는 계속 동작): {_exception.GetType().Name}: {_exception.Message}");
    }
}
