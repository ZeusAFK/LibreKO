using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class ClanCapeRuleTests
{
    private const byte Grade5 = 5;
    private const byte Grade3 = 3;
    private const byte Grade1 = 1;
    private const byte AnyGrade = 0;

    [Theory]
    [InlineData(ClanType.Royal1, Grade5, ClanType.Royal1, AnyGrade, true)]
    [InlineData(ClanType.Royal1, Grade5, ClanType.Promoted, Grade1, true)]
    [InlineData(ClanType.Royal5, Grade1, ClanType.Royal4, AnyGrade, false)]
    [InlineData(ClanType.Accredited5, Grade5, ClanType.Promoted, Grade3, true)]
    [InlineData(ClanType.Promoted, Grade5, ClanType.Promoted, Grade3, false)]
    [InlineData(ClanType.Promoted, Grade3, ClanType.Promoted, Grade3, true)]
    [InlineData(ClanType.Training, Grade1, ClanType.Promoted, Grade1, false)]
    [InlineData(ClanType.Training, Grade5, ClanType.None, AnyGrade, true)]
    public void TheCapeRankIsTheClanTypeAndThePointGradeCountsOnlyBelowAccredited(
        ClanType clan, byte grade, ClanType capeRank, byte capeGrade, bool allowed) =>
        ClanRules.MeetsCapeRank((byte)clan, grade, (byte)capeRank, capeGrade).Should().Be(allowed);
}
