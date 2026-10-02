using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NationRankAuraTests
{
    [Theory]
    [InlineData(1, NationRankAura.GoldFxId)]
    [InlineData(2, NationRankAura.SilverFxId)]
    [InlineData(4, NationRankAura.SilverFxId)]
    [InlineData(5, NationRankAura.MirageFxId)]
    [InlineData(10, NationRankAura.MirageFxId)]
    [InlineData(11, NationRankAura.NoFx)]
    [InlineData(0, NationRankAura.NoFx)]
    [InlineData(NationRankAura.Unranked, NationRankAura.NoFx)]
    public void TheMonthlyPlacePicksTheAuraTier(int place, int fxId) =>
        Assert.Equal(fxId, NationRankAura.FxIdFor(place));

    [Theory]
    [InlineData(21, false)]
    [InlineData(29, true)]
    [InlineData(76, true)]
    [InlineData(89, true)]
    public void SomeZonesHideTheAura(int zone, bool hidden) =>
        Assert.Equal(hidden, NationRankAura.HiddenIn(zone));
}
