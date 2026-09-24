using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class HeldSteerTests
{
    private const float Slop = 5f;

    [Fact]
    public void DraggingAfterClickingATargetStartsSteering()
    {
        var steer = new HeldSteer();
        steer.ArmAt(100, 100);
        Assert.False(steer.TryStart(buttonHeld: true, 103, 101, Slop));
        Assert.True(steer.TryStart(buttonHeld: true, 130, 100, Slop));
        Assert.False(steer.TryStart(buttonHeld: true, 160, 100, Slop));
    }

    [Fact]
    public void ReleasingTheButtonBeforeDraggingKeepsItAClick()
    {
        var steer = new HeldSteer();
        steer.ArmAt(100, 100);
        Assert.False(steer.TryStart(buttonHeld: false, 130, 100, Slop));
        Assert.False(steer.TryStart(buttonHeld: true, 160, 100, Slop));
    }

    [Fact]
    public void NothingSteersWithoutAPressOnATarget()
    {
        var steer = new HeldSteer();
        Assert.False(steer.TryStart(buttonHeld: true, 300, 300, Slop));
        steer.ArmAt(100, 100);
        steer.Disarm();
        Assert.False(steer.TryStart(buttonHeld: true, 300, 300, Slop));
    }
}
