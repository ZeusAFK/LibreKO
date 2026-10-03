using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class WindowPlacementTests
{
    private static readonly Vector2 Screen = new(1280, 720);

    [Fact]
    public void AWindowOpensInTheMiddleOfTheScreen()
    {
        Assert.Equal(new Vector2(440, 210), WindowPlacement.Centre(Screen, new Vector2(400, 300)));
    }

    [Fact]
    public void AnOddLeftoverIsRoundedDownToAWholePixel()
    {
        Assert.Equal(new Vector2(439, 209), WindowPlacement.Centre(Screen, new Vector2(401, 301)));
    }

    [Fact]
    public void AWindowTallerThanTheScreenKeepsTheMarginAtTheTop()
    {
        Assert.Equal(new Vector2(440, WindowPlacement.CentreMargin), WindowPlacement.Centre(Screen, new Vector2(400, 900)));
    }

    [Fact]
    public void AWindowThatAlmostFillsTheScreenStaysClearOfBothEdges()
    {
        Assert.Equal(new Vector2(WindowPlacement.CentreMargin, 10), WindowPlacement.Centre(Screen, new Vector2(1270, 700)));
    }

    private const float BottomInset = 38f;
    private static readonly Rect2 TargetFrame = new(520, 10, 240, 48);
    private static readonly Rect2 Clock = new(1180, 12, 88, 24);

    [Fact]
    public void TheFirstPlateSitsNearTheTopInTheMiddle()
    {
        var spots = WindowPlacement.TopCentreStack(Screen, new[] { new Vector2(300, 60) }, new[] { Clock }, BottomInset);
        Assert.Equal(new Vector2(490, WindowPlacement.PlateTop), spots[0]);
    }

    [Fact]
    public void ThePlateGoesUnderTheTargetFrame()
    {
        var spots = WindowPlacement.TopCentreStack(Screen, new[] { new Vector2(300, 60) }, new[] { TargetFrame, Clock }, BottomInset);
        Assert.Equal(new Vector2(490, 64), spots[0]);
    }

    [Fact]
    public void LaterPlatesStackUnderTheEarlierOnes()
    {
        var spots = WindowPlacement.TopCentreStack(Screen,
            new[] { new Vector2(300, 60), new Vector2(200, 40) }, new[] { TargetFrame }, BottomInset);
        Assert.Equal(new Vector2(490, 64), spots[0]);
        Assert.Equal(new Vector2(540, 130), spots[1]);
    }

    [Fact]
    public void ARectLowerThanTheTopBandDoesNotPushThePlate()
    {
        var lowRect = new Rect2(500, 50, 300, 40);
        var spots = WindowPlacement.TopCentreStack(Screen, new[] { new Vector2(300, 60) }, new[] { lowRect }, BottomInset);
        Assert.Equal(WindowPlacement.PlateTop, spots[0].Y);
    }

    [Fact]
    public void APlateThatWouldPassTheBottomIsLifted()
    {
        var spots = WindowPlacement.TopCentreStack(new Vector2(1280, 200),
            new[] { new Vector2(300, 100), new Vector2(300, 100) }, new Rect2[0], BottomInset);
        Assert.Equal(WindowPlacement.PlateTop, spots[0].Y);
        Assert.Equal(200 - BottomInset - 100, spots[1].Y);
    }
}
