namespace LibreKO.Game.World;

public static class NestBalance
{
    private const int CurveLevel = 40;

    private readonly record struct StatCurve(double AtCurveLevel, double GrowthPerLevel)
    {
        public double At(int level) => AtCurveLevel * Math.Pow(GrowthPerLevel, level - CurveLevel);
    }

    private static readonly StatCurve Hp = new(2171, 1.0565);
    private static readonly StatCurve Attack = new(187, 1.0615);
    private static readonly StatCurve Ac = new(269, 1.0196);
    private static readonly StatCurve HitRate = new(47, 1.0513);
    private static readonly StatCurve EvadeRate = new(102, 1.0399);
    private static readonly StatCurve Experience = new(164937, 1.039);
    private static readonly StatCurve Gold = new(235, 1.0192);

    public const int BossLevelLead = 3;
    public const double BossBulk = 8;
    public const double BossPower = 1.5;
    public const double BossGuard = 1.25;
    public const double MinOwnRatio = 0.75;
    public const double MaxOwnRatio = 1.25;

    public static void Apply(NpcInstance npc, int familyLevel, bool isBoss)
    {
        if (!npc.IsMonster || npc.Attack1 <= 0 || familyLevel <= 0)
            return;

        var own = Math.Max(1, (int)npc.Level);
        var level = isBoss ? familyLevel + BossLevelLead : familyLevel;

        npc.Level = (short)level;
        npc.MaxHp = npc.Hp = (int)Stat(Hp, npc.MaxHp, own, level, isBoss ? BossBulk : 0);
        npc.Attack1 = ToShort(Stat(Attack, npc.Attack1, own, level, isBoss ? BossPower : 0));
        npc.Ac = ToShort(Stat(Ac, npc.Ac, own, level, isBoss ? BossGuard : 0));
        npc.HitRate = ToShort(Stat(HitRate, npc.HitRate, own, level, isBoss ? BossGuard : 0));
        npc.EvadeRate = ToShort(Stat(EvadeRate, npc.EvadeRate, own, level, isBoss ? 1 : 0));
        npc.Experience = Reward(Experience, npc.Experience, own, level, isBoss);
        npc.GoldDrop = Reward(Gold, npc.GoldDrop, own, level, isBoss);
    }

    private static int Reward(StatCurve curve, int value, int ownLevel, int level, bool isBoss) =>
        value <= 0 ? value : (int)Stat(curve, value, ownLevel, level, isBoss ? BossBulk : 0);

    private static double Stat(StatCurve curve, int value, int ownLevel, int level, double bossFactor) =>
        bossFactor > 0
            ? curve.At(level) * bossFactor
            : curve.At(level) * Math.Clamp(value / curve.At(ownLevel), MinOwnRatio, MaxOwnRatio);

    private static short ToShort(double value) => (short)Math.Clamp(value, 1, short.MaxValue);
}
