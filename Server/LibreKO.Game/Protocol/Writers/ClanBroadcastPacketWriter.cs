using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ClanBroadcastPacketWriter
{
    public const byte ScreenNoticeType = 1;

    public static Packet ClanPremium(byte sub, byte result)
    {
        var packet = new Packet(GameOpcodes.GS_CLAN_PREMIUM);
        packet.WriteByte(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet ClanBattleNotification() => new(GameOpcodes.GS_CLAN_BATTLE);

    public static Packet ClanPointsBattleNotification(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_CLANPOINTS_BATTLE);
        packet.WriteByte(ScreenNoticeType);
        packet.WriteByte(sub);
        return packet;
    }
}
