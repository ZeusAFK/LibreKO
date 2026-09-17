using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MiningPacketWriter
{
    public static Packet Result(byte sub, ushort resultCode)
    {
        var packet = Sub(sub);
        packet.WriteUShort(resultCode);
        return packet;
    }

    public static Packet Broadcast(byte sub, ushort resultCode, int characterId)
    {
        var packet = Result(sub, resultCode);
        packet.WriteInt(characterId);
        return packet;
    }

    public static Packet AttemptSucceeded(byte sub, ushort resultCode, int characterId, ushort effect)
    {
        var packet = Broadcast(sub, resultCode, characterId);
        packet.WriteUShort(effect);
        return packet;
    }

    public static Packet BettingResult(
        byte sub, ushort result, int characterId, byte playerRoll, byte npcRoll)
    {
        var packet = Broadcast(sub, result, characterId);
        packet.WriteUShort(0);
        packet.WriteByte(playerRoll);
        packet.WriteByte(npcRoll);
        return packet;
    }

    public static Packet Durability(byte slotIndex, short durability)
    {
        var packet = new Packet(GameOpcodes.GS_DURATION);
        packet.WriteByte(slotIndex);
        packet.WriteShort(durability);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_MINING);
        packet.WriteByte(sub);
        return packet;
    }
}
