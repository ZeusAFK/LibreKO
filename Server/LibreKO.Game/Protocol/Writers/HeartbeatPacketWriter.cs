using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class HeartbeatPacketWriter
{
    public static Packet Probe(byte opcode, byte[] payload)
    {
        var packet = new Packet(opcode);
        packet.WriteBytes(payload);
        return packet;
    }
}
