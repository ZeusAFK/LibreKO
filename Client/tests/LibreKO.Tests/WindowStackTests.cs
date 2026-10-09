using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class WindowStackTests
{
    private static readonly Rect2 West = new(100, 100, 300, 200);
    private static readonly Rect2 East = new(250, 150, 300, 200);

    [Fact]
    public void WhereWindowsOverlapTheOneDrawnLastIsHit()
    {
        var overlap = new Vector2(300, 200);
        Rect2[] eastOnTop = { West, East };
        Rect2[] westOnTop = { East, West };
        Assert.Equal(East, eastOnTop[WindowStack.TopmostAt(eastOnTop, overlap)]);
        Assert.Equal(West, westOnTop[WindowStack.TopmostAt(westOnTop, overlap)]);
    }

    [Fact]
    public void TheUncoveredPartOfAWindowBelowStillHitsIt()
    {
        Assert.Equal(0, WindowStack.TopmostAt(new[] { West, East }, new Vector2(120, 120)));
    }

    [Fact]
    public void APointOutsideEveryWindowHitsNothing()
    {
        Assert.Equal(WindowStack.None, WindowStack.TopmostAt(new[] { West, East }, new Vector2(10, 10)));
        Assert.Equal(WindowStack.None, WindowStack.TopmostAt(new Rect2[0], new Vector2(300, 200)));
    }

    [Fact]
    public void AnIncomingWindowIsRaisedOnlyWhileNoOtherWindowIsBeingTypedInto()
    {
        Assert.True(WindowStack.RaisesIncoming(WindowStack.None, 0));
        Assert.True(WindowStack.RaisesIncoming(1, 1));
        Assert.False(WindowStack.RaisesIncoming(1, 0));
        Assert.False(WindowStack.RaisesIncoming(0, 1));
    }
}
