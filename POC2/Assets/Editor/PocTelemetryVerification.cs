using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class PocTelemetryVerification
{
    [MenuItem("Tools/POC/Verify Unified Telemetry File")]
    public static void RunChecks()
    {
        if (EditorApplication.isPlaying)
        {
            throw new InvalidOperationException("Stop Play Mode after finishing a game session before checking its file.");
        }
        string directory = Path.Combine(Application.persistentDataPath, "Telemetry");
        string file = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (file == null)
        {
            throw new FileNotFoundException("Play the Game scene and close the session first.", directory);
        }
        JObject[] rows = File.ReadLines(file).Where(_line => !string.IsNullOrWhiteSpace(_line)).Select(JObject.Parse).ToArray();
        Require(rows.Length > 0, "File is empty");
        string sessionId = (string)rows[0]["sessionId"];
        Require(!string.IsNullOrEmpty(sessionId), "Session ID is missing");
        for (int i = 0; i < rows.Length; i++)
        {
            Require((string)rows[i]["sessionId"] == sessionId, "Mixed session IDs");
            Require((long)rows[i]["sequence"] == i + 1L, "Duplicate or missing sequence");
            Require(rows[i]["runId"] == null && rows[i]["runSeconds"] == null, "Legacy run envelope remains");
        }
        Require(rows.Count(_row => (string)_row["eventType"] == "session_start") == 1, "Expected one session start");
        Require(rows.Count(_row => (string)_row["eventType"] == "session_end") == 1, "Expected one session end");
        Require((string)rows.Last()["eventType"] == "session_end", "Records exist after session end");
        foreach (string type in new[] { "player_shot", "projectile_impact" })
        {
            var events = rows.Where(_row => (string)_row["eventType"] == type).ToArray();
            var ids = events.Select(_row => (string)(type == "player_shot"
                ? _row["payload"]["shotId"] : _row["payload"]["impact"]["projectileId"])).ToArray();
            Require(ids.All(_id => !string.IsNullOrEmpty(_id)) && ids.Distinct().Count() == ids.Length, "Duplicate or missing " + type + " identity");
        }
        foreach (var group in rows.Where(_row => (string)_row["eventType"] == "wave_summary" ||
            (string)_row["eventType"] == "wave_summary_correction").GroupBy(_row => (string)_row["payload"]["summaryId"]))
        {
            Require(group.Select(_row => (int)_row["payload"]["revision"]).SequenceEqual(Enumerable.Range(0, group.Count())),
                "Summary revisions must start at zero and increase once per correction");
        }
        Debug.Log("[PocTelemetryVerification] PASS: " + rows.Length + " unified records in " + file);
    }

    private static void Require(bool _condition, string _message)
    {
        if (!_condition)
        {
            throw new InvalidDataException(_message);
        }
    }
}
