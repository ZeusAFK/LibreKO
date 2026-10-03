using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

// Server -> client party DPS broadcast. Carries the party's accumulated damage
// table plus a shared window-start (the earliest moment any member dealt damage),
// so the client can compute a per-member DPS figure.
public sealed class DpsPacketWriter
{
    public static Packet DamageTable(long windowStartUnix, IReadOnlyList<(int CharId, long Damage)> entries)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY_DPS);
        packet.WriteLong(windowStartUnix);
        packet.WriteShort((short)entries.Count);
        foreach (var entry in entries)
        {
            packet.WriteInt(entry.CharId);
            packet.WriteLong(entry.Damage);
        }
        return packet;
    }
}
