using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class GlobalMapPacketWriter
{
    public readonly record struct ZoneEntry(
        ushort ZoneId, string Name, ushort PlayerCount, byte OwnerNation);

    public static Packet ZoneList(byte sub, IReadOnlyCollection<ZoneEntry> zones)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)zones.Count);

        foreach (var zone in zones)
        {
            packet.WriteUShort(zone.ZoneId);
            packet.WriteSByteString(zone.Name);
            packet.WriteUShort(zone.PlayerCount);
            packet.WriteByte(zone.OwnerNation);
        }

        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_GLOBAL_MAP);
        packet.WriteByte(sub);
        return packet;
    }
}
