namespace LibreKO.Session;

public readonly record struct SelfHpChange(int Hp, int MaxHp, int Damage, int Recovered, int AttackerId);

public readonly record struct VitalChange(int Value, int Max, int Delta);

public readonly record struct GoldChange(int Total, int Delta, bool Transfer);

public readonly record struct ExpChange(long Exp, long Delta);

public readonly record struct LevelChange(int Level, int PreviousLevel, long Exp, long PreviousExp, long MaxExp, long PreviousMaxExp)
{
    public bool LeveledUp => Level > PreviousLevel;
}

public readonly record struct LoyaltyChange(int Np, int Delta, int MonthlyDelta);

public readonly record struct MannerChange(int Manner, int Delta);
