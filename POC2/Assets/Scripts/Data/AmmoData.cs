using System;

[Serializable]
public sealed class AmmoData
{
    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string name;
        public string color;
        public int referenceMagazineSize;
        public int maxReserve;
        public int startingReserve;
    }

    public string status;
    [Serializable]
    public sealed class PickupSettings
    {
        public float dropChance;
        public float pickupDistance;
        public float magazineFraction;
    }

    public PickupSettings pickup;
    public Entry[] ammoTypes = Array.Empty<Entry>();
}
