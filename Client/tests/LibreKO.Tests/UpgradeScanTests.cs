using LibreKO.Features.Windows;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class UpgradeScanTests
{
    [Theory]
    [InlineData(ItemUpgradeResult.Failed, true)]
    [InlineData(ItemUpgradeResult.Succeeded, false)]
    public void OnlyAFailedUpgradeScansAsAFailure(ItemUpgradeResult code, bool fails) =>
        Assert.Equal(fails, UpgradeFeature.ScanFails(code));
}
