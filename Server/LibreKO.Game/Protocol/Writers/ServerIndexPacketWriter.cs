using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ServerIndexPacketWriter
{
    public const ushort SingleServerId = 1;
    public const ushort SingleServerGroupId = 1;

    public static Packet Index(ushort serverId, ushort groupId)
    {
        var packet = new Packet(GameOpcodes.GS_SERVER_INDEX);
        packet.WriteUShort(serverId);
        packet.WriteUShort(groupId);
        return packet;
    }
}
