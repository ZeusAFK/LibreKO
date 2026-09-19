using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestInterpreterTests
{
    private static QuestExecutionResult Run(string body, IQuestHost host, int reward = -1)
    {
        var compilation = QuestCompilation.Create("Bind Npc 100\nOn greeting\n" + body, "test.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        compilation.Program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var id).Should().BeTrue();
        return new QuestInterpreter(compilation.Program, host, reward).Run(id);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 30)]
    public void RollDispatchRunsOneArm(int roll, int gold)
    {
        var host = Substitute.For<IQuestHost>();
        host.RollDice(2).Returns(roll);
        Run("""
                By roll of 3
                    0
                        Give 10 coins
                    1
                        Give 20 coins
                    Else
                        Give 30 coins
            """, host).Failure.Should().BeNull();
        host.Received(1).RollDice(2);
        host.Received(1).GiveGold(gold);
        host.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IQuestHost.GiveGold))
            .Should().Be(1);
    }

    [Fact]
    public void TheRenameAndStatPanelsAreSeparatePhrases()
    {
        var host = Substitute.For<IQuestHost>();
        Run("    Open rename panel\n", host).Failure.Should().BeNull();
        host.Received(1).OpenRenamePanel();
        host.DidNotReceive().OpenStatSkillPanel();

        var other = Substitute.For<IQuestHost>();
        Run("    Open stats panel\n", other).Failure.Should().BeNull();
        other.Received(1).OpenStatSkillPanel();
        other.DidNotReceive().OpenRenamePanel();
    }

    [Fact]
    public void ResettingStatsAndSkillsAreSeparatePhrases()
    {
        var host = Substitute.For<IQuestHost>();
        Run("    Reset stats\n    Reset skills\n", host).Failure.Should().BeNull();
        host.Received(1).ResetStatPoints();
        host.Received(1).ResetSkillPoints();
    }

    [Fact]
    public void TodoIsInertAndDoesNotInterruptTheFollowingAction()
    {
        var host = Substitute.For<IQuestHost>();
        Run("    Todo \"Review this\"\n    Give 1 coins\n", host).Failure.Should().BeNull();
        host.DidNotReceiveWithAnyArgs().Unsupported(default!);
        host.Received().GiveGold(1);
    }

    [Fact]
    public void AnIntentionalEmptyArmIsNotAnIncompleteConversion()
    {
        var compilation = QuestCompilation.Create("Bind Npc 100\nOn greeting\n    If player level == 1\n        Do nothing\n    Else\n        Give 5 coins\n    Give 1 coins\n", "empty.quest");
        compilation.Diagnostics.Should().BeEmpty();
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(1);
        compilation.Program.TryGetGreeting(out var entry);
        new QuestInterpreter(compilation.Program, host).Run(entry);
        host.Received().GiveGold(1);
        host.DidNotReceive().GiveGold(5);
    }

    [Fact]
    public void QuestExchangesPreserveTheChosenRewardAndExplicitOverrides()
    {
        var host = Substitute.For<IQuestHost>();
        Run("    Exchange 123 for quest\n    Exchange 456 for quest reward 1\n", host, 3);
        host.Received().RunQuestExchange(123, 3);
        host.Received().RunQuestExchange(456, 1);
        host.DidNotReceiveWithAnyArgs().RunExchange(default);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public void ClassSubtypeGatesDoNotIncludeTheWholeFamily(int subtype, bool matches)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerClassGroup.Returns(1);
        host.PlayerClassSubtype.Returns(subtype);
        Run("    If player class subtype == 6\n        Give 1 coins\n", host);
        host.Received(matches ? 1 : 0).GiveGold(1);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(36)]
    [InlineData(255)]
    public void CustomQuestStatesDoNotWrapIntoAvailableOrActiveStates(int state)
    {
        var host = Substitute.For<IQuestHost>();
        host.QuestStatus(777).Returns(state);
        Run("    If quest 777 is available or quest 777 is active\n        Give 1 coins\n", host);
        host.DidNotReceiveWithAnyArgs().GiveGold(default);
    }

    [Fact]
    public void ExcessiveConditionalMenusFailWithoutResumingTheOuterBody()
    {
        var host = Substitute.For<IQuestHost>();
        var buttons = string.Concat(Enumerable.Repeat(
            "        If player level >= 0\n            Topic \"Choose\" goto close\n", 257));
        var result = Run("    If player level >= 0\n        Say \"Choose\"\n" + buttons
            + "    Give 1 coins\n", host);
        result.Failure.Should().Contain("256 buttons");
        host.DidNotReceiveWithAnyArgs().ShowDialog(default, default, default!, default!);
        host.DidNotReceiveWithAnyArgs().GiveGold(default);
    }

    [Fact]
    public void CloseStopsEffectsAndAFalseBranchDoesNotRunItsBody()
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(1);
        Run("""
                If player level >= 10
                    Give 10 coins
                Else
                    Goto close
                Give 100 coins
            """, host);
        host.DidNotReceiveWithAnyArgs().GiveGold(default);
    }

    [Fact]
    public void NamedItemsAndQuestsResolveToTheirDeclaredIds()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 100
            spool = item 123
            hunt = quest 777
            On greeting
                If player has spool
                    Take spool
                    Start hunt
            """, "names.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        host.ItemCount(123).Returns(1);
        compilation.Program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var id);
        new QuestInterpreter(compilation.Program, host).Run(id);
        host.Received().TakeItem(123, 1);
        host.Received().SetQuestState(777, 1);
    }

    [Fact]
    public void AnUndeclaredItemNameIsACompileError()
    {
        QuestCompilation.Create("Bind Npc 100\nOn greeting\n    Give missing_item\n", "names.quest")
            .Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(9, 500, false)]
    [InlineData(10, 99, false)]
    [InlineData(10, 100, true)]
    [InlineData(11, 0, true)]
    [InlineData(10, 4000000000L, true)]
    public void ExperienceEligibilityOnlyAppliesAtTheMinimumLevel(int level, long experience, bool eligible)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(level);
        host.PlayerExperience.Returns(experience);
        Run("    If player level >= 10 and (player level > 10 or player experience >= 100)\n        Give 1 coins\n", host);
        host.Received(eligible ? 1 : 0).GiveGold(1);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("5")]
    [InlineData("4294967296")]
    public void ButtonRewardIndicesMustFitTheActualRewardColumns(string reward)
    {
        QuestCompilation.Create($"Bind Npc 100\nOn greeting\n    Say \"Choose\"\n    Topic \"Choice\" reward {reward} goto close\n", "choices.quest")
            .Succeeded.Should().BeFalse();
    }
}
