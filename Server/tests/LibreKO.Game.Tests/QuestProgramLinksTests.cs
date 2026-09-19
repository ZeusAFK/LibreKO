using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestProgramLinksTests
{
    private static QuestProgram Compile(string file, string code)
    {
        var result = QuestCompilation.Create(code, file);
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        return result.Program;
    }

    [Fact]
    public void BorrowingUsesTheNamedFileEvenWhenAnotherFileAnswersTheSameName()
    {
        var unrelated = Compile("a.quest", "Bind Npc 100\nOn payout\n    Give 5 coins");
        var owner = Compile("z.quest", "Bind Npc 100\nOn payout\n    Give 23 coins");
        var caller = Compile("caller.quest", """
            Bind Npc 100 Zone 21
            Quest 61
            payout = event from z
            On accept
                Goto payout
            """);
        var program = QuestProgramComposer.Compose("combined", 100, 21, [unrelated, caller, owner]);
        var host = Substitute.For<IQuestHost>();
        program.TryGetEntry(QuestProgram.AcceptEvent, 61, out var id).Should().BeTrue();
        new QuestInterpreter(program, host).Run(id).Failure.Should().BeNull();
        host.Received(1).GiveGold(23);
        host.DidNotReceive().GiveGold(5);
    }

    [Theory]
    [InlineData("Bind Npc 200")]
    [InlineData("Bind Npc 100 Zone 22")]
    public void ASameNamedEventAtTheRightNpcDoesNotValidateBorrowingFromTheWrongFile(string binding)
    {
        var unrelated = Compile("a.quest", "Bind Npc 100\nOn payout\n    Give 5 coins");
        var owner = Compile("z.quest", binding + "\nOn payout\n    Give 23 coins");
        var caller = Compile("caller.quest", """
            Bind Npc 100 Zone 21
            Quest 61
            payout = event from z
            On accept
                Goto payout
            """);
        QuestProgramLinks.Resolve(caller, caller.Borrowed.Single(), [unrelated, caller, owner], out var problem)
            .Should().BeNull();
        problem.Should().Contain("not one of NPC 100's files");
    }

    [Fact]
    public void ReachabilityFollowsTransactionsAndBorrowingButDoesNotMarkAnUnenteredCycleLive()
    {
        var caller = Compile("caller.quest", """
            Bind Npc 100 Zone 21
            Quest 61
            payout = event from shared
            On accept
                Transaction
                    Give 1 coins
                    Goto payout
            """);
        var shared = Compile("shared.quest", "Bind Npc 100\nOn payout\n    Goto finish\nOn finish\n    Give 23 coins");
        var dead = Compile("dead.quest", "Bind Npc 100\nOn first\n    Goto second\nOn second\n    Goto first");
        var reached = QuestProgramLinks.Reachable([caller, shared, dead]);
        reached.Count(node => node.Program == shared).Should().Be(2);
        reached.Should().NotContain(node => node.Program == dead);
    }

    [Fact]
    public void TransactionsKeepTheirOwnRewardPoolLocationAndContinuationAfterComposition()
    {
        var unrelated = Compile("a.quest", """
            point = location "Wrong"
            RewardPool prize
                111
            Bind Npc 100
            On unused
                Give 5 coins
            """);
        var caller = Compile("caller.quest", """
            point = location "Right"
            RewardPool prize
                222
            Bind Npc 100
            Quest 61
            On accept
                Transaction
                    Give random from prize
                    Map point
                    Goto finish
            On finish
                Give 23 coins
            """);
        var program = QuestProgramComposer.Compose("combined", 100, 21, [unrelated, caller]);
        var host = Substitute.For<IQuestHost>();
        program.TryGetEntry(QuestProgram.AcceptEvent, 61, out var id).Should().BeTrue();
        new QuestInterpreter(program, host).Run(id).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 1 && actions[0].Kind == QuestActionKind.GiveItem
            && actions[0].Arguments.GetInt("item") == 222));
        host.Received(1).ShowLocation(Arg.Is<QuestLocation>(location => location.Title == "Right"), Arg.Any<int>());
        host.Received(1).GiveGold(23);
    }
}
