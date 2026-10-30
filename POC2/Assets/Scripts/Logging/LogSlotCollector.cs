using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

public sealed class LogSlotState
{
    public int? waveNumber;
    public string phase;
    public double elapsedSeconds;
    public LogPlayerSnapshot player;
    public LogEnemySnapshot enemies;
}

public sealed class LogSlotOutcome
{
    public int tripleFace;
    public int pairFace;
    public int multiplier;
    public bool hasAmmo;
    public int partCount;
    public int nextEraPartCount;
    public bool isMiss;
    public bool isNearMiss;
}

public sealed class LogSpinContext
{
    public string SpinId { get; }
    public int? OriginWaveNumber { get; }
    public string OriginPhase { get; }
    public double StartedElapsedSeconds { get; }
    internal readonly LogSlotCollector owner;
    internal object machine;
    internal readonly JToken faces;
    internal readonly JToken symbols;
    internal readonly List<JToken> ammo = new List<JToken>();
    internal bool closed;
    internal bool healed;

    internal LogSpinContext(LogSlotCollector _owner, object _machine, string _id, LogSlotState _before,
        JToken _faces, JToken _symbols)
    {
        owner = _owner;
        machine = _machine;
        SpinId = _id;
        OriginWaveNumber = _before?.waveNumber;
        OriginPhase = _before?.phase;
        StartedElapsedSeconds = _before.elapsedSeconds;
        faces = _faces;
        symbols = _symbols;
    }
}

