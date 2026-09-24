using System.Collections.Generic;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class CooldownCueTests
{
    private const int Nova = 110560;
    private const int Slash = 105505;
    private const int Unbarred = 110501;

    private static readonly int[] Hotbar = [Nova, Slash, 0, 0];

    private static float Recast(int skillId) => skillId == Slash ? 1f : 10f;

    [Fact]
    public void ASkillWhoseCooldownEndedSinceTheLastTickIsCued()
    {
        var readyAt = new Dictionary<int, double> { [Nova] = 100.5 };
        var cued = new List<int>();
        CooldownCue.Collect(readyAt, 100.0, 101.0, Hotbar, Recast, cued);
        Assert.Equal([Nova], cued);
    }

    [Fact]
    public void ACooldownThatEndedEarlierOrLaterIsNotCuedAgain()
    {
        var readyAt = new Dictionary<int, double> { [Nova] = 99.0 };
        var cued = new List<int>();
        CooldownCue.Collect(readyAt, 100.0, 101.0, Hotbar, Recast, cued);
        Assert.Empty(cued);

        readyAt[Nova] = 102.0;
        CooldownCue.Collect(readyAt, 100.0, 101.0, Hotbar, Recast, cued);
        Assert.Empty(cued);
    }

    [Fact]
    public void ShortCooldownsAndSkillsOffTheBarStaySilent()
    {
        var readyAt = new Dictionary<int, double> { [Slash] = 100.5, [Unbarred] = 100.5 };
        var cued = new List<int>();
        CooldownCue.Collect(readyAt, 100.0, 101.0, Hotbar, Recast, cued);
        Assert.Empty(cued);
    }

    [Fact]
    public void TheFirstTickAfterALoadCuesNothing()
    {
        var readyAt = new Dictionary<int, double> { [Nova] = 0.5 };
        var cued = new List<int>();
        CooldownCue.Collect(readyAt, 0, 1.0, Hotbar, Recast, cued);
        Assert.Empty(cued);
    }
}
