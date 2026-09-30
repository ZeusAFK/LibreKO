namespace LibreKO.Game.Protocol;

public enum MonsterStoneResult : byte
{
    CannotEnterHere = 1,
    Entered = 2,
    PartyLeaderOnly = 4,
    Failed = 5,
    PartyLevelTooHigh = 8,
    NotEnoughHealth = 9,
}
