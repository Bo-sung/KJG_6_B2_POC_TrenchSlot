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
        var starts = rows.Where(_row => (string)_row["eventType"] == "slot_spin_started").ToArray();
        var terminals = rows.Where(_row => (string)_row["eventType"] == "slot_spin_resolved" ||
            (string)_row["eventType"] == "slot_spin_incomplete").ToArray();
        Require(starts.All(_row => !string.IsNullOrEmpty((string)_row["payload"]["spinId"])), "Missing spin ID");
        Require(starts.Select(_row => (string)_row["payload"]["spinId"]).Distinct().Count() == starts.Length, "Duplicate spin start");
        Require(terminals.Length == starts.Length, "Each spin must resolve or close incomplete before session end");
        foreach (JObject start in starts)
        {
            string id = (string)start["payload"]["spinId"];
            var results = terminals.Where(_row => (string)_row["payload"]["spinId"] == id).ToArray();
            Require(results.Length == 1 && (long)results[0]["sequence"] > (long)start["sequence"], "Spin terminal identity or order mismatch");
            Require(start["payload"]["faces"] is JArray faces && faces.Count == 3, "Expected original three faces");
            if ((string)results[0]["eventType"] == "slot_spin_resolved")
            {
                Require(JToken.DeepEquals(start["payload"]["faces"], results[0]["payload"]["faces"]) &&
                    JToken.DeepEquals(start["payload"]["symbolIds"], results[0]["payload"]["symbolIds"]), "Spin result changed its original symbols");
                foreach (JToken grant in results[0]["payload"]["ammoGranted"])
                {
                    string transactionId = (string)grant["transactionId"];
                    var originals = rows.Where(_row => (string)_row["eventType"] == "ammo_changed" &&
                        (string)_row["payload"]["transactionId"] == transactionId).ToArray();
                    Require(!string.IsNullOrEmpty(transactionId) && (string)grant["causeId"] == id &&
                        originals.Length == 1 && JToken.DeepEquals(originals[0]["payload"], grant), "Slot ammo grant does not match canonical transaction");
                }
            }
            else
            {
                Require(results[0]["payload"]["outcome"] == null && results[0]["payload"]["actualMoneyDelta"] == null,
                    "Incomplete spin contains invented reward");
            }
        }
        var offers = rows.Where(_row => (string)_row["eventType"] == "shop_offers_presented").ToArray();
        foreach (var group in offers.GroupBy(_row => (string)_row["payload"]["offerSetId"]))
        {
            Require(group.Select(_row => (int)_row["payload"]["revision"]).SequenceEqual(Enumerable.Range(0, group.Count())),
                "Shop revisions must start at zero and increase once");
        }
        var attempts = rows.Where(_row => (string)_row["eventType"] == "shop_purchase_attempt").ToArray();
        var purchases = rows.Where(_row => (string)_row["eventType"] == "shop_purchase_result" ||
            (string)_row["eventType"] == "shop_purchase_incomplete").ToArray();
        Require(attempts.Select(_row => (string)_row["payload"]["attemptId"]).Distinct().Count() == attempts.Length &&
            attempts.Length == purchases.Length, "Purchase attempts and terminals must be unique and paired");
        foreach (JObject attempt in attempts)
        {
            string id = (string)attempt["payload"]["attemptId"];
            Require(!string.IsNullOrEmpty(id), "Missing purchase attempt ID");
            var results = purchases.Where(_row => (string)_row["payload"]["attemptId"] == id).ToArray();
            Require(results.Length == 1 && (long)results[0]["sequence"] > (long)attempt["sequence"], "Purchase terminal missing or out of order");
            JToken reference = attempt["payload"]["offer"];
            if (reference != null && reference.Type != JTokenType.Null)
            {
                Require(offers.Count(_row => (string)_row["payload"]["offerSetId"] == (string)reference["offerSetId"] &&
                    (int)_row["payload"]["revision"] == (int)reference["revision"] && (long)_row["sequence"] < (long)attempt["sequence"]) == 1,
                    "Purchase references unknown shop presentation");
            }
            if ((string)results[0]["eventType"] == "shop_purchase_result")
            {
                foreach (JToken change in results[0]["payload"]["ammoChanges"])
                {
                    Require((string)change["causeId"] == id && rows.Count(_row => (string)_row["eventType"] == "ammo_changed" &&
                        (string)_row["payload"]["transactionId"] == (string)change["transactionId"] &&
                        JToken.DeepEquals(_row["payload"], change)) == 1, "Purchase ammo change must match its canonical event");
                }
            }
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
