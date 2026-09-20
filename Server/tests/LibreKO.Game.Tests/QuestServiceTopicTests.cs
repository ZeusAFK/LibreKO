using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;
using System.Runtime.CompilerServices;

namespace LibreKO.Game.Tests;

public class QuestServiceTopicTests
{
    private const int BlueChest = 379156000;
    private const int PriestDestructionTop = 283011340;
    private const int Crystal = 389075000;
    private const int TaliaTalisman = 379072000;

    private static string BakedQuestPath(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static QuestProgram Compose(string file, string name, int npc, int zone)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath(file));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose(name, npc, zone, [compilation.Program]);
    }

    private static IQuestHost Host(int classGroup)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerNation.Returns(1);
        host.PlayerLevel.Returns(60);
        host.PlayerClassGroup.Returns(classGroup);
        host.RollDice(Arg.Any<int>()).Returns(0);
        return host;
    }

    private static IReadOnlyList<DialogButton> Shown(IQuestHost host) =>
        (IReadOnlyList<DialogButton>)host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[3]!;

    private static void Run(QuestProgram program, IQuestHost host, int eventId) =>
        new QuestInterpreter(program, host).Run(eventId).Failure.Should().BeNull();

    private static IReadOnlyList<BoundStatement.Action> Applied(IQuestHost host) =>
        (IReadOnlyList<BoundStatement.Action>)host.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ApplyReward").GetArguments()[0]!;

    [Fact]
    public void MoirasChestTopicDrawsOnePrizeAndRefusesAnEmptyHandedPlayer()
    {
        var program = Compose("16047_21_11.quest", "moira", 16047, 21);
        var host = Host(1);
        host.ItemCount(BlueChest).Returns(1);
        program.TryGetEntry(QuestProgram.TopicsEvent, 0, out var topics).Should().BeTrue();
        Run(program, host, topics);
        var trade = Shown(host).Single(b => b.Label.Text == "Trade 1st grade treasure chest");
        Run(program, host, trade.TargetEvent);
        var confirm = Shown(host).Single(b => b.Label.Text == "Confirm");
        Run(program, host, confirm.TargetEvent);
        var applied = Applied(host);
        applied.Should().HaveCount(2);
        applied[0].Kind.Should().Be(QuestActionKind.TakeItem);
        applied[0].Arguments.GetInt("item").Should().Be(BlueChest);
        applied[1].Kind.Should().Be(QuestActionKind.GiveItem);
        applied[1].Arguments.GetInt("item").Should().Be(121210002);

        var empty = Host(1);
        Run(program, empty, topics);
        Run(program, empty, Shown(empty).Single(b => b.Label.Text == "Trade 1st grade treasure chest").TargetEvent);
        empty.Received().ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(),
            Arg.Is<DialogLine>(l => l.Text.StartsWith("You don't have the treasure chest")), Arg.Any<IReadOnlyList<DialogButton>>());
        empty.DidNotReceiveWithAnyArgs().ApplyReward(default!);
    }

    [Fact]
    public void HepasTaliaTopicTakesTheClassesOwnTopAndDrawsFromTheClassPool()
    {
        var program = Compose("14301_21_205.quest", "hepa", 14301, 21);
        var host = Host(4);
        program.TryGetEntry(QuestProgram.TopicsEvent, 0, out var topics).Should().BeTrue();
        Run(program, host, topics);
        Run(program, host, Shown(host).Single(b => b.Label.Text == "Talia armor").TargetEvent);
        Run(program, host, Shown(host).Single(b => b.Label.Text == "Confirm").TargetEvent);
        var applied = Applied(host);
        applied.Select(a => (a.Kind, a.Arguments.GetInt("item"))).Should().Equal(
            (QuestActionKind.TakeItem, PriestDestructionTop),
            (QuestActionKind.TakeItem, Crystal),
            (QuestActionKind.TakeItem, TaliaTalisman),
            (QuestActionKind.GiveItem, 283021328));
    }
}
