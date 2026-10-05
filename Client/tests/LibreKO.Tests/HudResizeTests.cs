using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class HudResizeTests
{
    private static readonly Vector2 Screen = new(1600, 900);
    private static readonly Rect2 Start = new(12, 650, 694, 238);

    [Fact]
    public void GrowingFromTheTopMayReachTheTopOfTheScreen() =>
        Assert.Equal(Start.End.Y, HudResize.Limit(Screen, new Vector2(12, 600), Start, fromLeft: false, fromTop: true).Y);

    [Fact]
    public void GrowingFromTheLeftMayReachTheLeftOfTheScreen() =>
        Assert.Equal(Start.End.X, HudResize.Limit(Screen, new Vector2(2, 650), Start, fromLeft: true, fromTop: false).X);

    [Fact]
    public void GrowingDownOrRightStopsAtTheScreenEdge()
    {
        var limit = HudResize.Limit(Screen, Start.Position, Start, fromLeft: false, fromTop: false);
        Assert.Equal(new Vector2(Screen.X - Start.Position.X, Screen.Y - Start.Position.Y), limit);
    }
}
