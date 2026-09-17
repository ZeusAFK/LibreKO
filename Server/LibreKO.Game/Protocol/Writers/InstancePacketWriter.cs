using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class InstancePacketWriter
{
    public readonly record struct Entry(int Id, string Name, byte MinLevel, ushort PartySize);

    public static Packet InstanceList(byte sub, IReadOnlyCollection<Entry> instances)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)instances.Count);

        foreach (var instance in instances)
        {
            packet.WriteInt(instance.Id);
            packet.WriteSByteString(instance.Name);
            packet.WriteByte(instance.MinLevel);
            packet.WriteUShort(instance.PartySize);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result, int instanceId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(instanceId);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_INSTANCE);
        packet.WriteByte(sub);
        return packet;
    }
}
