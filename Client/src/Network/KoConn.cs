
namespace LibreKO.Network;

public sealed class KoConn : FramedConn
{
    protected override Packet? BuildIncoming(byte[] body)
    {
        var packet = BuildPacket(body);
        if (packet.GetOpcode() == (byte)GameOpcodes.GS_COMPRESS_PACKET)
            return Packet.Decompress(packet);
        return packet;
    }
}
