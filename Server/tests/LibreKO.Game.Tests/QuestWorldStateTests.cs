using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestWorldStateTests
{
    private static QuestCompilation Compile(string statement) =>
        QuestCompilation.Create($"""
            Bind Npc 16079

            On greeting
                {statement}
            """, "world.quest");

    private static BoundCondition ConditionOf(string test)
    {
        var compilation = QuestCompilation.Create($"""
            Bind Npc 16079

            On greeting
                Say "Well?"
                If {test}
                    Topic "Yes" goto close
                Topic "No" goto close
            """, "world.quest");

        compilation.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        return compilation.Program.Events.Values
            .Single(e => e.Name == "greeting")
            .Body.OfType<BoundStatement.Dialog>().Single()
            .Choices.First(c => c.When is not null).When!;
    }

    private static BoundStatement.Action ActionOf(QuestCompilation compilation) =>
        compilation.Program.Events.Values
            .Single(e => e.Name == "greeting")
            .Body.OfType<BoundStatement.Action>().Single();

    [Fact]
    public void AZoneIsAConditionOfItsOwn()
    {
        var condition = ConditionOf("player in zone 21");

        condition.Should().BeOfType<BoundCondition.Predicate>();
        condition.As<BoundCondition.Predicate>().Kind.Should().Be(QuestConditionKind.PlayerZone);
        condition.As<BoundCondition.Predicate>().Arguments.GetInt("zone").Should().Be(21);
    }

    [Fact]
    public void AZoneCanBeAskedTheOtherWayRound()
    {
        ConditionOf("player not in zone 21")
            .Should().BeOfType<BoundCondition.Not>();
    }

    [Fact]
    public void TheMonumentHolderIsANation()
    {
        var condition = ConditionOf("monument is Karus");

        condition.As<BoundCondition.Predicate>().Kind.Should().Be(QuestConditionKind.MonumentNation);
        condition.As<BoundCondition.Predicate>().Arguments.GetInt("nation").Should().Be(1);
    }

    [Fact]
    public void TheMonumentTakesANationAndNotAnything()
    {
        Compile("Say \"Well?\"");
        QuestCompilation.Create("""
            Bind Npc 16079

            On greeting
                Say "Well?"
                If monument is Warrior
                    Topic "Yes" goto close
            """, "world.quest")
            .Diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void CashIsGivenLikeGold()
    {
        var action = ActionOf(Compile("Give 500 cash"));

        action.Kind.Should().Be(QuestActionKind.GiveCash);
        action.Arguments.GetInt("amount").Should().Be(500);
    }

    [Fact]
    public void AJobChangeDefaultsToTheMasteredPath()
    {
        var action = ActionOf(Compile("Change job to Warrior"));

        action.Kind.Should().Be(QuestActionKind.ChangeJob);
        action.Arguments.GetInt("class").Should().Be(QuestVocabulary.ClassGroupWarrior);
        action.Arguments.GetInt("mastered").Should().Be(1);
    }

    [Fact]
    public void AJobChangeCanSayItIsNotTheMasteredPath()
    {
        var action = ActionOf(Compile("Change job to Kurian unmastered"));

        action.Arguments.GetInt("class").Should().Be(QuestVocabulary.ClassGroupKurian);
        action.Arguments.GetInt("mastered").Should().Be(0);
    }

    [Fact]
    public void AnExchangeCanRunSeveralTimes()
    {
        var action = ActionOf(Compile("Exchange 317 x 4"));

        action.Kind.Should().Be(QuestActionKind.ExchangeTimes);
        action.Arguments.GetInt("exchange").Should().Be(317);
        action.Arguments.GetInt("count").Should().Be(4);
    }

    [Fact]
    public void TheTimesFormNeedsTheSpaceItsPhraseShows()
    {
        Compile("Exchange 317 x4")
            .Diagnostics.Should().Contain(d => d.Id == DiagnosticId.UnexpectedToken);
    }
}
