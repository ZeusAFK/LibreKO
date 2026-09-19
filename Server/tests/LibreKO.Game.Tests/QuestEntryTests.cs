using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestEntryTests
{
    private const string Entries = """
        Bind Npc 16079

        On greeting
            Say "What do you need?"
            Topic "Silk Spool" goto accept_61
            Topic "Nothing" goto close

        On accept for quest 61
            Goto silk

        On fulfil for quest 61
            Say "Well done."
            Topic "Fine" goto close

        On abandon for quest 61
            Say "Pity."
            Topic "Fine" goto close

        On silk
            Say "Bring me apples."
            Topic "Fine" goto close
        """;

    private static QuestCompilation Compile(string text) =>
        QuestCompilation.Create(text, "entries.quest");

    [Fact]
    public void AQuestEntryIsAddressedByItsRoleAndQuest()
    {
        var program = Compile(Entries).Program;

        program.TryGetEntry(QuestProgram.AcceptEvent, 61, out var accept).Should().BeTrue();
        program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var fulfil).Should().BeTrue();
        program.TryGetEntry(QuestProgram.AbandonEvent, 61, out var abandon).Should().BeTrue();

        accept.Should().NotBe(fulfil);
        fulfil.Should().NotBe(abandon);
    }

    [Fact]
    public void AQuestEntryForAnotherQuestIsNotFound()
    {
        Compile(Entries).Program
            .TryGetEntry(QuestProgram.AcceptEvent, 512, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void AQuestEntryCarriesNoNumberAndStillGetsALocalId()
    {
        Compile(Entries).Program.TryGetEntry(QuestProgram.AcceptEvent, 61, out var accept);
        accept.Should().BeGreaterThanOrEqualTo(QuestProgram.LocalEventBase);
    }

    [Fact]
    public void AQuestEntryCanBeReachedByNameFromTheGreeting()
    {
        var compilation = Compile(Entries);

        compilation.Diagnostics
            .Should().NotContain(d => d.Id == DiagnosticId.UnknownEventTarget);

        compilation.Program.TryGetEntry(QuestProgram.AcceptEvent, 61, out var accept);
        var greeting = compilation.Program.Events.Values.Single(e => e.Name == "greeting");
        greeting.Body.OfType<BoundStatement.Dialog>().Single()
            .Choices.Should().Contain(c => c.Button.TargetEvent == accept);
    }

    [Fact]
    public void EntriesAreNotReportedAsDeadCode()
    {
        Compile(Entries).Diagnostics
            .Should().NotContain(d => d.Id == DiagnosticId.UnreachableEvent);
    }

    [Fact]
    public void AnEntryQuestIsAnAddressSoItIsNeverAnUnusedScope()
    {
        Compile(Entries).Diagnostics
            .Should().NotContain(d => d.Id == DiagnosticId.UnusedQuestScope);
    }

    [Fact]
    public void AQuestEntryWithoutAQuestIsAnError()
    {
        var diagnostics = Compile("""
            Bind Npc 16079

            On greeting
                Say "Hello."
                Topic "Nothing" goto close

            On fulfil
                Say "Well done."
                Topic "Fine" goto close
            """).Diagnostics;

        diagnostics.Should().ContainSingle(d => d.Id == DiagnosticId.EntryWithoutQuest);
    }

    [Fact]
    public void TheSameRoleServesDifferentQuestsSeparately()
    {
        var program = Compile("""
            Bind Npc 16079

            On greeting
                Say "Hello."
                Topic "Nothing" goto close

            On fulfil for quest 61
                Say "Apples, good."
                Topic "Fine" goto close

            On fulfil for quest 512
                Say "Vouchers, good."
                Topic "Fine" goto close
            """).Program;

        program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var first).Should().BeTrue();
        program.TryGetEntry(QuestProgram.FulfilEvent, 512, out var second).Should().BeTrue();
        first.Should().NotBe(second);
    }

    [Fact]
    public void AGreetingNeedsNoQuestToBeFound()
    {
        Compile(Entries).Program.TryGetGreeting(out var greeting).Should().BeTrue();
        Compile(Entries).Program
            .TryGetEntry(QuestProgram.GreetingEvent, 0, out var byRole).Should().BeTrue();
        byRole.Should().Be(greeting);
    }
}
