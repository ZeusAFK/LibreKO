namespace LibreKO.Domain;

public static class WarpData
{
    public readonly struct Destination
    {
        public readonly string Name;
        public readonly string Description;
        public readonly string Image;
        public readonly int MinLevel;
        public readonly int MaxLevel;

        public Destination(string name, string description, string image, int minLevel, int maxLevel)
        {
            Name = name; Description = description; Image = image;
            MinLevel = minLevel; MaxLevel = maxLevel;
        }
    }

    private static readonly Dictionary<int, Destination> destinations = new();
    private static bool loaded;

    internal static Action? Loader { get; set; }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        Loader?.Invoke();
    }

    internal static void Add(int warpId, Destination destination) => destinations[warpId] = destination;

    public static bool TryGet(int warpId, out Destination destination)
    {
        EnsureLoaded();
        return destinations.TryGetValue(warpId, out destination);
    }
}
