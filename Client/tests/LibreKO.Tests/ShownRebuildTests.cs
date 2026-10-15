using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ShownRebuildTests
{
    [Fact]
    public void AChangeWhileHiddenRebuildsOnceWhenShown()
    {
        var rebuild = new ShownRebuild();

        Assert.False(rebuild.Changed(shown: false));
        Assert.False(rebuild.Changed(shown: false));

        Assert.True(rebuild.Shown());
        Assert.False(rebuild.Shown());
    }

    [Fact]
    public void AChangeWhileShownRebuildsAtOnceAndNotAgainOnShow()
    {
        var rebuild = new ShownRebuild();

        Assert.True(rebuild.Changed(shown: true));

        Assert.False(rebuild.Shown());
    }

    [Fact]
    public void AnOwnerRebuildSettlesAHiddenChange()
    {
        var rebuild = new ShownRebuild();
        rebuild.Changed(shown: false);

        rebuild.Rebuilt();

        Assert.False(rebuild.Owed);
        Assert.False(rebuild.Shown());
    }

    [Fact]
    public void ShowingWithoutAChangeDoesNotRebuild()
    {
        var rebuild = new ShownRebuild();

        Assert.False(rebuild.Shown());
    }
}
