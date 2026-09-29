using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Tests;

internal static class CountChangePackets
{
    public static List<byte> Positions(IEnumerable<Packet> packets)
    {
        var positions = new List<byte>();
        foreach (var packet in packets)
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE)
                continue;
            packet.ResetOffset();
            int entries = packet.ReadShort();
            for (var entry = 0; entry < entries; entry++)
            {
                packet.ReadByte();
                positions.Add(packet.ReadByte());
                packet.ReadInt();
                packet.ReadInt();
                packet.ReadByte();
                packet.ReadShort();
                packet.ReadInt();
            }
        }
        return positions;
    }
}
