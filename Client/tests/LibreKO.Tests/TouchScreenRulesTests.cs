using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class TouchScreenRulesTests
{
    private const int TabletShortSide = 1668;
    private const int UnknownShortSide = 0;

    [Fact]
    public void ThePhoneTheLayoutIsDrawnForKeepsItsSize() =>
        Assert.Equal(1f, TouchScreenRules.PhoneScale((int)TouchScreenRules.PhoneShortSide));

    [Fact]
    public void ATabletEnlargesTheLayoutByItsShortSide() =>
        Assert.Equal(TabletShortSide / TouchScreenRules.PhoneShortSide, TouchScreenRules.PhoneScale(TabletShortSide));

    [Fact]
    public void AScreenOfUnknownSizeKeepsThePhoneSize() =>
        Assert.Equal(1f, TouchScreenRules.PhoneScale(UnknownShortSide));
}
