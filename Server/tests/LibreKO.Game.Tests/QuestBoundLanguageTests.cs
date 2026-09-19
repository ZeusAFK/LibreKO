using FluentAssertions;
using NSubstitute;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;

namespace LibreKO.Game.Tests;

public class QuestBoundLanguageTests
{
    private sealed class Includes(Dictionary<string, string> files) : IQuestIncludes
    {
        public bool TryRead(string path, out string text, out string fileName)
        {
            fileName = path;
            return files.TryGetValue(path, out text!);
        }
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1999, 10)]
    [InlineData(2000, 20)]
    [InlineData(9999, 20)]
    public void ForRangesSelectExactlyOneOutcomeIncludingBothEndpoints(int roll, int coins)
    {
        var result = QuestCompilation.Create("""
            Bind Zone 21
            Quest 61
            On accept
                By roll of 10000
                    For 0 to 1999
                        Give 10 coins
                    For 2000 to 9999
                        Give 20 coins
            """, "test.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        host.RollDice(9999).Returns(roll);
        result.Program.TryGetEntry(QuestProgram.AcceptEvent, 61, out var id).Should().BeTrue();
        new QuestInterpreter(result.Program, host).Run(id).Failure.Should().BeNull();
        host.Received(1).GiveGold(coins);
        host.Received(1).GiveGold(Arg.Any<int>());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(4, false)]
    public void MissingRewardSelectionsRefuseWithoutApplyingTransfers(int selection, bool accepted)
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            Quest 61
            On fulfil
                By selection
                    For 0
                        Transaction
                            Take 2 of 123
                            Give 100 coins
            """, "test.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var id).Should().BeTrue();
        new QuestInterpreter(result.Program, host, selection).Run(id).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(a => a.Count == (accepted ? 2 : 0)));
    }

    [Theory]
    [InlineData(0, 202002005)]
    [InlineData(1, 242002005)]
    public void ASelectionInsideATransactionPaysTheSharedTransfersAndTheChosenOneTogether(int selection, int item)
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            Quest 61
            On fulfil
                Transaction
                    Take 2 of 810418000
                    Give 7500 experience
                    By selection
                        For 0
                            Give 1 of 202002005
                        For 1
                            Give 1 of 242002005
            """, "test.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var id).Should().BeTrue();
        new QuestInterpreter(result.Program, host, selection).Run(id).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(a =>
            a.Count == 3
            && a[0].Kind == QuestActionKind.TakeItem && a[0].Arguments.GetInt("item") == 810418000
            && a[1].Kind == QuestActionKind.GiveExperience
            && a[2].Kind == QuestActionKind.GiveItem && a[2].Arguments.GetInt("item") == item));
    }

    [Fact]
    public void ASelectionInsideATransactionThatMatchesNothingRefusesAllOfIt()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            Quest 61
            On fulfil
                Transaction
                    Take 2 of 810418000
                    By selection
                        For 0
                            Give 1 of 202002005
            """, "test.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var id).Should().BeTrue();
        new QuestInterpreter(result.Program, host, 3).Run(id).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(a => a.Count == 0));
    }

    [Theory]
    [InlineData(false, 1, 0)]
    [InlineData(true, 0, 1)]
    public void ATransactionRunsItsRestOnlyWhenTheTransfersWereApplied(
        bool rewardFails, int expectedStateChanges, int expectedElseLines)
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            Quest 61 "Paid on delivery"
            On fulfil
                Transaction
                    Take 2 of 810418000
                    Give 7500 experience
                    Complete quest
                    Say "Thank you!"
                Else
                    Say "You need a free inventory slot."
            """, "test.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        host.ActionFailed.Returns(rewardFails);
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var id).Should().BeTrue();
        new QuestInterpreter(result.Program, host).Run(id).Failure.Should().BeNull();

        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(a => a.Count == 2));
        host.Received(expectedStateChanges).SetQuestState(61, 2);
        host.Received(expectedStateChanges + expectedElseLines).ShowDialog(
            Arg.Any<DialogStyle>(), Arg.Any<int>(),
            Arg.Is<DialogLine>(line => line.Text == (rewardFails
                ? "You need a free inventory slot."
                : "Thank you!")),
            Arg.Any<IReadOnlyList<DialogButton>>());
    }

    [Fact]
    public void ATransactionWithNothingToApplyIsRejected()
    {
        QuestCompilation.Create("""
            Bind Npc 100
            Quest 61 "Empty"
            On fulfil
                Transaction
                    Complete quest
            """, "test.quest").Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Reward")]
    [InlineData("By reward")]
    public void TheSpellingsThatNeverShippedAreNotAccepted(string removed)
    {
        QuestCompilation.Create($"""
            Bind Npc 100
            Quest 61 "One spelling only"
            On fulfil
                {removed}
                    Give 1 of 123
            """, "test.quest").Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Bind Npc 100\ninclude global\nOn greeting\n    Say 1")]
    [InlineData("Bind Npc 100\nOn greeting\n    Say 1\nQuest 61")]
    [InlineData("/*")]
    [InlineData("include duplicate\nBind Npc 100\nOn greeting\n    Say hello")]
    public void InvalidMetadataAndUnclosedCommentsAreRejected(string source)
    {
        var result = QuestCompilation.Create(source, "test.quest", includes: new Includes(new()
        {
            ["global"] = "hello = text \"Hello\"",
            ["duplicate"] = "hello = text \"One\"\nhello = text \"Two\""
        }));
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void QuestShorthandWorksInsideCompoundConditionsAndStateActions()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100 Zone 21
            Quest 61
            On topics
                If quest is available and player level >= 2
                    Topic "Start" do
                        Start quest
            On fulfil
                If player kills >= 2 in group 1 of quest and quest is active
                    Complete quest
            """, "test.quest");
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out _).Should().BeTrue();
    }

    [Fact]
    public void LaterIncludesOverrideEarlierNamesAndLocalNamesOverrideIncludes()
    {
        var includes = new Includes(new()
        {
            ["global"] = "hello = text \"Global\"\n",
            ["zone"] = "hello = text \"Zone\"\n"
        });
        foreach (var (local, expected) in new[] { ("", "Zone"), ("hello = text \"Local\"", "Local") })
        {
            var result = QuestCompilation.Create($"""
                include global
                include zone
                {local}
                Bind Npc 100
                On greeting
                    Say hello
                """, "test.quest", includes: includes);
            result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
            result.Program.TryGetGreeting(out var id).Should().BeTrue();
            result.Program.Events[id].Body.Should().ContainSingle().Which.Should()
                .BeOfType<BoundStatement.Dialog>().Which.Header.Text.Should().Be(expected);
        }
    }

    [Fact]
    public void CommentsPreserveLinesAndStringContents()
    {
        var result = QuestCompilation.Create("""
            /* header
             	 arbitrary comment indentation
               second line */
            Bind Zone 21 // entry
            Quest 61
            On zone_entry
                Start quest /* starts here */
                /* not executable:
                Complete quest
                */
                Say "https://example.test/*literal*/"
                Topic "Close" goto close
            """, "comments.quest");
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.Program.TryGetEntry(QuestProgram.ZoneEntryEvent, 0, out var id).Should().BeTrue();
        result.Program.Events[id].Body.Should().HaveCount(2);
    }

    [Fact]
    public void ShorthandWithoutAQuestIsRejected()
    {
        var result = QuestCompilation.Create("Bind Npc 100\nOn accept\n    Complete quest", "test.quest");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void FirstGreetingWinsAndATransactionNeedsSomethingToApply()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            On greeting
                Say "First"
            On greeting
                Say "Second"
            """, "test.quest");
        result.Succeeded.Should().BeTrue();
        result.Program.TryGetGreeting(out var id).Should().BeTrue();
        ((BoundStatement.Dialog)result.Program.Events[id].Body[0]).Header.Text.Should().Be("First");
        result = QuestCompilation.Create("Bind Npc 100\nQuest 61\nOn accept\n    Transaction\n        Complete quest", "test.quest");
        result.Succeeded.Should().BeFalse();
    }
}
