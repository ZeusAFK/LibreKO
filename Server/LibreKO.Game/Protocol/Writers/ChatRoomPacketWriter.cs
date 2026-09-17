using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ChatRoomPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const int NoRoomId = 0;

    public readonly record struct RoomEntry(int RoomId, string Name, ushort MemberCount);

    public static Packet RoomList(byte sub, IReadOnlyCollection<RoomEntry> rooms)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)rooms.Count);

        foreach (var room in rooms)
        {
            packet.WriteInt(room.RoomId);
            packet.WriteSByteString(room.Name);
            packet.WriteUShort(room.MemberCount);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet RoomResult(byte sub, byte result, int roomId)
    {
        var packet = Result(sub, result);
        packet.WriteInt(roomId);
        return packet;
    }

    public static Packet Say(byte sub, int roomId, string speaker, string text)
    {
        var packet = Sub(sub);
        packet.WriteInt(roomId);
        packet.WriteSByteString(speaker);
        packet.WriteSByteString(text);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_CHATROOM);
        packet.WriteByte(sub);
        return packet;
    }
}
