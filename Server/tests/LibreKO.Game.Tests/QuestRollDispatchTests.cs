using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestRollDispatchTests
{
    private static QuestCompilation Compile(string body) =>
        QuestCompilation.Create($"""
            Bind Npc 16079

            On greeting
            {body}
            """, "roll.quest");

    private static BoundStatement.Switch SwitchOf(QuestCompilation compilation) =>
        compilation.Program.Events.Values
            .Single(e => e.Name == "greeting")
            .Body.OfType<BoundStatement.Switch>().Single();

    private const string Complete = """
                By roll of 2
                    0
                        Give 1 coins
                    1
                        Give 2 coins
                    2
                        Give 3 coins
        """;

    [Fact]
    public void ARollIsItsOwnSelector()
    {
        var compilation = Compile(Complete);

        compilation.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        var node = SwitchOf(compilation);
        node.Selector.Should().Be(SwitchSelectorKind.Roll);
        node.Max.Should().Be(2);
    }

    [Fact]
    public void OneCaseCanAnswerSeveralRolls()
    {
        var node = SwitchOf(Compile("""
                    By roll of 2
                        0, 2
                            Give 1 coins
                        1
                            Give 2 coins
            """));

        node.Cases.Should().HaveCount(2);
        node.Cases[0].Labels.Should().Equal(0L, 2L);
    }

    [Fact]
    public void ARollWithNoHighestNumberIsRejected()
    {
        Compile("""
                    By roll of
                        0
                            Give 1 coins
            """)
            .Diagnostics.Should().Contain(d => d.Id == DiagnosticId.UnexpectedToken);
    }

    [Fact]
    public void ARollThatCannotComeUpIsWorthSaying()
    {
        Compile("""
                    By roll of 1
                        0
                            Give 1 coins
                        1
                            Give 2 coins
                        7
                            Give 3 coins
            """)
            .Diagnostics.Should().Contain(d =>
                d.Id == DiagnosticId.CaseTypeMismatch && d.Message.Contains("never comes up 7"));
    }

    [Fact]
    public void ARollNothingAnswersIsWorthSaying()
    {
        Compile("""
                    By roll of 6
                        0
                            Give 1 coins
                        1
                            Give 2 coins
            """)
            .Diagnostics.Should().Contain(d =>
                d.Id == DiagnosticId.CaseTypeMismatch && d.Message.Contains("2, 3, 4, 5"));
    }

    [Fact]
    public void AnOtherwiseAnswersTheRestOfTheRolls()
    {
        var compilation = Compile("""
                    By roll of 6
                        0
                            Give 1 coins
                        Else
                            Give 2 coins
            """);

        compilation.Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.CaseTypeMismatch);
        SwitchOf(compilation).DefaultBody.Should().NotBeNull();
    }

    [Fact]
    public void ARollDoesNotTakeANameAsACase()
    {
        Compile("""
                    By roll of 2
                        Warrior
                            Give 1 coins
                        1
                            Give 2 coins
            """)
            .Diagnostics.Should().Contain(d => d.Id == DiagnosticId.UnknownEnumMember);
    }

    [Fact]
    public void TheSameRollTwiceIsARealMistake()
    {
        Compile("""
                    By roll of 2
                        1
                            Give 1 coins
                        1
                            Give 2 coins
            """)
            .Diagnostics.Should().Contain(d => d.Id == DiagnosticId.DuplicateCaseLabel);
    }

    [Fact]
    public void ANumberIsStillNotAClass()
    {
        Compile("""
                    By player class
                        3
                            Give 1 coins
                        Rogue
                            Give 2 coins
            """)
            .Diagnostics.Should().Contain(d =>
                d.Id == DiagnosticId.UnknownEnumMember && d.Message.Contains("'3' is not a class"));
    }
}
