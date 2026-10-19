namespace LibreKO.Network.Readers;

internal interface IPacketReader
{
    void Register(PacketDispatcher dispatcher);
}
