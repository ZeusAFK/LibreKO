
namespace LibreKO.Network;

public sealed class GameConn : FramedConn, IPacketSink
{
    bool IPacketSink.Connected => Connected;

    protected override Packet? BuildIncoming(byte[] body)
    {
        var packet = BuildPacket(body);
        if (packet.GetOpcode() == (byte)GameOpcodes.GS_COMPRESS_PACKET)
            return Packet.Decompress(packet);
        return packet;
    }
}
