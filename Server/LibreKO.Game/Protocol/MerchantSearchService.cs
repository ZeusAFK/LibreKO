using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IMerchantSearchService
{
    Task HandleAsync(UserSession session, Packet packet);
}

public class MerchantSearchService(
    SessionManager sessionManager,
    IMagicItemUsageService magicItemUsageService,
    IWorldMovementService worldMovementService,
    ILogger<MerchantSearchService> logger) : IMerchantSearchService
{
    public const int OfficialListItem = 810166000;
    private const int StallsPerChunk = 50;
    private const int SellerIdMask = 0xFFFF;
    private const float StandBeside = 1.5f;
    private const int WarpScale = 10;

    public async Task HandleAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        switch (packet.ReadByte())
        {
            case MerchantSearchPacketWriter.Open when packet.RemainingBytes >= 4:
                await OpenAsync(session, packet.ReadInt());
                break;
            case MerchantSearchPacketWriter.Results when packet.RemainingBytes >= 4:
                await SendChunkAsync(session, packet.ReadInt());
                break;
            case MerchantSearchPacketWriter.Move when packet.RemainingBytes >= 2:
                await MoveAsync(session, packet.ReadUShort());
                break;
        }
    }

    private async Task OpenAsync(UserSession session, int itemId)
    {
        var allowed = itemId == OfficialListItem && magicItemUsageService.CanUseItem(session, OfficialListItem);
        await session.Client.SendPacket(MerchantSearchPacketWriter.Result(MerchantSearchPacketWriter.Open,
            allowed ? MerchantSearchPacketWriter.Accepted : MerchantSearchPacketWriter.Refused));
    }

    private async Task SendChunkAsync(UserSession session, int cursor)
    {
        if (!magicItemUsageService.CanUseItem(session, OfficialListItem))
        {
            await session.Client.SendPacket(MerchantSearchPacketWriter.ResultsRefused());
            return;
        }

        var stalls = StallsBeside(session).ToList();
        var start = Math.Clamp(cursor, 0, stalls.Count);
        var chunk = stalls.Skip(start).Take(StallsPerChunk).ToList();
        var next = start + chunk.Count;
        await session.Client.SendPacket(MerchantSearchPacketWriter.Chunk(next >= stalls.Count, next, chunk));
    }

    private IEnumerable<MerchantSearchStall> StallsBeside(UserSession session) =>
        sessionManager.GetAll()
            .Where(merchant => merchant.Trade.IsMerchanting
                               && merchant.ZoneId == session.ZoneId
                               && merchant.Room == session.Room
                               && merchant.CharacterId != session.CharacterId)
            .OrderBy(merchant => merchant.CharacterId)
            .Select(merchant => new MerchantSearchStall(
                merchant.CharacterId,
                merchant.Name,
                merchant.Trade.MerchantState,
                merchant.Trade.IsBuyingMerchant ? merchant.Trade.BuyMerchantItems : merchant.Trade.MerchantItems));

    private async Task MoveAsync(UserSession session, ushort sellerId)
    {
        var merchant = sessionManager.GetAll().FirstOrDefault(candidate =>
            (candidate.CharacterId & SellerIdMask) == sellerId
            && candidate.Trade.IsMerchanting
            && candidate.ZoneId == session.ZoneId
            && candidate.Room == session.Room);

        if (merchant == null || session.Hp <= 0 || session.Trade.IsTrading || session.Trade.IsMerchanting
            || session.IsGathering || !magicItemUsageService.CanUseItem(session, OfficialListItem))
        {
            await session.Client.SendPacket(MerchantSearchPacketWriter.Result(MerchantSearchPacketWriter.Move,
                MerchantSearchPacketWriter.Refused));
            return;
        }

        await session.Client.SendPacket(MerchantSearchPacketWriter.Result(MerchantSearchPacketWriter.Move,
            MerchantSearchPacketWriter.Accepted));
        await worldMovementService.WarpAsync(session,
            (ushort)((merchant.X + StandBeside) * WarpScale),
            (ushort)((merchant.Z + StandBeside) * WarpScale));
        logger.LogDebug("Merchant search: {Name} moved beside {Merchant}", session.Name, merchant.Name);
    }
}
