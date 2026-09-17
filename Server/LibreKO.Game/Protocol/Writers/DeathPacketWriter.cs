using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class DeathPacketWriter
{
    public const int NoKiller = -1;

    public static Packet NpcDeath(int npcUniqueId)
    {
        var packet = new Packet(GameOpcodes.GS_DEAD);
        packet.WriteInt(npcUniqueId);
        return packet;
    }

    public static Packet PlayerDeath(int characterId, int killerId)
    {
        var packet = new Packet(GameOpcodes.GS_DEAD);
        packet.WriteInt(characterId);
        packet.WriteInt(killerId);
        packet.WriteInt(0);
        packet.WriteInt(0);
        return packet;
    }
}
