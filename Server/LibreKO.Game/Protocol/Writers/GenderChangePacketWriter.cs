using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public static class GenderChangePacketWriter
{
    public const byte Failed = 0;
    public const byte Changed = 1;
    public const byte NoItem = 2;

    public static Packet Result(byte result)
    {
        var packet = new Packet(GameOpcodes.GS_GENDER_CHANGE);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet Change(int characterId, byte race, byte face, int hair)
    {
        var packet = Result(Changed);
        packet.WriteInt(characterId);
        packet.WriteByte(race);
        packet.WriteByte(face);
        packet.WriteInt(hair);
        return packet;
    }
}
