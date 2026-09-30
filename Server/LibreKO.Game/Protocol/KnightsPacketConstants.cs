namespace LibreKO.Game.Protocol;

public static class KnightsPacketConstants
{
    public const int ClanSymbolCost = 5_000_000;
    public const int MaxKnightsMarkBytes = 2400;

    public const int ClanLevelRequirement = World.ClanRules.CreationLevel;
    public const int ClanCoinRequirement = World.ClanRules.CreationCoins;
    public const int MaxClanUsers = World.ClanRules.MaxMembers;

    public const int ClanListPageSize = 10;
    public const int MaxNoticeLength = 200;
    public const int MaxMemoLength = 20;
}
