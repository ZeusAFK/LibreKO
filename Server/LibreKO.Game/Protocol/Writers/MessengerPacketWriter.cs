using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MessengerPacketWriter
{
    public const byte StatusOnline = 1;

    public readonly record struct Buddy(int CharacterId, string Name, byte Status);

    public static Packet OnlineList(byte sub, IReadOnlyCollection<Buddy> buddies)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)buddies.Count);

        foreach (var buddy in buddies)
        {
            packet.WriteInt(buddy.CharacterId);
            packet.WriteSByteString(buddy.Name);
            packet.WriteByte(buddy.Status);
        }

        return packet;
    }

    public static Packet WhisperResult(byte sub, byte result, string targetName)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteSByteString(targetName);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_MESSENGER);
        packet.WriteByte(sub);
        return packet;
    }
}
