namespace LibreKO.Network.Readers;

internal sealed class GenieReader : IPacketReader
{
    private readonly GenieSession genie;

    public GenieReader(GenieSession genie)
    {
        this.genie = genie;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_GENIE, HandleGenie);
    }

    private void HandleGenie(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            string tip = p.ReadSByteString();
            bool rewardAvail = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) != 0;
            genie.RaiseGenieStatus(tip, rewardAvail);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int rewardGold = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            genie.RaiseGenieClaim(ok, rewardGold);
        }
    }
}
