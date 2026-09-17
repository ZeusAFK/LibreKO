using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IAchievementPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    bool HasUnlockedTitle(UserSession session, int titleId);
}

public class AchievementPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IAchievementProgressService achievementProgressService,
    IWorldPacketCoordinator worldPacketCoordinator,
    ILogger<AchievementPacketCoordinator> logger) : IAchievementPacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = (AchievementSubOpcode)packet.ReadByte();
        switch (sub)
        {
            case AchievementSubOpcode.List:
                var requested = ReadRequestedIds(packet);
                logger.LogDebug("ACHIEVE list request from {Name}: {Count} row(s) [{Ids}]",
                    session.Name,
                    requested is null ? "unpaged" : requested.Count.ToString(),
                    requested is null ? string.Empty : string.Join(", ", requested));
                achievementProgressService.ApplyProgress(session);
                await achievementProgressService.SendListAsync(session, requested);
                await session.Client.SendPacket(AchievementPacketWriter.TitleChanged(
                    session.CharacterId, session.DisplayTitleId));
                break;

            case AchievementSubOpcode.Summary:
                achievementProgressService.ApplyProgress(session);
                await achievementProgressService.SendSummaryAsync(session);
                break;

            case AchievementSubOpcode.ClaimReward:
            case AchievementSubOpcode.ClaimTitle:
                await HandleClaimAsync(session, packet);
                break;

            case AchievementSubOpcode.SelectDisplayTitle:
                await SelectTitleAsync(session, packet);
                break;

            default:
                logger.LogWarning(
                    "ACHIEVE sub {Sub} from {Name} has no handler; the window latches on every "
                    + "request it sends and only an answer clears it, so this freezes the panel. "
                    + "Payload: {Payload}",
                    (byte)sub, session.Name, Convert.ToHexString(packet.GetData()));
                break;
        }
    }

    private static List<int>? ReadRequestedIds(Packet packet)
    {
        if (packet.RemainingBytes < 2)
            return null;

        var count = packet.ReadUShort();
        var available = packet.RemainingBytes / 2;
        var ids = new List<int>(Math.Min(count, available));
        for (var index = 0; index < count && index < available; index++)
            ids.Add(packet.ReadUShort());

        return ids;
    }

    private async Task HandleClaimAsync(UserSession session, Packet packet)
    {
        var achievementId = packet.RemainingBytes >= 2 ? packet.ReadShort() : (short)0;

        if (!gameDataService.AchievementTable.TryGetValue(achievementId, out var definition))
        {
            await SendClaimAsync(session, achievementId, AchievementPacketWriter.ClaimItemMissing);
            return;
        }

        if (session.Achievements.Of(achievementId).State != AchievementProgressState.Achieved)
        {
            await SendClaimAsync(session, achievementId, AchievementPacketWriter.ClaimNotAvailable);
            return;
        }

        await achievementProgressService.CompleteAsync(session, definition);
        await achievementProgressService.SendListAsync(session, [achievementId]);
    }

    public bool HasUnlockedTitle(UserSession session, int titleId)
    {
        if (titleId == 0) return true;
        if (!gameDataService.AchievementTitleTable.TryGetValue(titleId, out var title)) return false;
        return session.Achievements.IsClaimed(title.AchievementId);
    }

    private async Task SelectTitleAsync(UserSession session, Packet packet)
    {
        var titleId = packet.RemainingBytes >= 2 ? packet.ReadShort() : (short)0;
        if (!HasUnlockedTitle(session, titleId))
        {
            await SendClaimAsync(session, titleId, AchievementPacketWriter.ClaimNotAvailable);
            return;
        }

        session.DisplayTitleId = titleId;
        await worldPacketCoordinator.BroadcastDisplayTitleAsync(session);
    }

    private static Task SendClaimAsync(UserSession session, int achievementId, sbyte result) =>
        session.Client.SendPacket(AchievementPacketWriter.ClaimResult(achievementId, result));
}
