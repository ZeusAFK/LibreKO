using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SkillGrantTests
{
    [Theory]
    [InlineData(4110, 0, 0, false)]
    [InlineData(7106, 0, 0, false)]
    [InlineData(4110, 1, 0, true)]
    [InlineData(5110, 1, 0, false)]
    [InlineData(8110, 2, 4, true)]
    [InlineData(7110, 2, 4, false)]
    [InlineData(9110, 2, 0, true)]
    public void ANationBuffNeedsTheNationRoleThatUnlocksItsBand(int tree, int role, int line, bool granted)
    {
        Assert.Equal(granted, SkillData.IsGranted(tree, transformId: 0, role, line));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30010, false)]
    [InlineData(31501, true)]
    [InlineData(20008, true)]
    public void ACommandTreeSkillNeedsACommandForm(int transformId, bool granted)
    {
        Assert.Equal(granted, SkillData.IsGranted(1019, transformId));
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1106)]
    [InlineData(1010)]
    public void AnOrdinaryTreeNeedsNoRoleOrForm(int tree)
    {
        Assert.True(SkillData.IsGranted(tree, transformId: 0));
    }
}
