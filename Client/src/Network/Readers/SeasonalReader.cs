namespace LibreKO.Network.Readers;

internal sealed class SeasonalReader : IPacketReader
{
    private readonly SeasonalSession seasonal;

    public SeasonalReader(SeasonalSession seasonal)
    {
        this.seasonal = seasonal;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_SANTA, HandleSanta);
    }

    private void HandleSanta(Packet p)
    {
        int state = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        seasonal.RaiseSanta(state);
    }
}
