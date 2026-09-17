namespace LibreKO.Game.Protocol;

public enum AchievementSubOpcode : byte
{
    ClaimResult = 2,
    List = 3,
    Summary = 4,
    ClaimReward = 6,
    ClaimTitle = 7,
    SelectDisplayTitle = 16,
    TitleChanged = 32,
}
