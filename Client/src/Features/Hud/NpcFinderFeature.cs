using FileAccess = Godot.FileAccess;

namespace LibreKO.Features.Hud;

internal sealed class NpcFinderFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly MapFeature map;

    public NpcFinderFeature(IWorldContext context, SelfSession self, MapFeature map)
    {
        this.context = context;
        this.self = self;
        this.map = map;
    }

    public void Dispose()
    {
    }

    private const float BlipRadius = 4.5f;
    private static List<NpcSpot>? spots;

    internal int Kind { get; set; } = NpcFinder.NoKind;

    internal static IReadOnlyList<NpcSpot> Spots => spots ??= LoadSpots();

    private static List<NpcSpot> LoadSpots()
    {
        if (!FileAccess.FileExists(NpcFinder.ResourcePath)) return new List<NpcSpot>();
        using var file = FileAccess.Open(NpcFinder.ResourcePath, FileAccess.ModeFlags.Read);
        return file == null ? new List<NpcSpot>() : NpcFinder.Parse(file.GetAsText());
    }

    public void Build()
    {
        if (map.MiniMap != null) map.MiniMap.NpcKindPicked += PickNpcKind;
        map.AddMiniSource(AddBlips);
        RefreshKinds();
    }

    internal void PickNpcKind(int kind)
    {
        this.Kind = kind == this.Kind ? NpcFinder.NoKind : kind;
        RefreshKinds();
    }

    internal bool KindPresent(int kind) =>
        kind >= 1 && kind <= NpcFinder.KindCount
        && NpcFinder.KindsPresent(Spots, context.Zone, self.LastEnter.Nation)[kind - 1];

    private void RefreshKinds()
    {
        map.MiniMap?.SetNpcKinds(NpcFinder.KindsPresent(Spots, context.Zone, self.LastEnter.Nation), Kind);
        map.UpdateMini();
    }

    internal void AddBlips(List<MiniMap.Blip> blips)
    {
        if (Kind == NpcFinder.NoKind) return;
        foreach (var spot in NpcFinder.Spots(Spots, context.Zone, self.LastEnter.Nation, Kind))
            blips.Add(new MiniMap.Blip(spot.X, spot.Z, UiTheme.GoldVivid, BlipRadius, false, Diamond: true));
    }
}
