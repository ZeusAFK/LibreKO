using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestMenuTests
{
    private const string Menu = """
        Bind Npc 16079

        On greeting
            Say "What do you need?"
            If quest 61 is unstarted
                Topic "Silk Spool" goto silk
            If quest 512 is started and player level >= 60
                Topic "The vouchers" goto chaos
            Topic "Nothing" goto close

        On silk
            Say "Bring me apples."
            Topic "Fine" goto close

        On chaos
            Say "Hand them over."
            Topic "Fine" goto close
        """;

    private static QuestCompilation Compile(string text) =>
        QuestCompilation.Create(text, "menu.quest");

    [Fact]
    public void AnOfferInsideAnIfBelongsToTheDialogAboveIt()
    {
        var compilation = Compile(Menu);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var greeting = compilation.Program.Events.Values
            .Single(e => e.Name == "greeting");
        var dialog = greeting.Body.OfType<BoundStatement.Dialog>().Single();

        dialog.Choices.Should().HaveCount(3);
        dialog.Choices.Count(c => c.When is not null).Should().Be(2);
        dialog.Choices.Last().When.Should().BeNull("the last offer is unconditional");
    }

    [Fact]
    public void TheGreetingIsNotReportedAsDeadCode()
    {
        Compile(Menu).Diagnostics
            .Should().NotContain(d => d.Id == DiagnosticId.UnreachableEvent);
    }

    [Fact]
    public void AScriptThatNamesAnNpcButHasNoGreetingIsFlagged()
    {
        var text = string.Join('\n',
            "Bind Npc 16079",
            "",
            "topic = event 1205",
            "",
            "On topic",
            "    Say \"hi\"",
            "    Topic \"bye\" goto close");

        Compile(text).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.NoGreeting);
    }

    [Fact]
    public void AFileThatOnlyCarriesQuestTextIsNotCalledUnreachable()
    {
        var text = string.Join('\n',
            "Bind Npc 16079",
            "",
            "Quest 523 \"[Descendant of Hero] Unidentified Attacker\"",
            "    Journal \"You must talk to Pablo.\"");

        Compile(text).Diagnostics
            .Should().NotContain(d => d.Id == DiagnosticId.NoGreeting);
    }

    [Fact]
    public void AFileWithQuestTextAndAHandlerStillNeedsAWayIn()
    {
        var text = string.Join('\n',
            "Bind Npc 16079",
            "",
            "Quest 523 \"[Descendant of Hero] Unidentified Attacker\"",
            "",
            "topic = event 1205",
            "",
            "On topic",
            "    Say \"hi\"",
            "    Topic \"bye\" goto close");

        Compile(text).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.NoGreeting);
    }

    [Fact]
    public void TheProgramExposesItsGreetingSoTheServerCanRouteAClick()
    {
        Compile(Menu).Program.TryGetGreeting(out var eventId).Should().BeTrue();
        eventId.Should().BeGreaterThanOrEqualTo(QuestProgram.LocalEventBase);
    }

    [Fact]
    public void AnOfferStillCannotFloatWithoutADialog()
    {
        var text = string.Join('\n',
            "Bind Npc 16079",
            "",
            "On \"greeting\"",
            "    Topic \"orphan\" goto close");

        Compile(text).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.ButtonWithoutMessage);
    }
}
