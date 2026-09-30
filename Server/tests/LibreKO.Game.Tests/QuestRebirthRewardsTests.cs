using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestRebirthRewardsTests
{
    private const int Dason = 31743;
    private const int RonarkLand = 71;
    private const int Karus = 1;
    private const int Daily = 1631;
    private const int DelicateGift = 931685000;
    private const int ShiningGift = 931684000;
    private const int GloriousGift = 931683000;

    private const string Header = """
        Bind Npc 31743 Zone 71
        Quest 1631 "Ones Who Failed Rebirth"
            Daily
            Journal "Help the souls rest in peace."
            Kill 3 of 10024

        Requires player rebirth level >= 1

        """;

    private const string Tiered = Header + """
        Rewards for rebirth 1 to 5
            Give 1 of 931685000

        Rewards for rebirth 6 to 9
            Give 1 of 931684000

        Rewards for rebirth 10 to 15
            Give 1 of 931683000
        """;

    private static QuestCompilation Compile(string source) => QuestCompilation.Create(source, "31743_71_1631.quest");

    private static QuestProgram Program(string source)
    {
        var compilation = Compile(source);
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose("dason", Dason, RonarkLand, [compilation.Program]);
    }

    private static IEnumerable<int> Given(QuestRewards rewards) =>
        rewards.Transfers.Where(a => a.Kind == QuestActionKind.GiveItem).Select(a => a.Arguments.GetInt("item"));

    [Theory]
    [InlineData(1, DelicateGift)]
    [InlineData(5, DelicateGift)]
    [InlineData(6, ShiningGift)]
    [InlineData(9, ShiningGift)]
    [InlineData(10, GloriousGift)]
    [InlineData(15, GloriousGift)]
    public void TheBoxFollowsTheRebirthLevel(int rebirthLevel, int box)
    {
        Given(Program(Tiered).RewardsFor(Daily, 0, Karus, rebirthLevel)!).Should().Equal(box);
    }

    [Theory]
    [InlineData(0, DelicateGift)]
    [InlineData(40, GloriousGift)]
    public void ALevelOutsideEveryRangeTakesTheNearestTier(int rebirthLevel, int box)
    {
        Given(Program(Tiered).RewardsFor(Daily, 0, Karus, rebirthLevel)!).Should().Equal(box);
    }

    [Fact]
    public void AnUnscopedQuestIgnoresTheRebirthLevel()
    {
        var program = Program(Header + "Rewards\n    Give 1 of 931685000\n");
        Given(program.RewardsFor(Daily, 0, Karus, 12)!).Should().Equal(DelicateGift);
    }

    [Theory]
    [InlineData("Rewards for rebirth 1 to 5\n    Give 1 of 931685000\n\nRewards for rebirth 5 to 9\n    Give 1 of 931684000\n", "without gaps or overlaps")]
    [InlineData("Rewards for rebirth 1 to 5\n    Give 1 of 931685000\n\nRewards for rebirth 7 to 9\n    Give 1 of 931684000\n", "without gaps or overlaps")]
    [InlineData("Rewards for rebirth 5 to 1\n    Give 1 of 931685000\n", "lowest level first")]
    [InlineData("Rewards for rebirth 1 to 5\n    Give 1 of 931685000\n\nRewards for karus\n    Give 1 of 931684000\n", "same kind of scope")]
    [InlineData("Rewards for rebirth 1\n    Give 1 of 931685000\n", "lowest and the highest rebirth level")]
    public void RangesMustBeOrderedConsecutiveAndUnmixed(string rewards, string diagnostic)
    {
        var compilation = Compile(Header + rewards);
        compilation.Succeeded.Should().BeFalse();
        compilation.RenderDiagnostics().Should().Contain(diagnostic);
    }
}
