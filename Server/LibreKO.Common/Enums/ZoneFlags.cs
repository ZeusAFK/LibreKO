namespace LibreKO.Common.Enums;

[Flags]
public enum ZoneFlags : byte
{
    None = 0,
    TradeOtherNation = 1 << 0,
    TalkOtherNation = 1 << 1,
    AttackOtherNation = 1 << 2,
    AttackSameNation = 1 << 3,
    FriendlyNpcs = 1 << 4,
    WarZone = 1 << 5,
    ClanUpdate = 1 << 6,
}
