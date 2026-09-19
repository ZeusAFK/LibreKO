using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestButtonBodyTests
{
    private const string Inline = """
        Bind Npc 16079

        On greeting
            Say "What can I do for you?"
            Topic "Where is the Sentinel?" do
                Clear 62
            Topic "I will swing by" do
                Start 62
            Topic "Nothing" goto close
        """;

    private static QuestCompilation Compile(string text) =>
        QuestCompilation.Create(text, "buttons.quest");

    private static BoundStatement.Dialog Greeting(QuestCompilation compilation) =>
        compilation.Program.Events.Values
            .Single(e => e.Name == "greeting")
            .Body.OfType<BoundStatement.Dialog>()
            .Single();

    [Fact]
    public void AButtonBodyCompilesWithoutAnEventOfItsOwn()
    {
        var compilation = Compile(Inline);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        Greeting(compilation).Choices.Should().HaveCount(3);
    }

    [Fact]
    public void TheBodyIsReachableThroughTheButtonsTarget()
    {
        var compilation = Compile(Inline);
        var choice = Greeting(compilation).Choices
            .Single(c => c.Button.Label.Text.Contains("Sentinel"));

        compilation.Program.TryGetEvent(choice.Button.TargetEvent, out var lifted)
            .Should().BeTrue("the wire carries an event id, so the body needs one");
        lifted.Body.Should().HaveCount(1);
        lifted.Name.Should().BeNull("nothing in the file names it");
    }

    [Fact]
    public void EachButtonGetsItsOwnBody()
    {
        var choices = Greeting(Compile(Inline)).Choices;
        var first = choices.Single(c => c.Button.Label.Text.Contains("Sentinel"));
        var second = choices.Single(c => c.Button.Label.Text.Contains("swing"));

        first.Button.TargetEvent.Should().NotBe(second.Button.TargetEvent);
    }

    [Fact]
    public void ALiftedBodyIsNotReportedAsDeadCode()
    {
        Compile(Inline).Diagnostics
            .Should().NotContain(d => d.Id == DiagnosticId.UnreachableEvent);
    }

    [Fact]
    public void AButtonBodyStillHasToFollowASay()
    {
        Compile("""
            Bind Npc 16079

            On greeting
                Say "Hello."
                Topic "Nothing" goto close

            On somewhere
                Topic "orphan" do
                    Clear 62
            """).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.ButtonWithoutMessage);
    }

    [Fact]
    public void AGatedButtonBodyKeepsItsCondition()
    {
        var choices = Greeting(Compile("""
            Bind Npc 16079

            On greeting
                Say "Well?"
                If player level >= 3
                    Topic "Ready" do
                        Start 62
                Topic "Nothing" goto close
            """)).Choices;

        choices.Single(c => c.Button.Label.Text == "Ready")
            .When.Should().NotBeNull();
    }

    [Fact]
    public void AButtonBodyMayOpenAPageOfItsOwn()
    {
        var compilation = Compile("""
            Bind Npc 16079

            On greeting
                Say "Well?"
                Topic "Tell me more" do
                    Say "It is a long story."
                    Topic "Fine" goto close
                Topic "Nothing" goto close
            """);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var choice = Greeting(compilation).Choices
            .Single(c => c.Button.Label.Text.Contains("more"));
        compilation.Program.TryGetEvent(choice.Button.TargetEvent, out var lifted);
        lifted.Body.OfType<BoundStatement.Dialog>().Should().HaveCount(1);
    }
}
