using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public static class ItemCombinePacketWriter
{
    public const byte CombineSub = 11;
    public const byte Refused = 0;
    public const byte Succeeded = 1;
    public const byte Failed = 2;
    public const byte WrongMaterial = 3;
    public const byte CombineEffectSub = 1;

    public static Packet Refusal(byte result) => Reply(result);

    public static Packet Success(short row, int itemId, byte bagSlot)
    {
        var packet = Reply(Succeeded);
        packet.WriteShort(row);
        packet.WriteInt(itemId);
        packet.WriteByte(bagSlot);
        return packet;
    }

    public static Packet Failure(short row)
    {
        var packet = Reply(Failed);
        packet.WriteShort(row);
        return packet;
    }

    public static Packet Effect(bool success, short npcId, short row)
    {
        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(CombineEffectSub);
        packet.WriteByte(success ? Succeeded : Failed);
        packet.WriteShort(npcId);
        packet.WriteShort(row);
        return packet;
    }

    private static Packet Reply(byte result)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte(CombineSub);
        packet.WriteByte(result);
        return packet;
    }
}
