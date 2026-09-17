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
    IMiningPacketCoordinator miningPacketCoordinator) : INpcAiDeathService
{
    public async Task HandlePlayerKilledByNpcAsync(UserSession target, NpcInstance npc)
    {
        var deadPacket = DeathPacketWriter.PlayerDeath(target.CharacterId, npc.UniqueId);
        await sessionManager.Regions.SendToRegion(target, deadPacket, excludeSender: false);

        if (target.Trade.IsTrading)
        {
            target.Trade.ExchangeUser = -1;
            target.Trade.ExchangeOk = false;
            target.Trade.ExchangeItemList.Clear();
        }

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
