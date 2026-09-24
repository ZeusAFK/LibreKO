using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestMissionArchiveTests
{
    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static QuestProgram Compose(string file, int npc, int zone)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath(file));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose("archive", npc, zone, [compilation.Program]);
    }

    private static IReadOnlyList<DialogLine> Greet(QuestProgram program, IQuestHost host)
    {
        IReadOnlyList<DialogLine>? said = null;
        DialogLine? header = null;
        host.When(h => h.Say(Arg.Any<IReadOnlyList<DialogLine>>())).Do(c => said = c.Arg<IReadOnlyList<DialogLine>>());
        host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => header = c.ArgAt<DialogLine>(2));
        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        return said ?? (header is null ? [] : [header]);
    }

    [Theory]
    [InlineData("32608_1.quest", 32608, 1, 184, 900074000, 2)]
    [InlineData("32553_71.quest", 32553, 71, 297, 0, 2)]
    public void TheEnemyArchiveOffersTheExchangeToAnInfiltratorOnTheMission(string file, int npc, int zone, int quest, int item, int nation)
    {
        var program = Compose(file, npc, zone);
        var host = Substitute.For<IQuestHost>();
        host.PlayerNation.Returns(nation);
        host.PlayerZone.Returns(zone);
        host.QuestStatus(quest).Returns(1);
        host.ItemCount(item).Returns(1);
        host.HasEffect(490119).Returns(true);
        var lines = Greet(program, host);
        string.Join(" ", lines.Select(l => l.Text)).Should().Contain("haven't found anything");
    }

    [Fact]
    public void TheArchiveOfTheInfiltratorsOwnNationStaysShut()
    {
        var program = Compose("32608_1.quest", 32608, 1);
        var host = Substitute.For<IQuestHost>();
        host.PlayerNation.Returns(1);
        host.PlayerZone.Returns(1);
        host.QuestStatus(184).Returns(1);
        host.ItemCount(900074000).Returns(1);
        string.Join(" ", Greet(program, host).Select(l => l.Text)).Should().Contain("well kept");
    }
}
