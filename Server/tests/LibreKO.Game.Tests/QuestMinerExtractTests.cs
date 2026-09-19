using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestMinerExtractTests
{
    private const int Miner = 31511;
    private const int MysteriousOre = 399210000;
    private const int Extract = 389770000;
    private const string ExtractTopic = "[Quest/Mining] Extracting magic from the rocks";

    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static (QuestProgram Program, IQuestHost Host, Func<IReadOnlyList<DialogButton>> Shown) Talk(int knockStatus)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath($"{Miner}.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("miner", Miner, 21, [compilation.Program]);

        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(60);
        host.QuestStatus(619).Returns(knockStatus);
        host.ItemCount(MysteriousOre).Returns(1);
        host.HasRoomForItem(Arg.Any<int>(), Arg.Any<int>()).Returns(true);
        host.CanReceiveStacks(Arg.Any<int>()).Returns(true);
        IReadOnlyList<DialogButton> shown = [];
        host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => shown = c.ArgAt<IReadOnlyList<DialogButton>>(3));
        return (program, host, () => shown);
    }

    private static DialogButton Follow(QuestProgram program, IQuestHost host, Func<IReadOnlyList<DialogButton>> shown, DialogButton button)
    {
        new QuestInterpreter(program, host).Run(button.TargetEvent).Failure.Should().BeNull();
        return shown().Single(b => b.Label.Text == ExtractTopic || b.Label.Text.StartsWith("[Mysterious Ore]"));
    }

    [Fact]
    public void WhileKnockIsOpenTheMinerTurnsAnOreIntoTheExtract()
    {
        var (program, host, shown) = Talk(knockStatus: 1);
        program.TryGetGreeting(out var greeting).Should().BeTrue();
        new QuestInterpreter(program, host).Run(greeting).Failure.Should().BeNull();

        var refine = shown().Single(b => b.Label.Text == ExtractTopic);
        new QuestInterpreter(program, host).Run(refine.TargetEvent).Failure.Should().BeNull();
        var extractMenu = shown().Single(b => b.Label.Text == ExtractTopic);
        shown().Should().HaveCount(3, "the quest branch adds the extract menu to the two refine topics");

        new QuestInterpreter(program, host).Run(extractMenu.TargetEvent).Failure.Should().BeNull();
        var fromOre = shown().Single(b => b.Label.Text == "[Mysterious Ore] from extract");
        new QuestInterpreter(program, host).Run(fromOre.TargetEvent).Failure.Should().BeNull();

        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 2
            && actions.Any(a => a.Kind == QuestActionKind.TakeItem && a.Arguments.GetInt("item") == MysteriousOre
                && a.Arguments.GetInt("count", 1) == 1)
            && actions.Any(a => a.Kind == QuestActionKind.GiveItem && a.Arguments.GetInt("item") == Extract
                && a.Arguments.GetInt("count", 1) == 1)));
    }

    [Fact]
    public void WithoutKnockTheExtractMenuStaysHidden()
    {
        var (program, host, shown) = Talk(knockStatus: 0);
        program.TryGetGreeting(out var greeting).Should().BeTrue();
        new QuestInterpreter(program, host).Run(greeting).Failure.Should().BeNull();

        var refine = shown().Single(b => b.Label.Text == ExtractTopic);
        new QuestInterpreter(program, host).Run(refine.TargetEvent).Failure.Should().BeNull();

        shown().Should().HaveCount(2);
        shown().Should().NotContain(b => b.Label.Text == ExtractTopic);
        host.DidNotReceive().ApplyReward(Arg.Any<IReadOnlyList<BoundStatement.Action>>());
    }
}
