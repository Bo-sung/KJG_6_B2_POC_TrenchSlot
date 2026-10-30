using System;

public sealed class LogGameplayEvents
{
    private const string CATEGORY_WAVE = "wave";
    private const string CATEGORY_SLOT = "slot";
    private const string CATEGORY_WEAPON = "weapon";
    private readonly LogGameLifecycle lifecycle;

    public LogGameplayEvents(LogGameLifecycle _lifecycle)
    {
        lifecycle = _lifecycle ?? throw new ArgumentNullException(nameof(_lifecycle));
    }

    public void IntermissionStarted(float _seconds, int _balance)
    {
        if (!lifecycle.IsActive || !lifecycle.Session.IsRecording)
        {
            return;
        }
        Write(CATEGORY_WAVE, "IntermissionStarted", new IntermissionStartedEvent
        {
            intermissionSeconds = _seconds,
            balance = _balance
        });
    }


    public void SlotSpinRequested(int _cost, int _balance)
    {
        WriteSlot("SpinRequested", _cost, _balance, "");
    }

    public void SlotSpinRejected(string _reason, int _cost, int _balance)
    {
        WriteSlot("SpinRejected", _cost, _balance, _reason);
    }

    private void WriteSlot(string _event, int _cost, int _balance, string _reason)
    {
        if (!lifecycle.IsActive || !lifecycle.Session.IsRecording)
        {
            return;
        }
        Write(CATEGORY_SLOT, _event, new SlotSpinEvent
        {
            cost = _cost,
            balance = _balance,
            reason = _reason ?? ""
        });
    }


    public void WeaponDropped(string _weaponId, int _magazine, string _reason)
    {
        WriteWeapon("WeaponDropped", _weaponId, -1, "", _magazine, _reason);
    }

    public void WeaponAcquired(string _event, string _weaponId, int _slot, string _replacedWeaponId, int _magazine)
    {
        WriteWeapon(_event, _weaponId, _slot, _replacedWeaponId, _magazine, "");
    }

    private void WriteWeapon(string _event, string _weaponId, int _slot, string _replacedWeaponId, int _magazine, string _reason)
    {
        if (!lifecycle.IsActive || !lifecycle.Session.IsRecording)
        {
            return;
        }
        Write(CATEGORY_WEAPON, _event, new WeaponChangeEvent
        {
            weaponId = _weaponId ?? "",
            slot = _slot,
            replacedWeaponId = _replacedWeaponId ?? "",
            magazine = _magazine,
            reason = _reason ?? ""
        });
    }


    private void Write(string _category, string _event, object _data)
    {
        if (!lifecycle.IsActive || !lifecycle.Session.IsRecording)
        {
            return;
        }
        try
        {
            string name;
            switch (_event)
            {
                case "IntermissionStarted": name = "intermission_started"; break;
                case "SpinRequested": name = "spin_requested"; break;
                case "SpinRejected": name = "spin_rejected"; break;
                case "WeaponDropped": name = "dropped"; break;
                case "WeaponBought": name = "bought"; break;
                case "WeaponPickedUp": name = "picked_up"; break;
                default: throw new ArgumentOutOfRangeException(nameof(_event));
            }
            lifecycle.Session.TryRecord(_category + "_" + name, _data);
        }
        catch (Exception exception)
        {
            lifecycle.Session.ReportCollectionFailure(exception);
        }
    }
}
