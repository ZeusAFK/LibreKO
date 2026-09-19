using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestDialogFallbackTests
{
    private const string Menu = """
        Bind Npc 13013

        On greeting
            Say "What mission are you going to undertake?"
            If quest 60 is available
                Topic "Worm hunt" goto close
            If quest 62 is available
                Topic "Bandicoot hunt" goto close
            Topic "Nothing for now" goto close
            If no topic fits
                Say "There are too many sentinels around."
                Topic "Confirm" goto close
        """;

    private sealed record Shown(DialogLine Header, IReadOnlyList<DialogButton> Buttons);

    private static Shown Play(string source, params int[] availableQuests)
    {
        var compilation = QuestCompilation.Create(source, "fallback.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        foreach (var quest in availableQuests)
            host.QuestStatus(quest).Returns(0);
        foreach (var quest in new[] { 60, 62 }.Except(availableQuests))
            host.QuestStatus(quest).Returns(2);

        Shown? shown = null;
        host.When(h => h.ShowDialog(
                Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(),
                Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(call => shown = new Shown(
                call.ArgAt<DialogLine>(2), call.ArgAt<IReadOnlyList<DialogButton>>(3)));

        compilation.Program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(compilation.Program, host).Run(entry).Failure.Should().BeNull();
        shown.Should().NotBeNull();
        return shown!;
    }

    [Fact]
    public void TheTopicsShowWhileAnyOfThemFits()
    {
        var shown = Play(Menu, 60);

        shown.Header.Text.Should().Be("What mission are you going to undertake?");
        shown.Buttons.Select(b => b.Label.Text)
            .Should().Equal("Worm hunt", "Nothing for now");
    }

    [Fact]
    public void TheFallbackReplacesTheWholeDialogWhenNoTopicFits()
    {
        var shown = Play(Menu);

        shown.Header.Text.Should().Be("There are too many sentinels around.");
        shown.Buttons.Select(b => b.Label.Text).Should().Equal("Confirm");
    }

    [Fact]
    public void AnAlwaysOfferedButtonDoesNotCountAsATopic()
    {
        var compilation = QuestCompilation.Create(Menu, "fallback.quest");
        var dialog = compilation.Program.Events.Values
            .Single(e => e.Name == "greeting")
            .Body.OfType<BoundStatement.Dialog>()
            .Single();

        dialog.Choices.Should().HaveCount(3);
        dialog.Fallback.Should().NotBeNull();
        dialog.Fallback!.Should().ContainSingle()
            .Which.Should().BeOfType<BoundStatement.Dialog>()
            .Which.Choices.Should().ContainSingle();
    }

    [Fact]
    public void AFallbackCanSendThePlayerSomewhereInstead()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 13013

            nothing_here = event 101

            On greeting
                Say "What mission?"
                If quest 60 is available
                    Topic "Worm hunt" goto close
                If no topic fits
                    Goto nothing_here

            On nothing_here
                Say "Come back later."
                Topic "Confirm" goto close
            """, "jump.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        host.QuestStatus(60).Returns(2);
        Shown? shown = null;
        host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(),
                Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(call => shown = new Shown(call.ArgAt<DialogLine>(2),
                call.ArgAt<IReadOnlyList<DialogButton>>(3)));

        compilation.Program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(compilation.Program, host).Run(entry).Failure.Should().BeNull();

        shown.Should().NotBeNull();
        shown!.Header.Text.Should().Be("Come back later.");
    }

    [Fact]
    public void AnEmptyGreetingWithNothingToOfferShowsNoWindow()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 14202 Zone 2

            On greeting
                If quest 519 is started and player lacks 910209000
                    Say "You forgot? I'll give it to you one last time."
                    Topic "Confirm" do
                        Transaction
                            Give 1 of 910209000
                Else
                    Say ""
            """, "clarence.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("clarence", 14202, 2, [compilation.Program]);

        var host = Substitute.For<IQuestHost>();
        host.QuestStatus(519).Returns(2);
        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.DidNotReceiveWithAnyArgs().ShowDialog(default, default, default!, default!);
        host.DidNotReceiveWithAnyArgs().ShowQuestView(default!);

        host.QuestStatus(519).Returns(1);
        host.ItemCount(910209000).Returns(0);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received(1).ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(),
            Arg.Is<DialogLine>(line => line.Text!.StartsWith("You forgot?")), Arg.Any<IReadOnlyList<DialogButton>>());
    }

    [Fact]
    public void AnNpcWhoseOnlyQuestsAreLockedSaysNothing()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 32531 Zone 73
            Quest 576 "Attack of the Devil Army"
                Journal "Talk to Krujed."

            Requires player level >= 66

            Rewards none

            On offer
                Show quest "Have they finally arrived!"
            """, "krujed.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("krujed", 32531, 73, [compilation.Program]);

        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(73);
        host.PlayerLevel.Returns(10);
        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.DidNotReceiveWithAnyArgs().ShowDialog(default, default, default!, default!);
        host.DidNotReceiveWithAnyArgs().ShowQuestView(default!);

        host.PlayerLevel.Returns(66);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received(1).ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(),
            Arg.Is<IReadOnlyList<DialogButton>>(buttons => buttons.Count == 1));
    }

    [Fact]
    public void AFallbackOutsideADialogIsRefused()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 13013

            On greeting
                If no topic fits
                    Give 1 coins
            """, "stray.quest");

        compilation.Diagnostics.Should().Contain(d =>
            d.Id == DiagnosticId.MisplacedFallback && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AFallbackWithNoTopicAboveItAlwaysWinsAndSaysSo()
    {
        var compilation = QuestCompilation.Create("""
            On greeting
                Say "Pick one."
                Topic "Nothing for now" goto close
                If no topic fits
                    Say "Nothing here."
                    Topic "Confirm" goto close
            """, "always.quest");

        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        compilation.Diagnostics.Should().Contain(d =>
            d.Id == DiagnosticId.MisplacedFallback && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ABoundGreetingKeepsItsFallbackBecauseSiblingFilesSupplyTheTopics()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 13013

            On greeting
                Say "Pick one."
                Topic "Nothing for now" goto close
                If no topic fits
                    Say "Nothing here."
                    Topic "Confirm" goto close
            """, "bound.quest");

        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        compilation.Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.MisplacedFallback);
    }

    [Fact]
    public void AFallbackHoldsOneMessageAndItsButtons()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 13013

            On greeting
                Say "Pick one."
                If quest 60 is available
                    Topic "Worm hunt" goto close
                If no topic fits
                    Give 1 coins
                    Say "Nothing here."
                    Topic "Confirm" goto close
            """, "mixed.quest");

        compilation.Diagnostics.Should().Contain(d =>
            d.Id == DiagnosticId.MisplacedFallback && d.Severity == DiagnosticSeverity.Error);
    }
}
