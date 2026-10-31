using System;
using System.IO;
using System.Text;
using UnityEngine;

// 판 기록 한 줄씩을 로컬 JSONL 파일(persistentDataPath/Telemetry/<sessionId>.jsonl)에 남긴다.
// 줄바꿈은 \n, BOM 없는 UTF-8로 쓴다. 서버 전송은 TeeLogStore가 붙인 LogKitStore가 맡는다.
// 이 스토어의 모든 예외는 여기서 삼킨다. 쓰기가 한 번 실패하면 이 판의 파일 쓰기만 멈추고, 전송 경로에는 영향을 주지 않는다.
public sealed class FileLogStore : ILogStore
{
    private StreamWriter writer;
    private bool disabled;
    private bool disposed;

    public string FilePath { get; }

    public FileLogStore(string _sessionId)
    {
        try
        {
            string folder = TelemetryCleanup.Folder();
            Directory.CreateDirectory(folder);
            FilePath = Path.Combine(folder, _sessionId + ".jsonl");
            writer = new StreamWriter(FilePath, true, new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    public void Append(string _line)
    {
        if (disposed || disabled)
        {
            return;
        }
        try
        {
            writer.Write(_line);
            writer.Write('\n');
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    public void Flush()
    {
        if (disposed || disabled)
        {
            return;
        }
        try
        {
            writer.Flush();
        }
        catch (Exception exception)
        {
            Disable(exception);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            writer?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[FileLogStore] 파일 닫기 실패(전송에는 영향 없음): {exception.GetType().Name}: {exception.Message}");
        }
        writer = null;
    }

    // 실패 경고는 한 번만 남기고, 이후 이 판에서는 파일 쓰기를 건너뛴다.
    private void Disable(Exception _exception)
    {
        if (disabled)
        {
            return;
        }
        disabled = true;
        try
        {
            writer?.Dispose();
        }
        catch (Exception)
        {
            // 이미 실패한 스트림을 닫는 중의 오류는 무시한다.
        }
        writer = null;
        Debug.LogWarning($"[FileLogStore] 로컬 판 기록 쓰기를 중단합니다(서버 전송은 계속됩니다): {_exception.GetType().Name}: {_exception.Message}");
    }
}
