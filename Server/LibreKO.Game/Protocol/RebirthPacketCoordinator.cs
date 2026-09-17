using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IRebirthPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class RebirthPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    ILogger<RebirthPacketCoordinator> logger) : IRebirthPacketCoordinator
{

    public const int RebirthGoldCost = 100_000_000;
    public const int RebirthLoyaltyCost = 10_000;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1) return;

        var sub = (RebirthSubOpcode)packet.ReadByte();
        if (sub != RebirthSubOpcode.Activate)
        {
            // Server only handles activation; other sub-opcodes are S2C-only builders.
            logger.LogDebug("WIZ_REBIRTH unhandled sub {Sub} from {Name}", sub, session.Name);
            return;
        }

        await HandleRequestAsync(client, session);
    }

    private async Task HandleRequestAsync(IClient client, UserSession session)
    {
        var levelExp = gameDataService.GetMaxExpForLevel(session.Level);
        var maxExp = RebirthBonus.RequiredExperience(levelExp, session.RebirthLevel);
        var ready = session.Level >= ProgressionTable.MaxLevel
            && maxExp > 0
            && session.Experience >= maxExp
            && session.Money >= RebirthGoldCost
            && session.Loyalty >= RebirthLoyaltyCost
            && session.RebirthLevel < RebirthBonus.MaxRebirthLevel;

        if (!ready)
        {
            await client.SendPacket(BuildResult(0));
            return;
        }

        await client.SendPacket(BuildActivate());
        await client.SendPacket(BuildComplete(session.RebirthLevel));

        logger.LogDebug("{Name} is eligible for rebirth {Level}",
            session.Name, session.RebirthLevel + 1);
    }

    public static Packet BuildActivate() => RebirthPacketWriter.Activate(RebirthSubOpcode.Activate);

    public static Packet BuildResult(byte resultCode) =>
        RebirthPacketWriter.Result(RebirthSubOpcode.Result, resultCode);

    public static Packet BuildProgress(int levelOffset, int current, int max) =>
        RebirthPacketWriter.Progress(RebirthSubOpcode.Progress, levelOffset, current, max);

    public static Packet BuildComplete(int rebirthLevel) =>
        RebirthPacketWriter.Complete(RebirthSubOpcode.Complete, rebirthLevel);
}
