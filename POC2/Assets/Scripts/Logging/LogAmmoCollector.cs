using System;
using System.Collections.Generic;
using System.Linq;

public sealed class LogAmmoWeaponSnapshot
{
    public string instanceId;
    public string weaponTypeId;
    public string ammoTypeId;
    public string location;
    public string hand;
    public int? slot;
    public int? loadedAmmo;
}

public sealed class LogAmmoSnapshot
{
    public LogReserveSnapshot[] reserves;
    public LogAmmoWeaponSnapshot[] weapons;
}

public sealed class LogAmmoChange
{
    public string transactionId;
    public string causeId;
    public string source;
    public string ammoTypeId;
    public string weaponInstanceId;
    public string hand;
    public string shotId;
    public int reserveDelta;
    public int loadedDelta;
    public int acquired;
    public int consumed;
    public int discarded;
    public int transferred;
    public int moved;
    public int? reserveMaximumBefore;
    public int? reserveMaximumAfter;
}

public sealed class LogAmmoTotals
{
    public string ammoTypeId;
    public long consumed;
    public long discarded;
    public long reloaded;
    public long moved;
    public int emptyFireAttempts;
    public int emptyReloadAttempts;
    public Dictionary<string, long> acquiredBySource = new Dictionary<string, long>();
}

public sealed class LogAmmoCollector : ILogWaveSummaryCollector
{
    private readonly LogGameLifecycle lifecycle;
    private readonly Func<LogAmmoSnapshot> capture;
    private Dictionary<string, LogAmmoTotals> totals;
    private object start;
    private bool open;
    private bool emptyHoldCounted;

    public string Key => "ammo";

    public LogAmmoCollector(LogGameLifecycle _lifecycle, Func<LogAmmoSnapshot> _capture)
    {
        lifecycle = _lifecycle ?? throw new ArgumentNullException(nameof(_lifecycle));
        capture = _capture ?? throw new ArgumentNullException(nameof(_capture));
    }

    public void BeginWave(LogWaveBoundary _boundary)
    {
        open = false;
        totals = new Dictionary<string, LogAmmoTotals>(StringComparer.Ordinal);
        LogAmmoSnapshot snapshot = capture();
        start = LogJson.CopyPayload(snapshot);
        if (snapshot?.reserves != null)
        {
            foreach (LogReserveSnapshot reserve in snapshot.reserves)
            {
                if (!string.IsNullOrWhiteSpace(reserve?.ammoTypeId))
                {
                    Totals(reserve.ammoTypeId);
                }
            }
        }
        open = true;
    }

    public object CloseWave(LogWaveBoundary _boundary)
    {
        open = false;
        return new
        {
            start,
            end = LogJson.CopyPayload(capture()),
            ammoTypes = totals.Values.OrderBy(_value => _value.ammoTypeId).ToArray()
        };
    }

    public void Change(LogAmmoChange _change)
    {
        Observe(() =>
        {
            if (_change == null)
            {
                return;
            }
            _change.transactionId = lifecycle.Session.NewId(LogIdKind.AmmoTransaction);
            _change.source = string.IsNullOrWhiteSpace(_change.source) ? "unknown" : _change.source;
            _change.ammoTypeId = string.IsNullOrWhiteSpace(_change.ammoTypeId) ? "unknown" : _change.ammoTypeId;
            LogJson.CopyPayload(_change);
            if (open && lifecycle.IsWaveOpen)
            {
                LogAmmoTotals value = Totals(_change.ammoTypeId);
                value.consumed += _change.consumed;
                value.discarded += _change.discarded;
                value.reloaded += _change.transferred;
                value.moved += _change.moved;
                if (_change.acquired > 0 || IsAcquisition(_change.source))
                {
                    value.acquiredBySource.TryGetValue(_change.source, out long previous);
                    value.acquiredBySource[_change.source] = previous + _change.acquired;
                }
            }
            lifecycle.Session.TryRecord("ammo_changed", _change);
        });
    }

