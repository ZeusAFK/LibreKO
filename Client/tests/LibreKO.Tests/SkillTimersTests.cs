using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class SkillTimersTests
{
    private const int SkillId = 105520;
    private const int PotionSkillId = 490010;
    private const int SlowPotionSkillId = 490060;
    private const double SlowPotionRecastSeconds = 2.5;
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

    [Fact]
    public void APotionWithALongerRecastKeepsItBesideTheSharedTimer()
    {
        var (timers, _) = NewTimers();
        var slowPotion = new SkillTiming(SlowPotionSkillId, SlowPotionRecastSeconds, IsPotion: true);
        var otherPotion = Potion();

        timers.Start(slowPotion, Now);

        Assert.False(timers.OnCooldown(otherPotion, Now + SkillTimers.PotionCooldownSeconds));
        Assert.True(timers.OnCooldown(slowPotion, Now + SkillTimers.PotionCooldownSeconds));
        Assert.False(timers.OnCooldown(slowPotion, Now + SlowPotionRecastSeconds));
    }

    [Fact]
    public void APotionShowsTheFullerOfTheSharedTimerAndItsOwnRecast()
    {
        var (timers, _) = NewTimers();
        var slowPotion = new SkillTiming(SlowPotionSkillId, SlowPotionRecastSeconds, IsPotion: true);

        timers.Start(Potion(), Now);

        Assert.Equal(0.5f, timers.Fraction(slowPotion, Now + SkillTimers.PotionCooldownSeconds / 2), 3);

        timers.Start(slowPotion, Now);

        Assert.Equal(0.2f, timers.Fraction(slowPotion, Now + SkillTimers.PotionCooldownSeconds), 3);
    }

    [Fact]
    public void CancellingARefusedPotionKeepsTheSharedTimer()
    {
        var (timers, self) = NewTimers();
        var slowPotion = new SkillTiming(SlowPotionSkillId, SlowPotionRecastSeconds, IsPotion: true);

        timers.Start(slowPotion, Now);
        timers.Cancel(slowPotion);

        Assert.Equal(0, timers.ReadyAt(SlowPotionSkillId));
        Assert.True(timers.OnCooldown(Potion(), Now + 1));
        Assert.Equal(Now + SkillTimers.PotionCooldownSeconds, self.PotionReadyAt);
    }
}
