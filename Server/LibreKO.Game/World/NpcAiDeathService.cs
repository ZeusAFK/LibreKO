using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface INpcAiDeathService
{
    Task HandlePlayerKilledByNpcAsync(UserSession target, NpcInstance npc);
}

public class NpcAiDeathService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IPlayerProgressionService playerProgressionService,
    IMiningPacketCoordinator miningPacketCoordinator,
    IExchangeLifecycleService exchangeLifecycleService) : INpcAiDeathService
{
    public async Task HandlePlayerKilledByNpcAsync(UserSession target, NpcInstance npc)
    {
        var deadPacket = DeathPacketWriter.PlayerDeath(target.CharacterId, npc.UniqueId);
        await sessionManager.Regions.SendToRegion(target, deadPacket, excludeSender: false);

        // Cancelling hands both players' escrowed offers back; clearing the list would lose them.
        if (target.Trade.IsTrading)
            await exchangeLifecycleService.CancelAsync(target, isOnDeath: true);

        if (target.Trade.IsMerchanting)
            target.Trade.MerchantState = Protocol.MerchantMode.None;

        await miningPacketCoordinator.StopGatheringAsync(target);

        var expLoss = DeathPenaltyCalculator.CalculateNpcDeathExpLoss(target, npc, gameDataService);
        target.DeathExpLoss = expLoss;
        if (expLoss > 0)
            await playerProgressionService.ChangeExperienceAsync(target, -expLoss);

        target.KillerNpcType = npc.NpcType;
    }
}
