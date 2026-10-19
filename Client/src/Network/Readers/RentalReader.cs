namespace LibreKO.Network.Readers;

internal sealed class RentalReader : IPacketReader
{
    private readonly RentalSession rental;

    private const int NpcStateLength = 6;

    public RentalReader(RentalSession rental)
    {
        this.rental = rental;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_RENTAL, HandleRental);
    }

    private void HandleRental(Packet p)
    {
        if (p.RemainingBytes < 1 || (RentalSubOpcode)p.ReadByte() != RentalSubOpcode.Npc || p.RemainingBytes < NpcStateLength) return;
        short state = p.ReadShort();
        p.ReadInt();
        if (state < 0) rental.RaiseRentalUnavailable();
    }
}
