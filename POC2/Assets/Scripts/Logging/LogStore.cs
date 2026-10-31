using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public interface ILogStore : IDisposable
{
    void Append(string _line);
    void Flush();
}

public sealed class MemoryLogStore : ILogStore
{
    private readonly List<string> lines = new List<string>();
    private readonly int capacity;
    private bool disposed;

    public IReadOnlyList<string> Lines => lines.AsReadOnly();
    public int FlushCount { get; private set; }
    public bool IsDisposed => disposed;

    public MemoryLogStore(int _capacity)
    {
        if (_capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_capacity));
        }
        capacity = _capacity;
    }

    public void Append(string _line)
    {
        if (disposed || lines.Count >= capacity)
        {
            throw new IOException("Memory log store disposed or full.");
        }
        lines.Add(_line);
    }

    public void Flush()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(MemoryLogStore));
        }
        FlushCount++;
    }

    public void Dispose()
    {
        disposed = true;
    }
}
