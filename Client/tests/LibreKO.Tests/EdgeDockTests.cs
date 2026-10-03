using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class EdgeDockTests
{
    private static readonly EdgeDock.Frame Screen = new(new Vector2(1280, 720), Margin: 12, BottomInset: 38);
    private static readonly Rect2 Clock = new(1180, 12, 88, 24);
    private static readonly Rect2 MiniMap = new(1068, 42, 200, 230);
    private static readonly Rect2 IconRow = new(230, 18, 44, 44);
    private static readonly Rect2 Chat = new(12, 470, 490, 238);
    private static readonly Rect2[] Hud = { Clock, MiniMap, IconRow, Chat };

    [Fact]
    public void AWindowThatFitsUnderTheMiniMapSitsAgainstTheRightEdge()
    {
        var spot = EdgeDock.Place(EdgeDock.Side.Right, new[] { new Vector2(250, 300) }, Hud, Screen);
        Assert.Equal(new Vector2(1018, 278), spot[0]);
    }

    [Fact]
    public void ATallWindowSlidesBesideTheMiniMapAndStartsUnderTheClockStrip()
    {
        var spot = EdgeDock.Place(EdgeDock.Side.Right, new[] { new Vector2(330, 420) }, Hud, Screen);
        Assert.Equal(new Vector2(732, 42), spot[0]);
    }

    [Fact]
    public void ALaterWindowStacksInwardOfTheEarlierOne()
    {
        var spots = EdgeDock.Place(EdgeDock.Side.Right,
            new[] { new Vector2(250, 300), new Vector2(250, 200) }, Hud, Screen);
        Assert.Equal(new Vector2(1018, 278), spots[0]);
        Assert.Equal(new Vector2(762, 42), spots[1]);
    }

    [Fact]
    public void ALeftWindowThatCannotClearTheChatEndsAtTheBottomLimit()
    {
        var spot = EdgeDock.Place(EdgeDock.Side.Left, new[] { new Vector2(300, 500) }, Hud, Screen);
        Assert.Equal(new Vector2(12, 170), spot[0]);
    }

    [Fact]
    public void ALeftWindowThatClearsTheChatStartsUnderTheIconRow()
    {
        var spot = EdgeDock.Place(EdgeDock.Side.Left, new[] { new Vector2(300, 380) }, Hud, Screen);
        Assert.Equal(new Vector2(12, 68), spot[0]);
    }

    [Fact]
    public void HudOnTheOtherHalfIsIgnored()
    {
        var spot = EdgeDock.Place(EdgeDock.Side.Right, new[] { new Vector2(250, 300) },
            new[] { IconRow, Chat }, Screen);
        Assert.Equal(new Vector2(1018, 12), spot[0]);
    }
}
