using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestSkillPointTests
{
    private const int HealTree = 5;

    [Fact]
    public void TheTierGateReadsTheTreeTheScriptNames()
    {
        var host = Substitute.For<IQuestHost>();
        host.SkillPoints(HealTree).Returns(60);

        Run(host).Should().Be("high");
        host.Received().SkillPoints(HealTree);
    }

    [Fact]
    public void ALowerTotalFallsToTheNextRungDown()
    {
        var host = Substitute.For<IQuestHost>();
        host.SkillPoints(HealTree).Returns(45);

        Run(host).Should().Be("low");
    }

    [Fact]
    public void NoPointsInThatTreeMissesEveryRung()
    {
        var host = Substitute.For<IQuestHost>();
        host.SkillPoints(HealTree).Returns(0);

        Run(host).Should().Be("none");
    }

    [Fact]
    public void TheTreeIsPartOfTheConditionSoAnotherTreeDoesNotOpenTheGate()
    {
        var host = Substitute.For<IQuestHost>();
        host.SkillPoints(HealTree).Returns(0);
        host.SkillPoints(6).Returns(99);

        Run(host).Should().Be("none");
    }

    private static string Run(IQuestHost host)
    {
        var result = QuestCompilation.Create($"""
            Bind Npc 11810

            On greeting
                If player skill points in tree {HealTree} > 59
                    Say "high"
                Else if player skill points in tree {HealTree} > 39
                    Say "low"
                Else
                    Say "none"
            """, "skillpoints.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        result.Program.TryGetGreeting(out var entry).Should().BeTrue();

        string? said = null;
        host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(),
                Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(call => said ??= call.Arg<DialogLine>().Text);

        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();
        return said ?? "";
    }
}
