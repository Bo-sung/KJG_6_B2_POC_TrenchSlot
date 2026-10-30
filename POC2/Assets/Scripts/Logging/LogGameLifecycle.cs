using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class LogGameLifecycle
{
    private readonly LogSession session;
    private readonly List<ILogSessionEndObserver> endObservers = new List<ILogSessionEndObserver>();
    private readonly Dictionary<string, ILogWaveSummaryCollector> collectors = new Dictionary<string, ILogWaveSummaryCollector>(StringComparer.Ordinal);
    private readonly HashSet<string> failedCollectors = new HashSet<string>(StringComparer.Ordinal);
    private bool started;
    private bool ended;
    private bool ending;
    private bool closingWave;
    private bool waveOpen;
    private int waveNumber;
    private int? finalReachedWave;
    private int? lastCompletedWave;
    private int waveKills;
    private int totalKills;
    private double waveStarted;
    private double? clearedAt;
    private JToken wavePlan;
    private JToken startPlayer;
    private JToken lastDamage;

    public LogSession Session => session;
    public bool IsActive => started && !ended;
    public bool IsWaveOpen => waveOpen;

    public LogGameLifecycle(LogSession _session)
    {
        session = _session ?? throw new ArgumentNullException(nameof(_session));
    }

    public bool Start(string _buildVersion, string _buildGuid, string _dataFingerprint, string _mapId)
    {
        if (started || ended)
        {
            return false;
        }
        started = true;
        session.SetContext(null, "initializing");
        session.TryRecord("session_start", new
        {
            utc = session.StartedUtc,
            buildVersion = Known(_buildVersion),
            buildGuid = Known(_buildGuid),
            dataVersion = (string)null,
            dataFingerprint = Known(_dataFingerprint),
            mapId = Known(_mapId),
            fixedDeltaTime = session.FixedDeltaTime
        });
        return true;
    }

    public bool RegisterCollector(ILogWaveSummaryCollector _collector)
    {
        if (ended || ending || waveOpen || closingWave || _collector == null ||
            string.IsNullOrWhiteSpace(_collector.Key) || collectors.ContainsKey(_collector.Key))
        {
            return false;
        }
        collectors.Add(_collector.Key, _collector);
        return true;
    }

    public bool RegisterSessionEndObserver(ILogSessionEndObserver _observer)
    {
        if (ended || ending || _observer == null || endObservers.Contains(_observer))
        {
            return false;
        }
        endObservers.Add(_observer);
        return true;
    }

    public void AdvanceFixedUpdate(double _fixedDeltaTime)
    {
        if (IsActive && !ending)
        {
            session.AdvanceFixedUpdate(_fixedDeltaTime);
        }
    }

    public void SetPhase(string _phase)
    {
        if (IsActive && !ending)
        {
            session.SetContext(finalReachedWave, _phase);
        }
    }

    public bool BeginWave(LogWavePlan _plan, LogPlayerSnapshot _player)
    {
        if (!IsActive || ending || waveOpen || closingWave || _plan == null || _plan.waveNumber <= 0 ||
            (finalReachedWave.HasValue && _plan.waveNumber <= finalReachedWave.Value))
        {
            return false;
        }
        JToken plan = LogJson.CopyPayload(_plan);
        JToken player = LogJson.CopyPayload(_player);
        waveNumber = _plan.waveNumber;
        finalReachedWave = waveNumber;
        wavePlan = plan;
        startPlayer = player;
        waveStarted = session.ElapsedSeconds;
        waveKills = 0;
        clearedAt = null;
        waveOpen = true;
        failedCollectors.Clear();
        session.SetContext(waveNumber, "wave");
        session.TryRecord("wave_start", new { plan = wavePlan, player = startPlayer });
        LogWaveBoundary boundary = Boundary("started", null);
        foreach (var entry in collectors)
        {
            try
            {
                entry.Value.BeginWave(boundary);
            }
            catch (Exception exception)
            {
                failedCollectors.Add(entry.Key);
                session.ReportCollectionFailure(exception);
            }
        }
        return true;
    }

    public void EnemyKilled()
    {
        if (IsActive && waveOpen && !ending && !closingWave)
        {
            waveKills++;
            totalKills++;
        }
    }

    public bool EnemiesCleared()
    {
        if (!IsActive || ending || !waveOpen || clearedAt.HasValue)
        {
            return false;
        }
        clearedAt = session.ElapsedSeconds;
        session.TryRecord("enemies_cleared", new
        {
            clearedElapsedSeconds = clearedAt,
            waveElapsedSeconds = session.ElapsedSeconds - waveStarted,
            kills = waveKills
        });
        return true;
    }

    public void SetLastDamage(LogDamageSnapshot _damage)
    {
        if (IsActive && !ending)
        {
            lastDamage = LogJson.CopyPayload(_damage);
        }
    }

    public bool CompleteWave(LogPlayerSnapshot _player, LogEnemySnapshot _enemiesBeforeCleanup,
        int _actualBonus, double _actualHealthRestored)
    {
        if (!IsActive || ending || !waveOpen || closingWave)
        {
            return false;
        }
        return CloseWave("completed", "time_limit", _player, _enemiesBeforeCleanup, _actualBonus, _actualHealthRestored);
    }

    public bool End(string _reason, LogPlayerSnapshot _player, LogEnemySnapshot _enemiesBeforeCleanup, bool _death = false)
    {
        if (!IsActive || ending || closingWave)
        {
            return false;
        }
        ending = true;
        string reason = _death ? "death" : Known(_reason) ?? "unknown";
        try
        {
            if (_death)
            {
                session.TryRecord("death_snapshot", new
                {
                    player = _player,
                    enemies = _enemiesBeforeCleanup,
                    lastDamage = lastDamage ?? LogJson.CopyPayload(new LogDamageSnapshot())
                });
            }
            if (waveOpen)
            {
                CloseWave("interrupted", reason, _player, _enemiesBeforeCleanup, null, null);
            }
            foreach (ILogSessionEndObserver observer in endObservers.ToArray())
            {
                try
                {
                    observer.SessionEnding(reason);
                }
                catch (Exception exception)
                {
                    session.ReportCollectionFailure(exception);
                }
            }
            session.SetContext(finalReachedWave, "ended");
            session.TryRecord("session_end", new
            {
                utc = session.ReadUtc(),
                playSeconds = session.ElapsedSeconds,
                reason,
                finalReachedWave,
                lastCompletedWave,
                totalKills
            });
        }
        finally
        {
            ended = true;
            endObservers.Clear();
            waveOpen = false;
            session.Dispose();
        }
        return true;
    }

    public static string ComputeDataFingerprint(object _loadedTables)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(LogJson.CopyPayload(_loadedTables).ToString(Formatting.None));
        using (SHA256 hash = SHA256.Create())
        {
            return "sha256:" + BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }

    private bool CloseWave(string _status, string _reason, LogPlayerSnapshot _player,
        LogEnemySnapshot _enemiesBeforeCleanup, int? _bonus, double? _healthRestored)
    {
        if (!session.TryCloseSummary(waveNumber, out string summaryId))
        {
            return false;
        }
        waveOpen = false;
        closingWave = true;
        try
        {
            if (_status == "completed")
            {
                lastCompletedWave = waveNumber;
            }
            var summaries = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["combat"] = new { collectionStatus = "not_collected", data = (object)null },
                ["ammo"] = new { collectionStatus = "not_collected", data = (object)null }
            };
            LogWaveBoundary boundary = Boundary(_status, _reason);
            foreach (var entry in collectors)
            {
                try
                {
                    if (failedCollectors.Contains(entry.Key))
                    {
                        summaries[entry.Key] = new { collectionStatus = "failed", data = (object)null };
                        continue;
                    }
                    object result = entry.Value.CloseWave(boundary);
                    JToken copy = LogJson.CopyPayload(result);
                    summaries[entry.Key] = new { collectionStatus = result == null ? "not_collected" : "collected", data = copy };
                }
                catch (Exception exception)
                {
                    summaries[entry.Key] = new { collectionStatus = "failed", data = (object)null };
                    session.ReportCollectionFailure(exception);
                }
            }
            var summary = LogJson.CopyPayload(new
            {
                summaryId,
                revision = 0,
                status = _status,
                reason = _reason,
                startedElapsedSeconds = waveStarted,
                endedElapsedSeconds = session.ElapsedSeconds,
                waveElapsedSeconds = session.ElapsedSeconds - waveStarted,
                plan = wavePlan,
                startPlayer,
                endPlayer = _player,
                enemiesBeforeCleanup = _enemiesBeforeCleanup,
                kills = waveKills,
                clearedElapsedSeconds = clearedAt,
                clearDurationSeconds = clearedAt.HasValue ? clearedAt.Value - waveStarted : (double?)null,
                rewardsApplied = _status == "completed",
                bonusPaid = _bonus,
                healthRestored = _healthRestored,
                summaries
            });
            session.TryRecord("wave_summary", summary);
            foreach (var entry in collectors)
            {
                if (!(entry.Value is ILogWaveSummaryReceiver receiver))
                {
                    continue;
                }
                try
                {
                    receiver.SummaryClosed(boundary, summary);
                }
                catch (Exception exception)
                {
                    session.ReportCollectionFailure(exception);
                }
            }
        }
        finally
        {
            closingWave = false;
        }
        return true;
    }

    private LogWaveBoundary Boundary(string _status, string _reason)
    {
        return new LogWaveBoundary(waveNumber, waveStarted, session.ElapsedSeconds, _status, _reason);
    }

    private static string Known(string _value)
    {
        return string.IsNullOrWhiteSpace(_value) ? null : _value;
    }
}
