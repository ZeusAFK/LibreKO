using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IKnightsCapePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class KnightsCapePacketCoordinator(
    IServiceProvider serviceProvider,
    SessionManager sessionManager,
    IGameDataService gameDataService,
    ILogger<KnightsCapePacketCoordinator> logger) : IKnightsCapePacketCoordinator
{
    private const byte OpcodeNormalPurchase = 0;
    private const byte OpcodeTicketPurchase = 1;
    private const int CastellanTicketItem = 914006000;
    private const int PaintCostClanPoints = 36000;
    private const byte ChiefFame = 1;
    private const byte ClanFlagPromoted = 2;

    private static readonly short[] KingCapeIds = [97, 98, 99];

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 7) return;

        var opcode = packet.ReadByte();
        var capeId = packet.ReadShort();
        var colour = packet.ReadInt();
        var r = (byte)(colour & 0xFF);
        var g = (byte)((colour >> 8) & 0xFF);
        var b = (byte)((colour >> 16) & 0xFF);

        if (opcode != OpcodeNormalPurchase && opcode != OpcodeTicketPurchase)
        {
            await SendFailAsync(client, CapeResult.NotAllowed);
            return;
        }

        if (session.Hp <= 0
            || session.KnightsFame != ChiefFame
            || session.KnightsId == 0
            || session.Trade.IsTrading
            || session.Trade.IsMerchanting
            || session.IsGathering)
        {
            await SendFailAsync(client, CapeResult.NotAllowed);
            return;
        }

        var clan = sessionManager.Knights.GetClan(session.KnightsId);
        if (clan == null)
        {
            await SendFailAsync(client, CapeResult.ClanNotFound);
            return;
        }

        if (clan.Flag < ClanFlagPromoted)
        {
            await SendFailAsync(client, CapeResult.NotAllowed);
            return;
        }

        int reqCoins = 0;
        int reqClanPoints = 0;

        if (capeId >= 0)
        {
            if (KingCapeIds.Contains(capeId))
            {
                await SendFailAsync(client, CapeResult.NotSoldHere);
                return;
            }

            if (!gameDataService.KnightsCapeTable.TryGetValue(capeId, out var capeDef))
            {
                await SendFailAsync(client, CapeResult.NotSoldHere);
                return;
            }

            // Ticket-only opcode requires that the cape's data flags it as a castellan/ticket cape.
            // Our schema stores Grade/Ranking/BuyPrice/BuyLoyalty; if BuyPrice == 0 we treat it as ticket-eligible.
            if (opcode == OpcodeTicketPurchase)
            {
                if (capeDef.BuyPrice != 0)
                {
                    await SendFailAsync(client, CapeResult.NotAllowed);
                    return;
                }

                if (!HasInventoryItem(session, CastellanTicketItem))
                {
                    await SendFailAsync(client, CapeResult.MissingPurchaseItem);
                    return;
                }

                if (clan.Grade > 3)
                {
                    await SendFailAsync(client, CapeResult.RankTooLow);
                    return;
                }
            }
            else
            {
                if ((capeDef.Grade > 0 && clan.Grade > capeDef.Grade)
                    || (capeDef.Ranking > 0 && clan.Flag < capeDef.Ranking))
                {
                    await SendFailAsync(client, CapeResult.RankTooLow);
                    return;
                }

                if (capeDef.BuyPrice > 0 && session.Money < capeDef.BuyPrice)
                {
                    await SendFailAsync(client, CapeResult.NotEnoughCoins);
                    return;
                }

                reqCoins = capeDef.BuyPrice;
                reqClanPoints = capeDef.BuyLoyalty;
            }
        }

        bool applyingPaint = r != 0 || g != 0 || b != 0;
        if (applyingPaint)
        {
            if (clan.Grade > 3)
            {
                await SendFailAsync(client, CapeResult.NotAllowed);
                return;
            }
            reqClanPoints += PaintCostClanPoints;
        }

        if (opcode == OpcodeNormalPurchase && session.Money < reqCoins)
        {
            await SendFailAsync(client, CapeResult.NotEnoughCoins);
            return;
        }

        if (opcode == OpcodeNormalPurchase && reqClanPoints > 0 && clan.ClanPointFund < reqClanPoints)
        {
            await SendFailAsync(client, CapeResult.NotEnoughClanPoints);
            return;
        }

        // ── Apply changes ─────────────────────────────────────────────────
        if (opcode == OpcodeTicketPurchase && capeId >= 0)
        {
            if (!ConsumeOneTicket(session))
                return;
        }

        if (opcode == OpcodeNormalPurchase)
        {
            session.Money -= reqCoins;
            clan.ClanPointFund -= reqClanPoints;
        }

        if (capeId >= 0) clan.Cape = capeId;
        if (applyingPaint)
        {
            clan.CapeR = r;
            clan.CapeG = g;
            clan.CapeB = b;
        }

        // Persist clan changes.
        using (var scope = serviceProvider.CreateScope())
        {
            var knightsRepo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            await knightsRepo.UpdateAsync(clan);
        }

        await client.SendPacket(
            KnightsPacketWriter.Cape((short)clan.Id, clan.Cape, clan.CapeR, clan.CapeG, clan.CapeB));

        // Broadcast KNIGHTS_UPDATE so all online members refresh their cape view.
        var update = KnightsPacketWriter.ClanUpdate(
            KnightsSubOpcode.Update,
            (short)clan.Id, clan.Flag, clan.Cape,
            clan.CapeR, clan.CapeG, clan.CapeB, clan.Points);
        foreach (var member in sessionManager.GetAll())
        {
            if (member.KnightsId == clan.Id)
                await member.Client.SendPacket(update);
        }

        logger.LogInformation("Clan {Clan} updated cape: id={Id} rgb=({R},{G},{B}) cost={Coins} gold + {Points} cp",
            clan.Name, clan.Cape, clan.CapeR, clan.CapeG, clan.CapeB, reqCoins, reqClanPoints);
    }

    private static bool HasInventoryItem(UserSession session, int itemId)
    {
        for (int i = Common.Domain.Entities.GameData.InventoryConstants.InventoryStart;
             i < Common.Domain.Entities.GameData.InventoryConstants.InventoryStart
                 + Common.Domain.Entities.GameData.InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[i];
            if (slot.ItemId == itemId && slot.Count > 0) return true;
        }
        return false;
    }

    private static bool ConsumeOneTicket(UserSession session)
    {
        for (int i = Common.Domain.Entities.GameData.InventoryConstants.InventoryStart;
             i < Common.Domain.Entities.GameData.InventoryConstants.InventoryStart
                 + Common.Domain.Entities.GameData.InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[i];
            if (slot.ItemId == CastellanTicketItem && slot.Count > 0)
            {
                slot.Count -= 1;
                if (slot.Count == 0) slot.Clear();
                return true;
            }
        }
        return false;
    }

    private static async Task SendFailAsync(IClient client, CapeResult result)
    {
        await client.SendPacket(KnightsPacketWriter.CapeRefused(result));
    }
}
