using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public static class KnightsBroadcastBuilders
{
    public const byte ClanPremiumQuery = 1;
    public const byte ClanPremiumStatusChange = 2;
    public const byte ClanPremiumInactive = 0;
    public const byte ClanPremiumActive = 1;

    public static Packet BuildClanPremium(byte subOpcode, byte result) =>
        ClanBroadcastPacketWriter.ClanPremium(subOpcode, result);

    public static Packet BuildClanBattleNotification() =>
        ClanBroadcastPacketWriter.ClanBattleNotification();

    public const byte ClanPointsBattleDisband = 0;
    public const byte ClanPointsBattleSub1 = 1;
    public const byte ClanPointsBattleSub2 = 2;
    public const byte ClanPointsBattleSub3 = 3;
    public const byte ClanPointsBattleSub4 = 4;
    public const byte ClanPointsBattleSub5 = 5;

    public static Packet BuildClanPointsBattleNotification(byte sub) =>
        ClanBroadcastPacketWriter.ClanPointsBattleNotification(sub);
}