public sealed class LogSlotCollector : ILogSessionEndObserver
{
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public new bool Equals(object _left, object _right) => ReferenceEquals(_left, _right);
        public int GetHashCode(object _value) => RuntimeHelpers.GetHashCode(_value);
    }

    private readonly LogGameLifecycle lifecycle;
    private readonly Dictionary<object, LogSpinContext> pending = new Dictionary<object, LogSpinContext>(new ReferenceComparer());

    public int PendingCount => pending.Count;

    public LogSlotCollector(LogGameLifecycle _lifecycle)
    {
        lifecycle = _lifecycle ?? throw new ArgumentNullException(nameof(_lifecycle));
        lifecycle.RegisterSessionEndObserver(this);
    }

    public LogSpinContext Start(object _machine, int _actualCost, int _baseCost, double _seconds,
        int[] _faces, string[] _symbolIds, LogSlotState _before)
    {
        if (!Active || _machine == null || pending.ContainsKey(_machine))
        {
            return null;
        }
        LogSpinContext context = null;
        Observe(() =>
        {
            if (_faces == null || _faces.Length != 3 || _symbolIds == null || _symbolIds.Length != 3 || _before == null)
            {
                throw new ArgumentException("A successful spin requires its original three faces, symbols and start state.");
            }
            if (_actualCost < 0 || _baseCost < 0 || double.IsNaN(_seconds) || double.IsInfinity(_seconds))
            {
                throw new ArgumentOutOfRangeException(nameof(_actualCost));
            }
            JToken before = LogJson.CopyPayload(_before);
            JToken faces = LogJson.CopyPayload(_faces);
            JToken symbols = LogJson.CopyPayload(_symbolIds);
            var candidate = new LogSpinContext(this, _machine, lifecycle.Session.NewId(LogIdKind.Spin), _before, faces, symbols);
            if (lifecycle.Session.TryRecord("slot_spin_started", new
            {
                spinId = candidate.SpinId,
                originWaveNumber = candidate.OriginWaveNumber,
                originPhase = candidate.OriginPhase,
                startedElapsedSeconds = candidate.StartedElapsedSeconds,
                actualCost = _actualCost,
                baseCost = _baseCost,
                spinSeconds = _seconds,
                faces,
                symbolIds = symbols,
                before
            }))
            {
                context = candidate;
                pending.Add(_machine, context);
            }
        });
        return context;
    }

    public void Healed(LogSpinContext _spin, double _actual, double? _healthAfter)
    {
        Observe(() =>
        {
            if (!Owns(_spin) || _spin.healed)
            {
                return;
            }
            _spin.healed = true;
            lifecycle.Session.TryRecord("slot_spin_healed", new
            {
                spinId = _spin.SpinId, actualHealthRestored = _actual, healthAfter = _healthAfter
            });
        });
    }

    public void ObserveAmmo(LogAmmoChange _change)
    {
        Observe(() =>
        {
            if (_change?.causeId == null)
            {
                return;
            }
            foreach (LogSpinContext spin in pending.Values)
            {
                if (spin.SpinId == _change.causeId)
                {
                    spin.ammo.Add(LogJson.CopyPayload(_change));
                    break;
                }
            }
        });
    }

    public bool Resolve(LogSpinContext _spin, LogSlotState _before, LogSlotState _after,
        LogSlotOutcome _outcome, double _piggyMultiplier, double? _cashBonusMultiplier, int _calculatedPayout,
        string[] _plannedPartIds = null)
    {
        if (!Owns(_spin))
        {
            return false;
        }
        bool recorded = false;
        try
        {
            Observe(() =>
            {
                if (_outcome == null)
                {
                    throw new ArgumentNullException(nameof(_outcome));
                }
                recorded = lifecycle.Session.TryRecord("slot_spin_resolved", new
                {
                    spinId = _spin.SpinId,
                    originWaveNumber = _spin.OriginWaveNumber,
                    originPhase = _spin.OriginPhase,
                    startedElapsedSeconds = _spin.StartedElapsedSeconds,
                    resolvedElapsedSeconds = lifecycle.Session.ElapsedSeconds,
                    faces = _spin.faces,
                    symbolIds = _spin.symbols,
                    outcome = _outcome,
                    piggyMultiplier = _piggyMultiplier,
                    cashBonusMultiplier = _cashBonusMultiplier,
                    calculatedPayout = _calculatedPayout,
                    plannedPartIds = _plannedPartIds,
                    actualMoneyDelta = _after?.player?.money.HasValue == true && _before?.player?.money.HasValue == true
                        ? (long?)_after.player.money.Value - _before.player.money.Value : null,
                    balanceAfter = _after?.player?.money,
                    ammoGranted = _spin.ammo,
                    before = _before,
                    after = _after
                });
            });
        }
        finally
        {
            if (!recorded && Active)
            {
                Incomplete(_spin, "result_collection_failed");
            }
            else
            {
                Close(_spin);
            }
        }
        return recorded;
    }

    public void SessionEnding(string _reason)
    {
        foreach (LogSpinContext spin in new List<LogSpinContext>(pending.Values))
        {
            Incomplete(spin, _reason);
        }
    }

    public void Incomplete(LogSpinContext _spin, string _reason)
    {
        if (!Owns(_spin))
        {
            return;
        }
        try
        {
            Observe(() => lifecycle.Session.TryRecord("slot_spin_incomplete", new
            {
                spinId = _spin.SpinId, originWaveNumber = _spin.OriginWaveNumber, originPhase = _spin.OriginPhase,
                startedElapsedSeconds = _spin.StartedElapsedSeconds, status = "incomplete", reason = _reason
            }));
        }
        finally
        {
            Close(_spin);
        }
    }

    private bool Active => lifecycle.IsActive && lifecycle.Session.IsRecording;

    private bool Owns(LogSpinContext _spin)
    {
        return _spin != null && ReferenceEquals(_spin.owner, this) && !_spin.closed &&
            _spin.machine != null && pending.TryGetValue(_spin.machine, out LogSpinContext current) && ReferenceEquals(current, _spin);
    }

    private void Close(LogSpinContext _spin)
    {
        if (_spin.closed)
        {
            return;
        }
        pending.Remove(_spin.machine);
        _spin.machine = null;
        _spin.ammo.Clear();
        _spin.closed = true;
    }

    private void Observe(Action _action)
    {
        if (!Active)
        {
            return;
        }
        try
        {
            _action();
        }
        catch (Exception exception)
        {
            lifecycle.Session.ReportCollectionFailure(exception);
        }
    }
}
