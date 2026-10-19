namespace LibreKO.Session;

public readonly record struct SkillTiming(int Id, double RecastSeconds, bool IsPotion)
{
    public static SkillTiming Of(SkillData.Skill s) => new(s.Id, s.Recast > 0 ? s.RecastSeconds : 0, s.IsPotion);
}

public sealed class SkillTimers
{
    public const double PotionCooldownSeconds = 2.0;

    private readonly SelfSession self;

    public SkillTimers(SelfSession self) => this.self = self;

    public void Start(SkillData.Skill s, double now) => Start(SkillTiming.Of(s), now);

    public void Ensure(SkillData.Skill s, double now) => Ensure(SkillTiming.Of(s), now);

    public void Cancel(SkillData.Skill s) => Cancel(SkillTiming.Of(s));

    public bool OnCooldown(SkillData.Skill s, double now) => OnCooldown(SkillTiming.Of(s), now);

    public float Fraction(SkillData.Skill s, double now) => Fraction(SkillTiming.Of(s), now);

    public void Start(SkillTiming s, double now)
    {
        if (s.IsPotion)
        {
            self.HoldPotions(now + PotionCooldownSeconds);
            return;
        }
        if (s.RecastSeconds <= 0) return;
        self.StartCooldown(s.Id, now + s.RecastSeconds);
    }

    public void Ensure(SkillTiming s, double now)
    {
        if (OnCooldown(s, now)) return;
        Start(s, now);
    }

    public void Cancel(SkillTiming s)
    {
        if (!s.IsPotion) self.CancelCooldown(s.Id);
    }

    public bool OnCooldown(SkillTiming s, double now) =>
        s.IsPotion
            ? now < self.PotionReadyAt
            : s.RecastSeconds > 0 && self.SkillCooldowns.TryGetValue(s.Id, out double until) && now < until;

    public double ReadyAt(int skillId) => self.SkillCooldowns.TryGetValue(skillId, out double until) ? until : 0;

    public SkillData.Skill? GroupBlocker(SkillData.Skill s, double now)
    {
        if (!CooldownGroups.Shares(s.CooldownGroup)) return null;
        foreach (var (id, until) in self.SkillCooldowns)
        {
            if (id == s.Id || now >= until) continue;
            var other = SkillData.Get(id);
            if (other != null && other.CooldownGroup == s.CooldownGroup) return other;
        }
        return null;
    }

    public float Fraction(SkillTiming s, double now)
    {
        if (s.IsPotion)
            return System.Math.Clamp((float)((self.PotionReadyAt - now) / PotionCooldownSeconds), 0f, 1f);
        if (s.RecastSeconds <= 0 || !self.SkillCooldowns.TryGetValue(s.Id, out double until)) return 0f;
        double left = until - now;
        if (left <= 0) return 0f;
        return System.Math.Clamp((float)(left / s.RecastSeconds), 0f, 1f);
    }
}
