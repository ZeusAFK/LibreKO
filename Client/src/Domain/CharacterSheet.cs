using System;

namespace LibreKO.Domain;

public sealed class CharacterSheet
{
    public const int MinLevel = 1;
    public const int MaxLevel = 83;

    public const int StatCount = 5;
    public static readonly string[] StatLabels = { "STR", "HP", "DEX", "INT", "MP" };
    public const int StatMax = 255;
    public const int BaseStatTotal = 290;
    public const int ResistCount = 6;

    public const int TypeStr = 1, TypeSta = 2, TypeDex = 3, TypeInt = 4, TypeMag = 5;

    private readonly int[] resist = new int[ResistCount];

    public int Str { get; private set; }
    public int Sta { get; private set; }
    public int Dex { get; private set; }
    public int Intel { get; private set; }
    public int Mag { get; private set; }

    public int Points { get; private set; }

    public int Ap { get; private set; }
    public int Ac { get; private set; }
    public int Np { get; private set; }
    public int Level { get; private set; }
    public const int CoinMax = 2_100_000_000;

    public int Gold { get; private set; }
    public int KnightCash { get; private set; }
    public int MaxWeight { get; private set; }
    public long Exp { get; private set; }
    public long MaxExp { get; private set; }

    public int ResistAt(int index) => (uint)index < ResistCount ? resist[index] : 0;

    public bool CanAllocate => Points > 0;

    public double ExpPercent => MaxExp > 0 ? (double)Exp / MaxExp * 100.0 : 0.0;

    public static int WireTypeForRow(int row) => row switch
    {
        0 => TypeStr, 1 => TypeSta, 2 => TypeDex, 3 => TypeInt, 4 => TypeMag, _ => 0,
    };

    public int StatAtRow(int row) => row switch
    {
        0 => Str, 1 => Sta, 2 => Dex, 3 => Intel, 4 => Mag, _ => 0,
    };

    private readonly int[] statBonus = new int[StatCount];

    public int StatBonusAtRow(int row) => (uint)row < StatCount ? statBonus[row] : 0;

    public void SeedStatBonuses(int str, int sta, int dex, int intel, int mag)
    {
        statBonus[0] = str; statBonus[1] = sta; statBonus[2] = dex;
        statBonus[3] = intel; statBonus[4] = mag;
    }

    private readonly int[] rebirthBonus = new int[StatCount];

    public int RebirthLevel { get; private set; }

    public int RebirthBonusAtRow(int row) => (uint)row < StatCount ? rebirthBonus[row] : 0;

    public string LevelLabel => RebirthLevel > 0 ? $"{Level}/{RebirthLevel}" : Level.ToString();

    public void SeedRebirth(int level, int str, int sta, int dex, int intel, int mag)
    {
        RebirthLevel = level;
        rebirthBonus[0] = str; rebirthBonus[1] = sta; rebirthBonus[2] = dex;
        rebirthBonus[3] = intel; rebirthBonus[4] = mag;
    }

    public void ApplyRebirthReset(byte[] picks)
    {
        for (int i = 0; i < StatCount && i < picks.Length; i++)
        {
            statBonus[i] += picks[i] - rebirthBonus[i];
            rebirthBonus[i] = picks[i];
        }
    }

    public void ApplyRebirth(byte[] picks)
    {
        RebirthLevel++;
        for (int i = 0; i < StatCount && i < picks.Length; i++)
        {
            rebirthBonus[i] += picks[i];
            statBonus[i] += picks[i];
        }
    }

    public int StatTotal => Str + Sta + Dex + Intel + Mag;

    public int PointsForLevel => Points + StatTotal - BaseStatTotal;

    public void SeedStats(int str, int sta, int dex, int intel, int mag, int points)
    {
        Str = str; Sta = sta; Dex = dex; Intel = intel; Mag = mag;
        Points = Math.Max(0, points);
    }

    public void SeedCombat(int ap, int ac) { Ap = ap; Ac = ac; }

    public void SeedWealth(int gold, int np) { Gold = Math.Max(0, gold); Np = np; MonthlyNp = null; }

    public void SetKnightCash(int total) => KnightCash = Math.Max(0, total);

    public void SeedProgress(int level, long exp, long maxExp)
    {
        Level = level; Exp = exp; MaxExp = maxExp;
    }

    public void SeedResists(int fire, int cold, int lightning, int magic, int disease, int poison)
    {
        resist[0] = fire; resist[1] = cold; resist[2] = lightning;
        resist[3] = magic; resist[4] = disease; resist[5] = poison;
    }

    public bool ApplyPointChange(int wireType, int newValue, int totalHit)
    {
        switch (wireType)
        {
            case TypeStr: Str = newValue; break;
            case TypeSta: Sta = newValue; break;
            case TypeDex: Dex = newValue; break;
            case TypeInt: Intel = newValue; break;
            case TypeMag: Mag = newValue; break;
            default: return false;
        }
        Points = Math.Max(0, Points - 1);
        Ap = totalHit;
        return true;
    }

    public bool ApplyReset(int[]? stats, int points, int ap)
    {
        Points = Math.Max(0, points);
        Ap = ap;
        if (stats is null || stats.Length < StatCount) return false;
        Str = stats[0]; Sta = stats[1]; Dex = stats[2]; Intel = stats[3]; Mag = stats[4];
        return true;
    }

    public bool ApplyLevel(int level, int points, long exp, long maxExp)
    {
        bool leveledUp = level > Level;
        Level = level;
        Points = Math.Max(0, points);
        Exp = exp;
        MaxExp = maxExp;
        return leveledUp;
    }

    public void SetMaxWeight(int maxWeight)
    {
        if (maxWeight > 0) MaxWeight = maxWeight;
    }

    public void ApplyDerived(DerivedStats s)
    {
        Ap = s.TotalHit;
        Ac = s.TotalAc;
        statBonus[0] = s.StrBonus; statBonus[1] = s.StaBonus; statBonus[2] = s.DexBonus;
        statBonus[3] = s.IntBonus; statBonus[4] = s.ChaBonus;
        SetMaxWeight(s.MaxWeight);
        resist[0] = s.FireR; resist[1] = s.ColdR; resist[2] = s.LightningR;
        resist[3] = s.MagicR; resist[4] = s.DiseaseR; resist[5] = s.PoisonR;
    }

    public void ApplyExp(long exp) => Exp = exp;

    public int? MonthlyNp { get; private set; }

    public void ApplyLoyalty(int np, int monthly)
    {
        Np = np;
        MonthlyNp = monthly;
    }

    public void SetNp(int np) => Np = np;

    public int Manner { get; private set; }

    public void SetManner(int manner) => Manner = Math.Max(0, manner);

    public void SetGold(int total) => Gold = Math.Max(0, total);

    public void Spend(long amount)
    {
        if (amount <= 0) return;
        Gold = (int)Math.Max(0L, Gold - amount);
    }

    public void Receive(long amount)
    {
        if (amount <= 0) return;
        Gold = (int)Math.Min(int.MaxValue, Gold + amount);
    }
}
