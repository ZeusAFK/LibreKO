using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MovementPacketWriter
{
    public const short ResolveHeightFromMap = -1;

    public static Packet Move(int characterId, ushort x, ushort z, ushort y, short speed, byte echo)
    {
        var packet = new Packet(GameOpcodes.GS_MOVE);
        packet.WriteInt(characterId);
        packet.WriteUShort(x);
        packet.WriteUShort(z);
        packet.WriteUShort(y);
        packet.WriteShort(speed);
        packet.WriteByte(echo);
        return packet;
    }

    public static Packet StateChange(int characterId, byte type, int value)
    {
        var packet = new Packet(GameOpcodes.GS_STATE_CHANGE);
        packet.WriteInt(characterId);
        packet.WriteByte(type);
        packet.WriteInt(value);
        return packet;
    }

    public static Packet Transformation(int characterId, byte type, int skillId)
    {
        var packet = new Packet(GameOpcodes.GS_STATE_CHANGE);
        packet.WriteInt(characterId);
        packet.WriteByte(type);
        packet.WriteInt(skillId);
        return packet;
    }

    public static Packet Warp(ushort x, ushort z)
    {
        var packet = new Packet(GameOpcodes.GS_WARP);
        packet.WriteUShort(x);
        packet.WriteUShort(z);
        packet.WriteShort(ResolveHeightFromMap);
        return packet;
    }

    public static Packet Rotate(int characterId, short direction)
    {
        var packet = new Packet(GameOpcodes.GS_ROTATE);
        packet.WriteInt(characterId);
        packet.WriteShort(direction);
        return packet;
    }
}
