using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class FamiliarTrainerTests
{
    private const int Kate = 13016;
    private const int Moradon = 21;
    private const int FamiliarQuest = 78;
    private const string HatchingTopic = "Familiar Hatching and transform";

    private static string QuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static QuestProgram Kates()
    {
        var programs = new[] { "13016_21.quest", "13016_21_78.quest" }.Select(file =>
        {
            var compilation = QuestCompilation.CreateFromFile(QuestPath(file));
            compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
            return compilation.Program;
        }).ToList();
        return QuestProgramComposer.Compose("kate", Kate, Moradon, programs);
    }

    private static IQuestHost Player(int questStatus)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(20);
        host.PlayerZone.Returns(Moradon);
        host.PlayerNation.Returns(1);
        host.QuestStatus(FamiliarQuest).Returns(questStatus);
        return host;
    }

    private static IReadOnlyList<DialogButton> GreetingButtons(QuestProgram program, IQuestHost host)
    {
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        new QuestInterpreter(program, host).Run(greeting).Failure.Should().BeNull();
        return (IReadOnlyList<DialogButton>)host.ReceivedCalls()
            .Last(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[3]!;
    }

    [Fact]
    public void KateOpensTheHatchWindowOnceHerQuestIsDone()
    {
        var program = Kates();
        var host = Player(questStatus: 2);

        var hatching = GreetingButtons(program, host).Single(b => b.Label.Text == HatchingTopic);
        new QuestInterpreter(program, host).Run(hatching.TargetEvent).Failure.Should().BeNull();

        host.Received(1).OpenFamiliarPanel();
    }

    [Fact]
    public void BeforeHerQuestKateOffersNoHatching()
    {
        var program = Kates();
        var host = Player(questStatus: 0);

        GreetingButtons(program, host).Should().NotContain(b => b.Label.Text == HatchingTopic);
    }

    [Fact]
    public void TheHatchWindowIsSelectMessageStyleNine()
    {
        var packet = NpcDialogPacketWriter.FamiliarPanel(Kate, "13016_21.quest");

        packet.ResetOffset();
        packet.ReadInt().Should().Be(Kate);
        packet.ReadByte().Should().Be(NpcDialogPacketWriter.FamiliarPanelStyle);
        NpcDialogPacketWriter.FamiliarPanelStyle.Should().Be(9, "the 2619 client opens CUINpcPetUpgrade for SELECT_MSG style 9");
    }
}
