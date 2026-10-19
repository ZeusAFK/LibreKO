using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class SkillTimersTests
{
    private const int SkillId = 105520;
    private const int PotionSkillId = 490010;
    private const double RecastSeconds = 10.0;
    private const double Now = 1000.0;

    private static SkillTiming Skill() => new(SkillId, RecastSeconds, IsPotion: false);

    private static SkillTiming Potion() => new(PotionSkillId, 0, IsPotion: true);

    private static (SkillTimers Timers, SelfSession Self) NewTimers()
    {
        var self = new SelfSession();
        return (new SkillTimers(self), self);
    }

    [Fact]
    public void ASkillIsOnCooldownUntilItsRecastHasPassed()
    {
        var (timers, _) = NewTimers();
        var skill = Skill();

        timers.Start(skill, Now);

        Assert.True(timers.OnCooldown(skill, Now + RecastSeconds - 0.1));
        Assert.False(timers.OnCooldown(skill, Now + RecastSeconds));
        Assert.Equal(Now + RecastSeconds, timers.ReadyAt(SkillId));
    }

    [Fact]
    public void TheFractionShrinksFromOneToZero()
    {
        var (timers, _) = NewTimers();
        var skill = Skill();

        timers.Start(skill, Now);

        Assert.Equal(1f, timers.Fraction(skill, Now), 3);
        Assert.Equal(0.5f, timers.Fraction(skill, Now + RecastSeconds / 2), 3);
        Assert.Equal(0f, timers.Fraction(skill, Now + RecastSeconds), 3);
    }

    [Fact]
    public void EnsureKeepsARunningCooldown()
    {
        var (timers, _) = NewTimers();
        var skill = Skill();

        timers.Start(skill, Now);
        timers.Ensure(skill, Now + 5);

        Assert.Equal(Now + RecastSeconds, timers.ReadyAt(SkillId));
    }

    [Fact]
    public void CancelClearsTheCooldown()
    {
        var (timers, _) = NewTimers();
        var skill = Skill();

        timers.Start(skill, Now);
        timers.Cancel(skill);

        Assert.False(timers.OnCooldown(skill, Now));
        Assert.Equal(0, timers.ReadyAt(SkillId));
    }

    [Fact]
    public void ASkillWithoutRecastNeverCoolsDown()
    {
        var (timers, _) = NewTimers();
        var skill = new SkillTiming(SkillId, 0, IsPotion: false);

        timers.Start(skill, Now);

        Assert.False(timers.OnCooldown(skill, Now));
        Assert.Equal(0f, timers.Fraction(skill, Now));
    }

    [Fact]
    public void PotionsShareOneTimer()
    {
        var (timers, self) = NewTimers();
        var potion = Potion();

        timers.Start(potion, Now);

        Assert.Equal(Now + SkillTimers.PotionCooldownSeconds, self.PotionReadyAt);
        Assert.True(timers.OnCooldown(potion, Now + 1));
        Assert.False(timers.OnCooldown(potion, Now + SkillTimers.PotionCooldownSeconds));
        Assert.Empty(self.SkillCooldowns);
    }
}
