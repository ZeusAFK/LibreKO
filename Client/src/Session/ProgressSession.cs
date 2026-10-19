namespace LibreKO.Session;

public sealed class ProgressSession
{
    private readonly SelfSession self;

    public ProgressSession(SelfSession self) => this.self = self;

    public event Action<PointChangeReply>? PointChangeEvent;

    internal void RaisePointChange(PointChangeReply reply) => PointChangeEvent?.Invoke(reply);

    public event Action<GoldChange>? GoldChangeEvent;

    internal void ApplyGold(int total)
    {
        int before = self.Sheet.Gold;
        self.Sheet.SetGold(total);
        RaiseGold(before, transfer: false);
    }

    internal void TransferGoldOut(long amount)
    {
        int before = self.Sheet.Gold;
        self.Sheet.Spend(amount);
        RaiseGold(before, transfer: true);
    }

    internal void TransferGoldIn(long amount)
    {
        int before = self.Sheet.Gold;
        self.Sheet.Receive(amount);
        RaiseGold(before, transfer: true);
    }

    private void RaiseGold(int before, bool transfer) => GoldChangeEvent?.Invoke(new GoldChange(self.Sheet.Gold, self.Sheet.Gold - before, transfer));

    public event Action<ExpChange>? ExpChangeEvent;

    internal void ApplyExp(long exp)
    {
        long before = self.Sheet.Exp;
        self.Sheet.ApplyExp(exp);
        ExpChangeEvent?.Invoke(new ExpChange(exp, exp - before));
    }

    public event Action<int, int>? ExpJackpotEvent;

    internal void RaiseExpJackpot(int arg1, int arg2) => ExpJackpotEvent?.Invoke(arg1, arg2);

    public event Action<long>? DeathExpLossEvent;

    internal void RaiseDeathExpLoss(long value) => DeathExpLossEvent?.Invoke(value);

    public event System.Action<GoldChangeReply>? GoldChangeLogEvent;

    internal void RaiseGoldChangeLog(GoldChangeReply goldChangeReply) => GoldChangeLogEvent?.Invoke(goldChangeReply);

    public event Action<LevelChange>? LevelChangeEvent;

    internal void ApplyLevel(int level, int statPoints, int skillPool, long maxExp, long exp, int maxHp, int hp, int maxMp, int mp)
    {
        var sheet = self.Sheet;
        var change = new LevelChange(level, sheet.Level, exp, sheet.Exp, maxExp, sheet.MaxExp);
        sheet.ApplyLevel(level, statPoints, exp, maxExp);
        self.Vitals.Seed(hp, maxHp, mp, maxMp);
        self.Mastery.SetPool(skillPool);
        LevelChangeEvent?.Invoke(change);
    }

    public event Action<int, int>? PeerLevelChangeEvent;

    internal void RaisePeerLevelChange(int arg1, int arg2) => PeerLevelChangeEvent?.Invoke(arg1, arg2);

    public event Action<LoyaltyChange>? LoyaltyChangeEvent;

    internal void ApplyLoyalty(int np, int? monthly)
    {
        var sheet = self.Sheet;
        int before = sheet.Np;
        int? monthlyBefore = sheet.MonthlyNp;
        if (monthly is { } total) sheet.ApplyLoyalty(np, total);
        else sheet.SetNp(np);
        int monthlyDelta = monthlyBefore is { } was && sheet.MonthlyNp is { } now ? now - was : 0;
        LoyaltyChangeEvent?.Invoke(new LoyaltyChange(np, np - before, monthlyDelta));
    }

    public event Action<MannerChange>? MannerChangeEvent;

    internal void ApplyManner(int manner)
    {
        int before = self.Sheet.Manner;
        self.Sheet.SetManner(manner);
        MannerChangeEvent?.Invoke(new MannerChange(self.Sheet.Manner, self.Sheet.Manner - before));
    }

    public event Action<int>? WeightChangeEvent;

    internal void RaiseWeightChange(int value) => WeightChangeEvent?.Invoke(value);

    public event Action<int, int, int>? StateChangeEvent;

    internal void RaiseStateChange(int arg1, int arg2, int arg3) => StateChangeEvent?.Invoke(arg1, arg2, arg3);

    public int SelfTransformSkill { get; private set; }

    public int SelfAbnormal { get; private set; }

    public const int NoExpeditionSide = -1;

    public int SelfExpeditionSide { get; private set; } = NoExpeditionSide;

    public bool SelfDevilForm { get; private set; }

    internal void ApplyOwnStateChange(int type, int value)
    {
        if (type == StateChange.Transformation) SelfTransformSkill = value;
        if (type == StateChange.Abnormal) SelfAbnormal = value;
        if (type == StateChange.ExpeditionSide) SelfExpeditionSide = value;
        if (type == StateChange.DevilForm) SelfDevilForm = value != 0;
    }

    internal void ResetOwnState()
    {
        SelfTransformSkill = 0;
        SelfAbnormal = 0;
        SelfExpeditionSide = NoExpeditionSide;
        SelfDevilForm = false;
    }
}
