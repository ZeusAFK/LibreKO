using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface INationTaxPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class NationTaxPacketCoordinator(
    SessionManager sessionManager,
    ILogger<NationTaxPacketCoordinator> logger) : INationTaxPacketCoordinator
{
    private const byte NationTaxSubStatus = 1;

    private struct NationTaxRecord
    {
        public byte NationTaxSellPct;     // merchant sell-tax percent (0-100)
        public byte NationTaxZonePct;     // zone tariff / toll percent (0-100)
        public int NationTaxTreasury;     // accumulated national treasury (gold)
    }

    // nation byte -> tax record (in-memory; resets on server restart).
    private static readonly Dictionary<byte, NationTaxRecord> NationTaxByNation = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case NationTaxSubStatus:
                await NationTaxSendStatusAsync(session);
                break;
        }
    }

    private async Task NationTaxSendStatusAsync(UserSession session)
    {
        var rec = NationTaxGetRecord((byte)session.Nation);
        logger.LogDebug(
            "{Name} requested nation tax: nation={Nation} sell={Sell}% zone={Zone}% treasury={Treasury}",
            session.Name, session.Nation, rec.NationTaxSellPct, rec.NationTaxZonePct, rec.NationTaxTreasury);

        var result = NationTaxPacketWriter.Status(
            NationTaxSubStatus, rec.NationTaxSellPct, rec.NationTaxZonePct, rec.NationTaxTreasury);
        await session.Client.SendPacket(result);
    }

    private static NationTaxRecord NationTaxGetRecord(byte nation)
    {
        if (!NationTaxByNation.TryGetValue(nation, out var rec))
        {
            // Sensible retail-flavoured defaults per nation (Karus=1, El Morad=2).
            rec = nation switch
            {
                1 => new NationTaxRecord { NationTaxSellPct = 5, NationTaxZonePct = 3, NationTaxTreasury = 1_250_000 },
                2 => new NationTaxRecord { NationTaxSellPct = 5, NationTaxZonePct = 3, NationTaxTreasury = 1_180_000 },
                _ => new NationTaxRecord { NationTaxSellPct = 0, NationTaxZonePct = 0, NationTaxTreasury = 0 },
            };
            NationTaxByNation[nation] = rec;
        }
        return rec;
    }
}
