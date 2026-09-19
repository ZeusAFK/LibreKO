using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Common.Tests;

public class SetItemClassBonusTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void TheFourJobGroupsCount(short classType, bool expected)
    {
        SetItemData.IsJobGroup(classType).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(255)]
    public void EverythingElseIsNotAJobGroup(short classType)
    {
        SetItemData.IsJobGroup(classType).Should().BeFalse();
    }

    [Fact]
    public void TheNoneMarkerIsTheOneTheDataActuallyUses()
    {
        SetItemData.NoJobGroup.Should().Be(255);
        SetItemData.IsJobGroup(SetItemData.NoJobGroup).Should().BeFalse();
    }
}
