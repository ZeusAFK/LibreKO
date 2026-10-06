using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class GenderChangeTests
{
    [Theory]
    [InlineData(105, new[] { 1 })]
    [InlineData(108, new[] { 2 })]
    [InlineData(110, new[] { 3, 4 })]
    [InlineData(111, new[] { 2, 4 })]
    [InlineData(114, new[] { 6 })]
    [InlineData(205, new[] { 11, 12, 13 })]
    [InlineData(207, new[] { 12, 13 })]
    [InlineData(212, new[] { 12, 13 })]
    [InlineData(215, new[] { 14 })]
    public void AClassMayTakeTheRacesItCouldStartAs(int classCode, int[] races) =>
        Assert.Equal(races, GenderChange.AllowedRaces(classCode));

    [Theory]
    [InlineData(101, false)]
    [InlineData(102, false)]
    [InlineData(104, true)]
    [InlineData(209, true)]
    [InlineData(113, false)]
    public void OnlyAClassWithASecondRaceCanChange(int classCode, bool canChange) =>
        Assert.Equal(canChange, GenderChange.CanChange(classCode));
}
