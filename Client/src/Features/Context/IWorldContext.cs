using Godot;

namespace LibreKO.Features;

internal interface IWorldContext
{
    Node3D Root { get; }

    int Zone { get; }

    bool Ready { get; }

    bool Loading { get; }

    double Now { get; }

    ChatSystem Chat { get; }

    FloaterSystem Floaters { get; }

    ICombatLog Log { get; }

    IItemTooltips Tooltips { get; }

    INpcDialog NpcDialog { get; }

    IMainWindows MainWindows { get; }

    IBagCompanions BagCompanions { get; }

    IInventoryView Inventory { get; }

    ISelfView Self { get; }

    IEntityAccess Entities { get; }

    ISelection Selection { get; }

    IHudLayout Hud { get; }

    IPluginNotices Plugins { get; }

    ITerrainQueries Terrain { get; }

    bool Alive { get; }

    IMovement Movement { get; }

    Camera3D? Camera { get; }
}
