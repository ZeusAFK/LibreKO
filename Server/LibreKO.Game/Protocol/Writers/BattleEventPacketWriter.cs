using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public sealed class BattleEventPacketWriter
{
    public static Packet ZoneOpened(byte openType, byte zone)
    {
        var packet = Sub(BattleEventSubOpcode.Open);
        packet.WriteByte(openType);
        packet.WriteByte(zone);
        return packet;
    }

    public static Packet ZoneClosed(byte zone)
    {
        var packet = Sub(BattleEventSubOpcode.Open);
        packet.WriteByte(BattleZoneManager.BATTLEZONE_CLOSE);
        packet.WriteByte(zone);
        return packet;
    }

    public static Packet ZoneState(byte open, byte zone, int secondsRemaining)
    {
        var packet = Sub(BattleEventSubOpcode.Open);
        packet.WriteByte(open);
        packet.WriteByte(zone);
        packet.WriteInt(secondsRemaining);
        return packet;
    }

    public static Packet Declare(byte declareType, byte nation)
    {
        var packet = Sub(BattleEventSubOpcode.Result);
        packet.WriteByte(declareType);
        packet.WriteByte(nation);
        return packet;
    }

    public static IReadOnlyList<Packet> Winner(byte winnerNation) =>
    [
        Declare(BattleZoneManager.DECLARE_WINNER, winnerNation),
        Declare(BattleZoneManager.DECLARE_LOSER, winnerNation == (byte)AccountNation.Karus
            ? (byte)AccountNation.ElMorad
            : (byte)AccountNation.Karus),
    ];

    internal static Packet Sub(BattleEventSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_BATTLE_EVENT);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
