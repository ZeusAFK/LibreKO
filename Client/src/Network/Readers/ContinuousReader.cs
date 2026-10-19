namespace LibreKO.Network.Readers;

internal sealed class ContinuousReader : IPacketReader
{
    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_CONTINOUS_PACKET, p => HandleContinuous(p, dispatcher));
    }

    private static void HandleContinuous(Packet p, PacketDispatcher dispatcher)
    {
        foreach (var piece in ContinuousPacket.Split(p))
            if (piece.GetOpcode() != (byte)GameOpcodes.GS_CONTINOUS_PACKET) dispatcher.Dispatch(piece);
    }
}
