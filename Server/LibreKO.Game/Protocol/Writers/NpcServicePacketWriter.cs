using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class NpcServicePacketWriter
{
    public const byte ClanCapeSub = 20;
    public const byte ClassChangeOpenSub = 1;

    public static Packet TradeNpc(int sellingGroup)
    {
        var packet = new Packet(GameOpcodes.GS_TRADE_NPC);
        packet.WriteInt(sellingGroup);
        return packet;
    }

    public static Packet RepairNpc(int sellingGroup)
    {
        var packet = new Packet(GameOpcodes.GS_REPAIR_NPC);
        packet.WriteInt(sellingGroup);
        return packet;
    }

    public static Packet ClanCapeNpc()
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte(ClanCapeSub);
        return packet;
    }

    public static Packet WarehouseNpc(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte(sub);
        return packet;
    }

    public static Packet ClassChangeNpc()
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte(ClassChangeOpenSub);
        return packet;
    }
}
