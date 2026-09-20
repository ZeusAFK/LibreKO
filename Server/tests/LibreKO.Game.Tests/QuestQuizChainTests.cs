using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;
using System.Runtime.CompilerServices;

namespace LibreKO.Game.Tests;

public class QuestQuizChainTests
{
    private const int Ascetic = 317;
    private const int BookOfSuffering = 910127000;
    private const int FirstCertificate = 910128000;
    private const int SeventhCertificate = 910134000;

    private static string BakedQuestPath(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static QuestProgram Compose(string file, string name, int npc, int zone)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath(file));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose(name, npc, zone, [compilation.Program]);
    }

    private static IQuestHost Host(int zone, int book, int seventh)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(zone);
        host.PlayerNation.Returns(1);
        host.PlayerLevel.Returns(62);
        host.PlayerClassGroup.Returns(1);
        host.QuestStatus(Ascetic).Returns(1);
        host.ItemCount(BookOfSuffering).Returns(book);
        host.ItemCount(SeventhCertificate).Returns(seventh);
        return host;
    }

    private static IReadOnlyList<DialogButton> Shown(IQuestHost host)
    {
        var call = host.ReceivedCalls().Last(c => c.GetMethodInfo().Name is "ShowDialog" or "ShowQuestView");
        return call.GetMethodInfo().Name == "ShowQuestView"
            ? ((QuestView)call.GetArguments()[0]!).Topics
            : (IReadOnlyList<DialogButton>)call.GetArguments()[3]!;
    }

    private static void Run(QuestProgram program, IQuestHost host, int eventId) =>
        new QuestInterpreter(program, host).Run(eventId).Failure.Should().BeNull();

    [Fact]
    public void TheAsceticHandsALostOrderBackBehindConfirmAndOnlyWhileTheChainIsNotFinished()
    {
        var program = Compose("0_0_317.quest", "veda", 24424, 11);
        var host = Host(11, book: 0, seventh: 0);
        program.TryGetEntry(QuestProgram.ViewEvent, Ascetic, out var view).Should().BeTrue();
        Run(program, host, view);
        var page = (QuestView)host.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ShowQuestView").GetArguments()[0]!;
        page.State.Should().Be(QuestViewState.InProgress);
        page.Dialogue.Text.Should().StartWith("I will give you another written order.");
        var confirm = page.Topics.Single(b => b.Label.Text == "Confirm");
        Run(program, host, confirm.TargetEvent);
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(a =>
            a.Count == 1 && a[0].Kind == QuestActionKind.GiveItem && a[0].Arguments.GetInt("item") == BookOfSuffering));

        foreach (var (book, seventh) in new[] { (1, 0), (0, 1) })
        {
            var holding = Host(11, book, seventh);
            Run(program, holding, view);
            var reminder = (QuestView)holding.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ShowQuestView").GetArguments()[0]!;
            reminder.Dialogue.Text.Should().NotContain("another written order");
            reminder.Topics.Should().NotContain(b => b.Label.Text == "Confirm");
            holding.DidNotReceiveWithAnyArgs().ApplyReward(default!);
        }
    }

    [Fact]
    public void AnUndertakerSwapsTheCertificateOnTheRightAnswerAndForfeitsItOnTheWrongOne()
    {
        var program = Compose("24417_11.quest", "chatsra", 24417, 11);
        foreach (var (answer, gives) in new[] { ("Goblin Village", true), ("Gavolt Village", false) })
        {
            var host = Host(11, book: 1, seventh: 0);
            program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
            Run(program, host, greeting);
            var accepted = Shown(host).Single(b => b.Label.Text == "Accepted");
            Run(program, host, accepted.TargetEvent);
            var choices = Shown(host);
            choices.Select(b => b.Label.Text).Should().BeEquivalentTo(["Goblin Village", "Gavolt Village"]);
            Run(program, host, choices.Single(b => b.Label.Text == answer).TargetEvent);
            host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(a =>
                a.Count == (gives ? 2 : 1)
                && a[0].Kind == QuestActionKind.TakeItem && a[0].Arguments.GetInt("item") == BookOfSuffering
                && (!gives || (a[1].Kind == QuestActionKind.GiveItem && a[1].Arguments.GetInt("item") == FirstCertificate))));
        }

        var empty = Host(11, book: 0, seventh: 0);
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var refusal).Should().BeTrue();
        Run(program, empty, refusal);
        empty.Received().ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(),
            Arg.Is<DialogLine>(l => l.Text.Contains("I only pose the question")), Arg.Any<IReadOnlyList<DialogButton>>());
    }
}
