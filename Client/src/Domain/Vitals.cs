using System;

namespace LibreKO.Domain;

public sealed class Vitals
{
    public readonly record struct HpChange(int Damage, int Recovered);

    public int Hp { get; private set; }
    public int MaxHp { get; private set; }
    public int Mp { get; private set; }
    public int MaxMp { get; private set; }

    public bool Known => MaxHp > 0;

    public bool HasMana(int cost) => cost <= 0 || Mp >= cost;

    public bool BelowHalfHp => MaxHp > 0 && Hp < MaxHp / 2;

    public void Seed(int hp, int maxHp, int mp, int maxMp)
    {
        Hp = hp; MaxHp = maxHp; Mp = mp; MaxMp = maxMp;
    }

    public HpChange ApplyHp(int hp, int maxHp)
    {
        int old = Known ? Hp : hp;
        Hp = hp;
        MaxHp = maxHp;
        return new HpChange(Math.Max(0, old - hp), Math.Max(0, hp - old));
    }

    public int ApplyMp(int mp, int maxMp)
    {
        int old = MaxMp > 0 ? Mp : mp;
        Mp = mp;
        MaxMp = maxMp;
        return mp - old;
    }

    public void ApplyMaxima(int maxHp, int maxMp)
    {
        MaxHp = maxHp;
        MaxMp = maxMp;
        if (MaxHp > 0) Hp = Math.Min(Hp, MaxHp);
        if (MaxMp > 0) Mp = Math.Min(Mp, MaxMp);
    }

    public int RestHp()
    {
        if (MaxHp <= 0) return 0;
        int before = Hp;
        Hp = Math.Min(MaxHp, Hp + Math.Max(1, MaxHp / 24));
        return Hp - before;
    }

    public int RestMp()
    {
        if (MaxMp <= 0) return 0;
        int before = Mp;
        Mp = Math.Min(MaxMp, Mp + Math.Max(1, MaxMp / 24));
        return Mp - before;
    }
}
