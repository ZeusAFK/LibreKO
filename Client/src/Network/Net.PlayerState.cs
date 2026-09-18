using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public CharacterSheet Sheet { get; } = new();

    public Vitals Vitals { get; } = new();

    public MasteryPoints Mastery { get; } = new();

    public Dictionary<int, double> SkillCooldowns { get; } = new();

    public double PotionReadyAt { get; set; }

    public Dictionary<int, double> BuffEnds { get; } = new();

    private readonly HashSet<string> _openWindows = new();

    public bool IsWindowOpen(string key) => _openWindows.Contains(key);

    public void SetWindowOpen(string key, bool open)
    {
        if (open) _openWindows.Add(key);
        else _openWindows.Remove(key);
    }

    private void ApplyOwnClass(int newClass)
    {
        if (newClass == 0 || newClass == LastEnter.Class) return;
        var info = LastEnter;
        info.Class = newClass;
        LastEnter = info;
    }

    private void SeedPlayerState(
        int level, long exp, long maxExp,
        int hp, int maxHp, int mp, int maxMp,
        int str, int sta, int dex, int intel, int magicStat, int statPoints,
        int strBonus, int staBonus, int dexBonus, int intelBonus, int magicBonus,
        int ap, int ac, int gold, int np, int maxWeight,
        int fireR, int coldR, int lightningR, int magicR, int diseaseR, int poisonR,
        byte[] skillPoints)
    {
        SkillCooldowns.Clear();
        PotionReadyAt = 0;
        BuffEnds.Clear();
        Sheet.SeedStats(str, sta, dex, intel, magicStat, statPoints);
        Sheet.SeedStatBonuses(strBonus, staBonus, dexBonus, intelBonus, magicBonus);
        Sheet.SeedCombat(ap, ac);
        Sheet.SeedWealth(gold, np);
        Sheet.SeedResists(fireR, coldR, lightningR, magicR, diseaseR, poisonR);
        Sheet.SeedProgress(level, exp, maxExp);
        Sheet.SetMaxWeight(maxWeight);
        Vitals.Seed(hp, maxHp, mp, maxMp);
        Mastery.Seed(skillPoints);
        ClearParty("a fresh GS_MYINFO (character select or reconnect)");
        _openWindows.Clear();
    }
}
