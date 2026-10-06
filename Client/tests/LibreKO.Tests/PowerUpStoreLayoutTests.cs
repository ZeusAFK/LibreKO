using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PowerUpStoreLayoutTests
{
    [Fact]
    public void OnTouchTheStoreFillsTheScreen() =>
        Assert.Equal(new Vector2(1500, 675), PowerUpStoreLayout.WindowSize(new Vector2(1500, 675), touch: true));

    [Fact]
    public void OnALargeScreenTheStoreTakesMostOfIt() =>
        Assert.Equal(new Vector2(1360, 864), PowerUpStoreLayout.WindowSize(new Vector2(1920, 1080), touch: false));

    [Fact]
    public void OnAMidScreenTheStoreKeepsItsMinimum() =>
        Assert.Equal(PowerUpStoreLayout.PointerMinimum, PowerUpStoreLayout.WindowSize(new Vector2(1024, 700), touch: false));

    [Fact]
    public void OnASmallScreenTheStoreNeverOverflows() =>
        Assert.Equal(new Vector2(768, 544), PowerUpStoreLayout.WindowSize(new Vector2(800, 576), touch: false));

    [Fact]
    public void TheGridFitsAsManyCardsAsTheWidthAllows()
    {
        Assert.Equal(5, PowerUpStoreLayout.Columns(820, 150, 10));
        Assert.Equal(4, PowerUpStoreLayout.Columns(789, 150, 10));
        Assert.Equal(1, PowerUpStoreLayout.Columns(90, 150, 10));
    }
}
