namespace LibreKO.Domain;

public static class FullMoon
{
    public const int Zone = 89;
    public const int SkillTree = 3500;
    public static readonly int[] FreeSkills = { 490330, 490331, 490332, 490333, 490334, 490335 };
    public const byte EntryKeyA = 0x2B;
    public const byte EntryKeyB = 0x00;
    public const byte ShoutKind = 4;
    public const double ShoutCooldownSeconds = 600;
    public const int ShoutText = 37300;
    public const int OtherTypeText = 37306;
    public const int LookingOtherText = 37307;
    public const int CooldownText = 16820;
    public const int ZoneNameText = 11647;
    public const int PartySize = 2;

    private const int TowerDownText = 37301;
    private const int AllTowersDownText = 37302;
    private const int StageText = 37303;
    private const int LastStageText = 37304;
    private const int SystemErrorText = 16506;

    public static (int Id, string Fallback) EntryText(int code) => code switch
    {
        1 => (40070, "Instance generation failed"),
        2 => (37310, "You can only enter when all party members are at Moradon"),
        3 => (37311, "Another instance is being created"),
        4 => (37312, "You cannot enter when one of your party members is dead"),
        5 => (37313, "There is no party available"),
        6 => (37314, "It is not a Full Moon Rift party"),
        7 => (37315, "You cannot enter. One of your party members does not have Full Moon Rift Voucher"),
        8 or 9 or 11 => (37318, ""),
        10 => (37317, "You cannot enter due to the number of dungeons has reached its max capacity."),
        12 => (37319, ""),
        _ => (SystemErrorText, "System Error"),
    };

    public static (int Id, string Fallback, int[] Args) TowerText(int remaining) => remaining > 0
        ? (TowerDownText, "The tower has been destroyed. Remaining tower(s): %d", new[] { remaining })
        : (AllTowersDownText, "All the towers have been destroyed", System.Array.Empty<int>());

    public static (int Id, string Fallback, int[] Args) StageLine(int total, int current) => current == total
        ? (LastStageText, "Stage %d has begun. This is the last stage", new[] { current })
        : (StageText, "Stage %d has begun. Remaining stage(s): %d", new[] { current, total - current });

    public static (int Id, string Fallback)? OpenRefusal(bool inParty, int partySize, PartyBoardType partyType, PartyBoardType? boardType) =>
        inParty && partySize > 1 && partyType != PartyBoardType.FullMoon ? (OtherTypeText, "You cannot search for different type of party")
        : boardType is not (null or PartyBoardType.FullMoon) ? (LookingOtherText, "You are currently looking for another type of party")
        : null;

}
