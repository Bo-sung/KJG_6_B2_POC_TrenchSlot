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

public sealed class FileLogStore : ILogStore
{
    private readonly string filePath;
    private StreamWriter writer;
    private bool disposed;

    public string FilePath => filePath;

    public FileLogStore(string _persistentDataPath, string _sessionId)
    {
        if (string.IsNullOrWhiteSpace(_persistentDataPath) || !Path.IsPathRooted(_persistentDataPath) ||
            !Guid.TryParseExact(_sessionId, "N", out Guid sessionGuid))
        {
            throw new ArgumentException("An absolute persistent data path and a GUID session ID are required.");
        }
        filePath = Path.Combine(Path.GetFullPath(_persistentDataPath), "Telemetry", sessionGuid.ToString("N") + ".jsonl");
    }

    public void Append(string _line)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(FileLogStore));
        }
        if (writer == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            writer = new StreamWriter(new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));
        }
        writer.Write(_line);
        writer.Write('\n');
    }

    public void Flush()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(FileLogStore));
        }
        writer?.Flush();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        writer?.Dispose();
    }
}
