using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class DisguisePacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public readonly record struct Entry(int DisguiseId, string Name);

    public static Packet DisguiseList(byte sub, IReadOnlyCollection<Entry> disguises)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)disguises.Count);

        foreach (var disguise in disguises)
        {
            packet.WriteInt(disguise.DisguiseId);
            packet.WriteSByteString(disguise.Name);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result, int disguiseId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(disguiseId);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_DISGUISE);
        packet.WriteByte(sub);
        return packet;
    }
}
