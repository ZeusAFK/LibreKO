using Godot;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginMapBridge(IWorldContext context, VanguardFeature vanguard, SelfAvatar avatar, MapFeature map, NpcFinderFeature npcFinder, ScenerySystem scenery, BattleEventSession battleEvent) : IGameMap
{
    private float heading;

    private string mapStem = "";

    private Texture2D? mapTexture;

    event Action? IGameMap.Changed { add { } remove { } }

    public void RaiseMap(float headingDegrees)
    {
        heading = headingDegrees;
        PluginHost.Game.RaiseMap();
    }

    public int Zone => context.Zone;

    public IReadOnlyList<byte> MonumentOwners => battleEvent.MonumentOwners;

    public bool WantedUiOn => vanguard.UiOn;

    void IGameMap.ToggleWantedUi() => vanguard.ToggleUi();

    void IGameMap.ShowWantedList() => vanguard.ShowWantedList();

    public string ZoneName => ZoneCatalog.Name(context.Zone);

    public float X => avatar.MapX;

    public float Z => avatar.MapZ;

    public float HeadingDegrees => heading;

    public float WorldExtent => map.MiniMap?.WorldExtent ?? 0f;

    public IReadOnlyList<MiniMap.Blip> Blips => map.BlipScratch;

    public int NpcKind => npcFinder.Kind;

    public bool NpcKindPresent(int kind) => npcFinder.KindPresent(kind);

    public void PickNpcKind(int kind) => npcFinder.PickNpcKind(kind);

    public Texture2D? MapTexture
    {
        get
        {
            string stem = scenery.Terrain?.ZoneStem ?? ZoneCatalog.Stem(context.Zone) ?? "";
            if (stem != mapStem)
            {
                mapStem = stem;
                mapTexture = stem.Length > 0 ? MiniMap.LoadBaked(stem) : null;
            }
            return mapTexture;
        }
    }
}
