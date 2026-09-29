using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class MoveInputTests
{
    [Theory]
    [InlineData(MoveKeys.None, MoveKeys.TurnLeft)]
    [InlineData(MoveKeys.Forward, MoveKeys.Forward | MoveKeys.TurnRight)]
    [InlineData(MoveKeys.Forward | MoveKeys.TurnLeft, MoveKeys.Forward | MoveKeys.TurnRight)]
    public void TurningIsNotANewMove(MoveKeys previous, MoveKeys current)
    {
        Assert.True(MoveInput.AnyKeyPressed(previous, current));
        Assert.False(MoveInput.WalkKeyPressed(previous, current));
    }

    [Theory]
    [InlineData(MoveKeys.None, MoveKeys.Forward)]
    [InlineData(MoveKeys.TurnLeft, MoveKeys.TurnLeft | MoveKeys.Backward)]
    [InlineData(MoveKeys.Forward, MoveKeys.Forward | MoveKeys.Backward)]
    public void PressingForwardOrBackwardIsANewMove(MoveKeys previous, MoveKeys current)
    {
        Assert.True(MoveInput.WalkKeyPressed(previous, current));
    }

    [Theory]
    [InlineData(MoveKeys.Forward, MoveKeys.Forward)]
    [InlineData(MoveKeys.Forward | MoveKeys.TurnLeft, MoveKeys.Forward)]
    [InlineData(MoveKeys.Forward, MoveKeys.None)]
    public void HoldingOrReleasingPressesNothing(MoveKeys previous, MoveKeys current)
    {
        Assert.False(MoveInput.AnyKeyPressed(previous, current));
        Assert.False(MoveInput.WalkKeyPressed(previous, current));
    }
}