    public void Shot(LogShotContext _shot, string _ammoId, int _before, int _after)
    {
        if (_shot == null)
        {
            return;
        }
        int spent = Math.Max(0, _before - _after);
        Change(new LogAmmoChange { source = "shot", ammoTypeId = _ammoId, shotId = _shot.ShotId,
            causeId = _shot.ShotId, weaponInstanceId = _shot.WeaponInstanceId, hand = _shot.Hand,
            loadedDelta = -spent, consumed = spent });
    }

    public void Weapon(string _source, object _weapon, string _ammoId, int _amount, string _hand = null, string _causeId = null)
    {
        Change(new LogAmmoChange { source = _source, ammoTypeId = _ammoId,
            weaponInstanceId = lifecycle.Session.GetWeaponId(_weapon), hand = _hand, causeId = _causeId,
            loadedDelta = IsAcquisition(_source) ? _amount : 0,
            acquired = IsAcquisition(_source) ? Math.Max(0, _amount) : 0,
            discarded = IsAcquisition(_source) ? Math.Max(0, -_amount) : 0,
            moved = _source == "weapon_drop" || _source == "weapon_pickup" ? _amount : 0 });
    }

    public void FireInput(bool _pressed)
    {
        if (!_pressed)
        {
            emptyHoldCounted = false;
        }
    }

    public void Reload(object _weapon, string _ammoId, int _hand, int _amount)
    {
        Change(new LogAmmoChange { source = "reload", ammoTypeId = _ammoId,
            weaponInstanceId = lifecycle.Session.GetWeaponId(_weapon), hand = _hand == 0 ? "right" : "left",
            reserveDelta = -_amount, loadedDelta = _amount, transferred = _amount });
    }

    public void Shrink(object _weapon, string _ammoId, int _removed, int _returned, string _causeId)
    {
        Change(new LogAmmoChange { source = "capacity_shrink", ammoTypeId = _ammoId, causeId = _causeId,
            weaponInstanceId = lifecycle.Session.GetWeaponId(_weapon), loadedDelta = -_removed,
            discarded = _removed - _returned });
    }

    public string NewCauseId()
    {
        return lifecycle.IsActive ? lifecycle.Session.NewId(LogIdKind.AmmoTransaction) : null;
    }

    public void EmptyFire(string _ammoId, bool _automatic, bool _eligible)
    {
        if (!_eligible || (_automatic && emptyHoldCounted))
        {
            return;
        }
        emptyHoldCounted = true;
        Shortage(_ammoId, "fire");
    }

    public void Shortage(string _ammoId, string _kind)
    {
        Observe(() =>
        {
            if (!open || !lifecycle.IsWaveOpen)
            {
                return;
            }
            LogAmmoTotals value = Totals(_ammoId ?? "unknown");
            if (_kind == "fire")
            {
                value.emptyFireAttempts++;
            }
            else if (_kind == "reload")
            {
                value.emptyReloadAttempts++;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(_kind));
            }
            lifecycle.Session.TryRecord("ammo_shortage", new { ammoTypeId = value.ammoTypeId, kind = _kind });
        });
    }

    private LogAmmoTotals Totals(string _id)
    {
        if (!totals.TryGetValue(_id, out LogAmmoTotals value))
        {
            value = new LogAmmoTotals { ammoTypeId = _id };
            totals.Add(_id, value);
        }
        return value;
    }

    private static bool IsAcquisition(string _source)
    {
        return _source == "drop" || _source == "shop_pack" || _source == "slot_reserve" ||
            _source == "slot_loaded" || _source == "new_weapon" || _source == "initial_weapon" ||
            _source == "world_weapon" || _source == "unknown";
    }

    private void Observe(Action _action)
    {
        if (!lifecycle.IsActive || !lifecycle.Session.IsRecording)
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
