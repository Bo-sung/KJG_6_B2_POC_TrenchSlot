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
    public Entry[] ammoTypes = Array.Empty<Entry>();
}
