using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class FishingHallPacketWriter
{
    public readonly record struct Entry(ushort Rank, string Name, int Score);

    public static Packet Rankings(byte sub, IReadOnlyCollection<Entry> anglers)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)anglers.Count);

        foreach (var angler in anglers)
        {
            packet.WriteUShort(angler.Rank);
            packet.WriteSByteString(angler.Name);
            packet.WriteInt(angler.Score);
        }

        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_FISHING_HALL);
        packet.WriteByte(sub);
        return packet;
    }
}
