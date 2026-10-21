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
    IExchangeLifecycleService exchangeLifecycleService,
    IMerchantLifecycleService merchantLifecycleService) : INpcAiDeathService
{
    public async Task HandlePlayerKilledByNpcAsync(UserSession target, NpcInstance npc)
    {
        if (target.IsBot || !target.TryRecordDeath())
            return;

        var deadPacket = DeathPacketWriter.PlayerDeath(target.CharacterId, npc.UniqueId);
        await sessionManager.Regions.SendToRegion(target, deadPacket, excludeSender: false);

        if (target.Trade.IsTrading)
            await exchangeLifecycleService.CancelAsync(target, isOnDeath: true);

        if (target.Trade.IsMerchanting || target.Trade.IsMerchantPreparing || target.Trade.BuyMerchantGold > 0)
            await merchantLifecycleService.CloseAsync(target, MerchantInOut.StallClosed);

        await miningPacketCoordinator.StopGatheringAsync(target);

        var expLoss = DeathPenaltyCalculator.CalculateNpcDeathExpLoss(target, npc, gameDataService);
        target.DeathExpLoss = expLoss;
        if (expLoss > 0)
            await playerProgressionService.ChangeExperienceAsync(target, -expLoss);

        target.KillerNpcType = npc.NpcType;
    }
}
