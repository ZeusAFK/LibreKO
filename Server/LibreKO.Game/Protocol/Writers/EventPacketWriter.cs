using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class EventPacketWriter
{
    private const ushort RivalPanelUnknown = 1;

    public static Packet MapEventScores(byte eventType, short karusScore, short elmoradScore)
    {
        var packet = new Packet(GameOpcodes.GS_MAP_EVENT);
        packet.WriteByte(eventType);
        packet.WriteShort(karusScore);
        packet.WriteShort(elmoradScore);
        return packet;
    }

    public static Packet BattleZoneOpened(byte sub, byte openType, byte zone)
    {
        var packet = Battle(sub);
        packet.WriteByte(openType);
        packet.WriteByte(zone);
        return packet;
    }

    public static Packet BattleDeclare(byte sub, byte declareType, byte nation)
    {
        var packet = Battle(sub);
        packet.WriteByte(declareType);
        packet.WriteByte(nation);
        return packet;
    }

    public static Packet MapEventEmpty()
    {
        return new Packet(GameOpcodes.GS_MAP_EVENT);
    }

    public static Packet BattleZoneClosed(byte sub, byte closeType)
    {
        var packet = Battle(sub);
        packet.WriteByte(closeType);
        return packet;
    }

    public static Packet BattleResult(byte sub, byte resultType, byte winner, int karus, int elmorad)
    {
        var packet = Battle(sub);
        packet.WriteByte(resultType);
        packet.WriteByte(winner);
        packet.WriteInt(karus);
        packet.WriteInt(elmorad);
        return packet;
    }

    public static Packet AssignRival(
        byte sub, int rivalId, int money, int loyalty, string clanName, string rivalName)
    {
        var packet = Pvp(sub);
        packet.WriteInt(rivalId);
        packet.WriteInt(money);
        packet.WriteInt(loyalty);
        packet.WriteUShort(RivalPanelUnknown);
        packet.WriteUShort(RivalPanelUnknown);
        packet.WriteString(clanName);
        packet.WriteString(rivalName);
        return packet;
    }

    public static Packet PvpFlag(byte sub) => Pvp(sub);

    public static Packet AngerGauge(byte sub, byte gauge, byte isFull)
    {
        var packet = Pvp(sub);
        packet.WriteByte(gauge);
        packet.WriteByte(isFull);
        return packet;
    }

    public static Packet TempleEvent(byte sub, byte result, short zone)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT);
        packet.WriteByte(sub);
        packet.WriteByte(result);
        packet.WriteShort(zone);
        return packet;
    }

    public const byte DrakiTimerHeaderFirst = 233;
    public const byte DrakiTimerHeaderSecond = 3;

    public static Packet DrakiTimer(
        ushort stage, ushort subStage, int timeLimitSeconds, int elapsedSeconds)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT);
        packet.WriteByte((byte)TempleSubOpcode.DrakiTimer);
        packet.WriteByte(DrakiTimerHeaderFirst);
        packet.WriteByte(DrakiTimerHeaderSecond);
        packet.WriteUShort(stage);
        packet.WriteUShort(subStage);
        packet.WriteInt(timeLimitSeconds);
        packet.WriteInt(elapsedSeconds);
        return packet;
    }

    public static Packet BattleZoneState(byte sub, byte open, byte zone, int secondsRemaining)
    {
        var packet = Battle(sub);
        packet.WriteByte(open);
        packet.WriteByte(zone);
        packet.WriteInt(secondsRemaining);
        return packet;
    }

    private static Packet Battle(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_BATTLE_EVENT);
        packet.WriteByte(sub);
        return packet;
    }

    private static Packet Pvp(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_PVP);
        packet.WriteByte(sub);
        return packet;
    }
}
