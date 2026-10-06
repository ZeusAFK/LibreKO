using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class SiegeRules
{
    public const ushort MaxTariff = 20;
    public const byte ChallengerSlots = 10;
    public const uint ChallengerSignUpFee = 0;
    public const byte FirstWeekday = 1;
    public const byte LastWeekday = 7;

    public static bool IsCastleLord(UserSession session, SiegeWarfareData? siege) =>
        siege is { MasterKnights: > 0 }
        && session.KnightsId == siege.MasterKnights
        && session.KnightsFame == ClanRules.FameChief;

    public static bool IsWeekday(byte day) => day is >= FirstWeekday and <= LastWeekday;

    public static byte DayBefore(byte weekday) => weekday == FirstWeekday ? LastWeekday : (byte)(weekday - 1);
}
