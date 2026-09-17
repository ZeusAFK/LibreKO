using LibreKO.Common.Domain.Services;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Scripting;

public static class ClanScriptEffects
{
    public static async Task ApplyPromotionAsync(
        SessionManager sessions, IServiceProvider provider, ILogger logger, int clanId)
    {
        var clan = sessions.Knights.GetClan(clanId);
        if (clan == null)
            return;

        await PersistAsync(provider, logger, clan, "promotion");

        var update = KnightsPacketWriter.ClanUpdate(
            KnightsSubOpcode.Update,
            clan.Id, clan.Flag, clan.Cape, clan.CapeR, clan.CapeG, clan.CapeB, clan.Points);
        await SendToMembersAsync(sessions, clan.Id, update);

        logger.LogInformation("Clan {Clan} promoted to type {Type}, cape {Cape}",
            clan.Name, clan.Flag, clan.Cape);
    }

    public static async Task ApplyPremiumAsync(
        SessionManager sessions, IServiceProvider provider, ILogger logger, int clanId)
    {
        var clan = sessions.Knights.GetClan(clanId);
        if (clan == null)
            return;

        await PersistAsync(provider, logger, clan, "premium");

        var notice = ClanBroadcastPacketWriter.ClanPremium(
            KnightsBroadcastBuilders.ClanPremiumStatusChange,
            clan.HasPremium
                ? KnightsBroadcastBuilders.ClanPremiumActive
                : KnightsBroadcastBuilders.ClanPremiumInactive);
        await SendToMembersAsync(sessions, clan.Id, notice);

        logger.LogInformation("Clan {Clan} premium now runs to {Expiry}", clan.Name, clan.PremiumExpiry);
    }

    private static async Task PersistAsync(
        IServiceProvider provider, ILogger logger, Common.Domain.Entities.KnightsEntity clan, string what)
    {
        try
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(clan);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not persist the {What} of clan {Clan}; it will be lost on restart",
                what, clan.Name);
        }
    }

    private static async Task SendToMembersAsync(
        SessionManager sessions, int clanId, Common.Infrastructure.Network.Packet packet)
    {
        foreach (var member in sessions.GetAll())
        {
            if (member.KnightsId == clanId)
                await member.Client.SendPacket(packet);
        }
    }
}
