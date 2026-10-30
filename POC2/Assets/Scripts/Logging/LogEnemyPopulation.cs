using System;
using System.Collections.Generic;

public sealed class LogEnemyPopulation
{
    private readonly HashSet<string> kinds = new HashSet<string>(StringComparer.Ordinal);

    public void Reset(IEnumerable<string> _plannedKinds)
    {
        kinds.Clear();
        if (_plannedKinds == null)
        {
            return;
        }
        foreach (string kind in _plannedKinds)
        {
            ObserveKind(kind);
        }
    }

    public void ObserveKind(string _kind)
    {
        kinds.Add(string.IsNullOrWhiteSpace(_kind) ? "unknown" : _kind);
    }

    public LogEnemySnapshot CreateSnapshot(IEnumerable<string> _plannedKinds, double? _radius, bool _hasCenter)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string kind in kinds)
        {
            counts[kind] = 0;
        }
        if (_plannedKinds != null)
        {
            foreach (string kind in _plannedKinds)
            {
                counts[string.IsNullOrWhiteSpace(kind) ? "unknown" : kind] = 0;
            }
        }
        return new LogEnemySnapshot
        {
            aliveByKind = counts,
            radius = _radius,
            nearbyAliveCount = _radius.HasValue && _hasCenter ? 0 : (int?)null
        };
    }

    public void AddAlive(LogEnemySnapshot _snapshot, string _kind, bool? _nearby)
    {
        string kind = string.IsNullOrWhiteSpace(_kind) ? "unknown" : _kind;
        ObserveKind(kind);
        _snapshot.aliveByKind.TryGetValue(kind, out int count);
        _snapshot.aliveByKind[kind] = count + 1;
        _snapshot.aliveCount++;
        if (_snapshot.nearbyAliveCount.HasValue && _nearby == true)
        {
            _snapshot.nearbyAliveCount++;
        }
    }
}
