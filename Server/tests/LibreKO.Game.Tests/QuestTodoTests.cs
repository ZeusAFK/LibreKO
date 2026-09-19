using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestTodoTests
{
    private static QuestCompilation Compile(string text) =>
        QuestCompilation.Create(text, "todo.quest");

    private const string WithNote = """
        Bind Npc 16079

        On greeting
            Todo "the guard below was dropped, so this always runs: lua 1006: if (MonsterSub == 0) then"
            Say "Hello."
            Topic "Fine" goto close
        """;

    [Fact]
    public void ANoteCompilesButIsReported()
    {
        var compilation = Compile(WithNote);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        compilation.Diagnostics
            .Should().ContainSingle(d => d.Id == DiagnosticId.UnfinishedStatement);
    }

    [Fact]
    public void ANoteDoesNothingAtAll()
    {
        var greeting = Compile(WithNote).Program.Events.Values.Single(e => e.Name == "greeting");

        greeting.Body.OfType<BoundStatement.Action>()
            .Should().ContainSingle(a => a.Kind == QuestActionKind.Todo)
            .Which.Arguments.Should().BeSameAs(ArgumentSet.Empty);
    }

    [Fact]
    public void ANoteKeepsAnOtherwiseEmptyHandlerCompiling()
    {
        Compile("""
            Bind Npc 16079

            On greeting
                Say "Hello."
                Topic "Fine" goto close

            On not_written_yet
                Todo "call SaveEvent"
            """).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Fact]
    public void ANoteCanStandAsAButtonBody()
    {
        var compilation = Compile("""
            Bind Npc 16079

            On greeting
                Say "Hello."
                Topic "Do the thing" do
                    Todo "call GivePremium"
                Topic "Fine" goto close
            """);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        compilation.Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.UnfinishedStatement);
    }
}
