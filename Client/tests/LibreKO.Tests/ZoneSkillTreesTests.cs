using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ZoneSkillTreesTests
{
    [Theory]
    [InlineData(89, 3500, 0, true)]
    [InlineData(89, 3000, 0, true)]
    [InlineData(89, 105, 0, false)]
    [InlineData(37, 3100, 0, true)]
    [InlineData(76, 3100, 0, true)]
    [InlineData(39, 105, 0, false)]
    [InlineData(45, 105, 490800, true)]
    [InlineData(45, 105, 490827, true)]
    [InlineData(45, 105, 490828, false)]
    [InlineData(57, 3200, 0, true)]
    [InlineData(60, 105, 0, false)]
    [InlineData(21, 105, 108010, true)]
    public void ZonesKeepToTheirSkillTrees(int zone, int tree, int skillId, bool allowed)
    {
        Assert.Equal(allowed, ZoneSkillTrees.Allowed(zone, tree, skillId));
    }
}
