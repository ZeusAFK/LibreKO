namespace LibreKO.Network.Readers;

internal sealed class StealthReader : IPacketReader
{
    private readonly StealthSession stealth;

    public StealthReader(StealthSession stealth)
    {
        this.stealth = stealth;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_STEALTH, HandleStealth);
    }

    private void HandleStealth(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        bool on = p.ReadByte() != 0;
        short radius = p.ReadShort();
        stealth.RaiseSight(on ? radius : 0f);
    }
}
