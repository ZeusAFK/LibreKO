using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class UpgradePreviewGateTests
{
    private const int Origin = 156210008;
    private const int Scroll = 379021000;
    private const int OtherScroll = 379205000;
    private const int OtherOrigin = 156210009;
    private const int OriginPosition = 0;
    private const int ScrollPosition = 1;
    private const int OtherScrollPosition = 2;
    private const int Empty = 0;
    private const int NoPosition = -1;

    [Fact]
    public void OnlyOnePreviewIsSentUntilItsReplyArrives()
    {
        var gate = new UpgradePreviewGate();
        Assert.True(gate.Begin([Origin, Scroll], [OriginPosition, ScrollPosition]));
        Assert.False(gate.Begin([Origin, OtherScroll], [OriginPosition, OtherScrollPosition]));
        Assert.True(gate.Pending);
        Assert.False(gate.Complete([Origin, OtherScroll], [OriginPosition, OtherScrollPosition]));
        Assert.False(gate.Pending);
        Assert.True(gate.Begin([Origin, OtherScroll], [OriginPosition, OtherScrollPosition]));
        Assert.True(gate.Complete([Origin, OtherScroll], [OriginPosition, OtherScrollPosition]));
    }

    [Fact]
    public void EditingTheSelectionOrClosingRejectsThePreviousReply()
    {
        var gate = new UpgradePreviewGate();
        int[] items = [Origin, Scroll], positions = [OriginPosition, ScrollPosition];
        gate.Begin(items, positions);
        items[0] = OtherOrigin;
        Assert.False(gate.Complete(items, positions));
        gate.Begin(items, positions);
        Assert.False(gate.Complete([Empty, Empty], [NoPosition, NoPosition]));
        Assert.False(gate.Complete(items, positions));
    }
}
