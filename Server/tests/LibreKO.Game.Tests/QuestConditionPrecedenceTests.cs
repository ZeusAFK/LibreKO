using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestConditionPrecedenceTests
{
    private static BoundCondition ConditionOf(string test)
    {
        var compilation = QuestCompilation.Create($"""
            Bind Npc 16079

            On greeting
                Say "Well?"
                If {test}
                    Topic "Yes" goto close
                Topic "No" goto close
            """, "precedence.quest");

        compilation.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        return compilation.Program.Events.Values
            .Single(e => e.Name == "greeting")
            .Body.OfType<BoundStatement.Dialog>().Single()
            .Choices.First(c => c.When is not null).When!;
    }

    [Fact]
    public void AndBindsTighterThanOr()
    {
        ConditionOf("player level >= 10 and player is Warrior or player is Rogue")
            .Should().BeOfType<BoundCondition.Or>(
                "an unbracketed chain reads as (level and Warrior) or Rogue");
    }

    [Fact]
    public void NotBindsTighterThanAndAndCanNegateAGroup()
    {
        var condition = ConditionOf("not (player is Warrior or player is Rogue) and player level >= 10")
            .Should().BeOfType<BoundCondition.And>().Subject;
        condition.Left.Should().BeOfType<BoundCondition.Not>().Subject.Operand
            .Should().BeOfType<BoundCondition.Or>();
    }

    [Fact]
    public void BracketsMakeTheOrGroupOneTerm()
    {
        var condition = ConditionOf(
            "player level >= 10 and (player is Warrior or player is Rogue)");

        condition.Should().BeOfType<BoundCondition.And>();
        condition.As<BoundCondition.And>().Right.Should().BeOfType<BoundCondition.Or>();
    }

    [Fact]
    public void ABracketedGroupOnItsOwnIsStillTheGroup()
    {
        ConditionOf("(player is Warrior or player is Rogue)")
            .Should().BeOfType<BoundCondition.Or>();
    }
}
