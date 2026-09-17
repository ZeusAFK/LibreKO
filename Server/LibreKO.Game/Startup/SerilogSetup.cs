using LibreKO.Common.Infrastructure.Logging;

namespace LibreKO.Game.Startup;

public static class SerilogSetup
{
    private static readonly LogFileRoute[] Routes =
    [
        new(
            "startup.log",
            "LibreKO.Game.Startup",
            "LibreKO.Common.Infrastructure.Persistence.Seed",
            "LibreKO.Common.Infrastructure.Persistence.GameDataService",
            "LibreKO.Game.World.MapManager",
            "Microsoft.Hosting.Lifetime"),
        new("network.log", "LibreKO.Common.Infrastructure.Network"),
        new("packets.log", "LibreKO.Game.GamePacketHandler"),
        new(
            "player.log",
            "LibreKO.Game.World.GameSessionInitializer",
            "LibreKO.Game.World.SessionManager",
            "LibreKO.Game.World.SessionTerminationService",
            "LibreKO.Game.World.UserSession",
            "LibreKO.Game.Protocol.PreGame",
            "LibreKO.Game.Protocol.Character",
            "LibreKO.Game.Protocol.Admin",
            "LibreKO.Game.Protocol.Misc",
            "LibreKO.Game.Commands"),
        new(
            "npc.log",
            "LibreKO.Game.World.NpcAiBehaviorService",
            "LibreKO.Game.World.NpcAiMovementService",
            "LibreKO.Game.World.NpcAiTargetingService",
            "LibreKO.Game.World.NpcInstance",
            "LibreKO.Game.World.NpcWorldFilter",
            "LibreKO.Game.Protocol.NpcPacketMapper"),
        new(
            "combat.log",
            "LibreKO.Game.Protocol.Combat",
            "LibreKO.Game.Protocol.Magic",
            "LibreKO.Game.World.NpcAiCombatService",
            "LibreKO.Game.World.NpcAiMagicService"),
        new(
            "items.log",
            "LibreKO.Game.Protocol.Item",
            "LibreKO.Game.Protocol.Loot",
            "LibreKO.Game.Protocol.Merchant",
            "LibreKO.Game.Protocol.Warehouse",
            "LibreKO.Game.Protocol.Exchange",
            "LibreKO.Game.Protocol.ShoppingMall"),
        new(
            "social.log",
            "LibreKO.Game.Protocol.Chat",
            "LibreKO.Game.Protocol.Party",
            "LibreKO.Game.Protocol.Social",
            "LibreKO.Game.Protocol.Challenge"),
        new(
            "world.log",
            "LibreKO.Game.Protocol.World",
            "LibreKO.Game.World.ZoneTransitionService",
            "LibreKO.Game.World.RegionManager",
            "LibreKO.Game.World.BattleZoneManager"),
        new(
            "systems.log",
            "LibreKO.Game.Protocol.EventSystems",
            "LibreKO.Game.World.EventSchedulerService",
            "LibreKO.Game.World.NpcRespawnService",
            "LibreKO.Game.World.NpcAiService",
            "LibreKO.Game.World.BuffExpiryService",
            "LibreKO.Game.World.HpMpRegenService",
            "LibreKO.Game.World.AutoSaveService",
            "LibreKO.Game.World.KingElectionTimerService",
            "LibreKO.Game.World.MonthlyLoyaltyResetService"),
        new(
            "knights.log",
            "LibreKO.Game.Protocol.Knights",
            "LibreKO.Game.Protocol.King",
            "LibreKO.Game.Protocol.NationSystems",
            "LibreKO.Game.World.KnightsManager",
            "LibreKO.Game.World.KingEventState"),
        new(
            "quests.log",
            "LibreKO.Game.Protocol.Quest",
            "LibreKO.Game.Scripting")
    ];

    private static readonly string[] ConsoleSourcePrefixes =
    [
        "LibreKO.Game.Startup",
        "LibreKO.Common.Infrastructure.Persistence.Seed",
        "LibreKO.Common.Infrastructure.Persistence.GameDataService",
        "LibreKO.Game.World.MapManager",
        "LibreKO.Common.Infrastructure.Network.SocketServer",
        "Microsoft.Hosting.Lifetime"
    ];

    public static Serilog.ILogger CreateLogger(
        string baseDir,
        string? fileLevelName = null,
        string? consoleLevelName = null,
        int retentionDays = 10,
        ErrorTrackingOptions? errorTracking = null)
    {
        return SerilogHostLogging.CreateLogger(
            baseDir,
            Routes,
            ConsoleSourcePrefixes,
            fileLevelName,
            consoleLevelName,
            retentionDays,
            errorTracking);
    }
}
