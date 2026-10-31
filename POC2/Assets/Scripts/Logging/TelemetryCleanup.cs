using System;
using System.IO;
using UnityEngine;

// 로컬 판 기록(persistentDataPath/Telemetry/*.jsonl) 정리. 최근 KEEP_COUNT개만 남긴다.
// 서버 전송 여부는 확인하지 않는다. 전송은 LogKit이 자체 Logs/ 폴더로 맡고, 여기서는 로컬 사본만 줄인다.
// 프로세스당 한 번, 첫 판을 만들기 직전에만 실행한다(판 종료 시점에는 지우지 않는다).
public static class TelemetryCleanup
{
    public const string FOLDER_NAME = "Telemetry";
    // 현재 판 파일을 포함해 남기는 최대 개수.
    public const int KEEP_COUNT = 20;
    private const string FILE_PATTERN = "*.jsonl";

    private static bool trimmedThisRun;
    private static bool warned;

    public static string Folder()
    {
        return Path.Combine(Application.persistentDataPath, FOLDER_NAME);
    }

    public static void TrimOnceForThisRun()
    {
        if (trimmedThisRun)
        {
            return;
        }
        trimmedThisRun = true;
        try
        {
            string folder = Folder();
            if (!Directory.Exists(folder))
            {
                return;
            }
            FileInfo[] files = new DirectoryInfo(folder).GetFiles(FILE_PATTERN);
            // 새 판 파일이 곧 생기므로 기존 파일은 KEEP_COUNT - 1개까지만 남긴다.
            int keepExisting = Math.Max(0, KEEP_COUNT - 1);
            Array.Sort(files, (_a, _b) => _b.LastWriteTimeUtc.CompareTo(_a.LastWriteTimeUtc));
            for (int i = keepExisting; i < files.Length; i++)
            {
                try
                {
                    files[i].Delete();
                }
                catch (Exception exception)
                {
                    WarnOnce($"오래된 판 기록 삭제 실패(게임은 계속): {files[i].Name}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            WarnOnce($"판 기록 정리 실패(게임은 계속): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void WarnOnce(string _message)
    {
        if (warned)
        {
            return;
        }
        warned = true;
        Debug.LogWarning($"[TelemetryCleanup] {_message}");
    }
}
