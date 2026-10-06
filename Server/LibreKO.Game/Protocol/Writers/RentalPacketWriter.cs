using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public static class RentalPacketWriter
{
    public const byte NpcSub = 3;
    public const short Unavailable = -1;

    public static Packet NpcState(short state, int sellingGroup)
    {
        var packet = new Packet(GameOpcodes.GS_RENTAL);
        packet.WriteByte(NpcSub);
        packet.WriteShort(state);
        packet.WriteInt(sellingGroup);
        return packet;
    }
}
