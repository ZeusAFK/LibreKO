namespace LibreKO.Game.Protocol;

internal static class SiegePacketConstants
{
    public const byte BaseCreate = 1;
    public const byte CastleFlag = 2;
    public const byte WarfareNpc = 3;
    public const byte CastleManager = 4;
    public const byte Rank = 5;

    public const byte WarfareApply = 1;
    public const byte WarfareSchedule = 2;
    public const byte WarfareAssault = 3;
    public const byte WarfareChallengers = 4;
    public const byte WarfareDefendingUnion = 5;
    public const byte WarfareOpen = 7;

    public const byte ManagerOpen = 1;
    public const byte ManagerCollect = 2;
    public const byte ManagerTariffs = 3;
    public const byte ManagerMoradonTariff = 4;
    public const byte ManagerDelosTariff = 5;
    public const byte ManagerDungeonFee = 6;

    public const short SignUpClosed = -2;
    public const short NoAuthority = -3;
    public const short CoinsFull = -5;
    public const short RateNotAllowed = -5;
}
