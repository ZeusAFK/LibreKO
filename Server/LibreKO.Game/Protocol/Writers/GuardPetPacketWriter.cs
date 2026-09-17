using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class GuardPetPacketWriter
{
    public const byte Inactive = 0;
    public const byte Active = 1;

    public static Packet Status(byte sub, bool active, int hp, int maxHp)
    {
        var packet = new Packet(GameOpcodes.GS_GUARD_PET);
        packet.WriteByte(sub);
        packet.WriteByte(active ? Active : Inactive);
        packet.WriteInt(active ? hp : 0);
        packet.WriteInt(active ? maxHp : 0);
        return packet;
    }
}
