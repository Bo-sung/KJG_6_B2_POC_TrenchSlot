public sealed class LogPosition3
{
    public double? x;
    public double? y;
    public double? z;
}

public sealed class LogWeaponSnapshot
{
    public string instanceId;
    public string weaponTypeId;
    public string ammoTypeId;
    public string hand;
    public int? slot;
    public bool? active;
    public int? loadedAmmo;
    public string[] partIds;
}

public sealed class LogReserveSnapshot
{
    public string ammoTypeId;
    public int? reserve;
    public int? maximum;
}

public sealed class LogPlayerSnapshot
{
    public double? health;
    public int? money;
    public string activeHand;
    public int? activeSlot;
    public LogPosition3 position;
    public LogWeaponSnapshot[] weapons;
    public LogReserveSnapshot[] reserves;
}

public sealed class LogSummaryReference
{
    public string summaryId;
    public int waveNumber;
    public int revision;
    public string collectionStatus;
}
