namespace LibreKO.Network.Readers;

internal sealed class CapeReader : IPacketReader
{
    private readonly CapeSession cape;
    private readonly ClanSession clan;

    public CapeReader(CapeSession cape, ClanSession clan)
    {
        this.cape = cape;
        this.clan = clan;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CAPE, HandleCape);
    }

    private void HandleCape(Packet p)
    {
        if (!cape.Pending || !CapeWire.TryRead(p, out var reply)) return;
        if (!reply.Changed)
        {
            cape.ApplyResult(new CapeChangeReply(Succeeded: false, ClanOrResult: (short)reply.Result, CapeId: 0, R: 0, G: 0, B: 0));
            return;
        }

        if (!clan.MyClan.InClan || reply.ClanId != clan.MyClan.ClanId)
        {
            cape.ApplyReset();
            return;
        }
        cape.ApplyResult(new CapeChangeReply(Succeeded: true, ClanOrResult: reply.ClanId, CapeId: reply.CapeId, R: reply.R, G: reply.G, B: reply.B));
    }
}
