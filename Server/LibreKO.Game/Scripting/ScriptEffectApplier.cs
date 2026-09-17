using LibreKO.Common.Domain.Services;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Scripting;

public interface IScriptEffectApplier
{
    Task ApplyAsync(UserSession session, QuestScriptContext context, string scriptName);
}

public sealed class ScriptEffectApplier(
    IGameDataService gameData,
    ICharacterStatePersister statePersister,
    IServiceProvider serviceProvider,
    ILogger<ScriptEffectApplier> logger) : IScriptEffectApplier
{
    public async Task ApplyAsync(UserSession session, QuestScriptContext context, string scriptName)
    {
        foreach (var packet in context.QueuedPackets)
            await session.Client.SendPacket(packet);

        if (context.PendingExperience != 0)
            await serviceProvider.GetRequiredService<IPlayerProgressionService>()
                .ChangeExperienceAsync(session, context.PendingExperience);

        if (context.QuestStateDirty)
        {
            _ = statePersister.SaveQuestStateAsync(session);
            await serviceProvider.GetRequiredService<Protocol.IAchievementProgressService>().RefreshAsync(session);
        }

        if (context.ClassChanged)
            _ = statePersister.SaveAsync(session);

        await ApplyPendingClanAsync(context);
        await ApplyPendingAchievementsAsync(session, context);
        await ApplyPendingTempleEventJoinAsync(session, context);
        await ApplyPendingLevelAsync(session, context, scriptName);

        await ApplyPendingNpcDespawnAsync(session, context, scriptName);
        await ApplyPendingZoneChangeAsync(session, context, scriptName);

        if (context.ActionFailed && context.FailureReason is { } reason)
        {
            logger.LogInformation(
                "Script {Script} action failed for {Name}: {Reason} (quest state left unchanged)",
                scriptName, session.Name, reason);
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, reason));
        }
    }

    private async Task ApplyPendingClanAsync(QuestScriptContext context)
    {
        if (context.ActionFailed || (context.PromotedClanId <= 0 && context.PremiumClanId <= 0))
            return;

        var sessions = serviceProvider.GetRequiredService<SessionManager>();
        if (context.PromotedClanId > 0)
            await ClanScriptEffects.ApplyPromotionAsync(sessions, serviceProvider, logger, context.PromotedClanId);
        if (context.PremiumClanId > 0)
            await ClanScriptEffects.ApplyPremiumAsync(sessions, serviceProvider, logger, context.PremiumClanId);
    }

    private async Task ApplyPendingAchievementsAsync(UserSession session, QuestScriptContext context)
    {
        if (context.PendingAchievements.Count == 0 || context.ActionFailed)
            return;

        var achievements = serviceProvider.GetRequiredService<Protocol.IAchievementProgressService>();
        foreach (var achievementId in context.PendingAchievements)
        {
            if (gameData.AchievementTable.TryGetValue(achievementId, out var definition))
                await achievements.CompleteAsync(session, definition);
            else
                logger.LogWarning("Script awarded unknown achievement {Id} to {Name}", achievementId, session.Name);
        }
    }

    private async Task ApplyPendingTempleEventJoinAsync(UserSession session, QuestScriptContext context)
    {
        if (!context.PendingTempleEventJoin || context.ActionFailed)
            return;

        await serviceProvider.GetRequiredService<Protocol.IEventSystemsPacketCoordinator>()
            .JoinTempleEventAsync(session);
    }

    private async Task ApplyPendingLevelAsync(
        UserSession session, QuestScriptContext context, string scriptName)
    {
        if (context.PendingLevel <= 0 || context.ActionFailed)
            return;

        if (context.PendingLevel > byte.MaxValue)
        {
            logger.LogWarning("Script {Script}: level {Level} is out of range for {Name}",
                scriptName, context.PendingLevel, session.Name);
            return;
        }

        await serviceProvider.GetRequiredService<IPlayerProgressionService>()
            .SetLevelAsync(session, (byte)context.PendingLevel);
    }

    private async Task ApplyPendingNpcDespawnAsync(
        UserSession session, QuestScriptContext context, string scriptName)
    {
        if (!context.DespawnEventNpc)
            return;

        if (context.ActionFailed)
        {
            logger.LogInformation(
                "Script {Script}: despawn skipped for {Name} — the script's action failed",
                scriptName, session.Name);
            return;
        }

        if (context.EventNpc is not { } npc)
        {
            logger.LogWarning(
                "Script {Script}: nothing to despawn for {Name} — no NPC in this interaction",
                scriptName, session.Name);
            return;
        }

        if (npc.IsDead)
            return;

        await serviceProvider.GetRequiredService<INpcLifecycleService>().DespawnAsync(npc);
    }

    private async Task ApplyPendingZoneChangeAsync(UserSession session, QuestScriptContext context, string scriptName)
    {
        if (context.PendingZoneChange is not { } warp)
            return;

        if (context.ActionFailed)
        {
            logger.LogInformation(
                "Script {Script}: zone change to {Zone} skipped for {Name} — the script's action failed",
                scriptName, warp.ZoneId, session.Name);
            return;
        }

        if (warp.ZoneId is <= 0 or > byte.MaxValue
            || !gameData.ZoneInfoTable.ContainsKey((short)warp.ZoneId))
        {
            logger.LogWarning(
                "Script {Script}: zone {Zone} is not on this server, ignoring the zone change for {Name}",
                scriptName, warp.ZoneId, session.Name);
            return;
        }

        if (warp.ZoneId == session.ZoneId)
        {
            var (x, z) = ResolveWarpTarget(session, warp);
            await serviceProvider.GetRequiredService<Protocol.IWorldPacketCoordinator>()
                .WarpAsync(session, (ushort)(x * 10f), (ushort)(z * 10f));
            return;
        }

        await serviceProvider.GetRequiredService<IZoneTransitionService>()
            .ChangeZoneAsync(session, (byte)warp.ZoneId, warp.X, warp.Z);
    }

    private (float X, float Z) ResolveWarpTarget(UserSession session, (int ZoneId, float X, float Z) warp)
    {
        if (warp.X != 0f || warp.Z != 0f)
            return (warp.X, warp.Z);

        var start = gameData.GetStartPosition((short)warp.ZoneId);
        if (start == null)
            return (session.X, session.Z);

        var x = (float)start.BaseX(session.Nation);
        var z = (float)start.BaseZ(session.Nation);
        return x == 0f && z == 0f ? (session.X, session.Z) : (x, z);
    }
}
