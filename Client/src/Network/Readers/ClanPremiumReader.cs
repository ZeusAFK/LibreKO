namespace LibreKO.Network.Readers;

internal sealed class ClanPremiumReader : IPacketReader
{
    private readonly ClanPremiumSession clanPremium;

    public ClanPremiumReader(ClanPremiumSession clanPremium)
    {
        this.clanPremium = clanPremium;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CLAN_PREMIUM, HandleClanPremium);
    }

    private void HandleClanPremium(Packet p)
    {
        if (ClanPremiumReply.Read(p) is not { } reply) return;
        clanPremium.ApplyStatus(reply.Active, reply.Active && reply.SecondsLeft > 0
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() + reply.SecondsLeft
            : 0);
    }
}
