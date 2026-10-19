namespace LibreKO.Network;

public interface IPacketSink
{
    bool Connected { get; }

    void Send(Packet packet);
}
