namespace LibreKO.Network;

public readonly record struct AchievementTitleAnswer(AchievementSubOpcode Sub, bool Reset, int TitleId, AchievementTitleResult Result, int Seconds)
{
    public bool Cooldown => Result == (Reset ? AchievementTitleResult.ResetCooldown : AchievementTitleResult.SelectCooldown);

    public static bool Handles(AchievementSubOpcode sub) => sub is >= AchievementSubOpcode.SelectDisplayTitle and <= AchievementSubOpcode.ResetSkillTitle;

    public static AchievementTitleAnswer? Read(AchievementSubOpcode sub, Packet p)
    {
        bool reset = sub is AchievementSubOpcode.ResetDisplayTitle or AchievementSubOpcode.ResetSkillTitle;
        int title = 0;
        if (!reset)
        {
            if (p.RemainingBytes < 2 * sizeof(ushort)) return null;
            p.ReadUShort();
            title = p.ReadUShort();
        }
        if (p.RemainingBytes < sizeof(short)) return null;
        var result = (AchievementTitleResult)p.ReadShort();
        var cooldown = reset ? AchievementTitleResult.ResetCooldown : AchievementTitleResult.SelectCooldown;
        int seconds = result == cooldown && p.RemainingBytes >= sizeof(ushort) ? p.ReadUShort() : 0;
        return new AchievementTitleAnswer(sub, reset, title, result, seconds);
    }
}
