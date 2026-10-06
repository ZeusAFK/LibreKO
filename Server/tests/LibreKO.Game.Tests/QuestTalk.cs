using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

internal sealed class QuestTalk
{
    public const int Moradon = 21;

    private readonly QuestProgram program;
    public readonly IQuestHost Host;
    public IReadOnlyList<DialogButton> Shown = [];
    public DialogLine? Header;

    public QuestTalk(int npc, int nation, params int[] carried) : this($"{npc}_{Moradon}.quest", null, npc, nation, carried)
    {
    }

    public QuestTalk(string file, string? startEvent, int npc, int nation, params int[] carried)
        : this(file, startEvent, npc, nation, null, carried)
    {
    }

    public QuestTalk(string file, string? startEvent, int npc, int nation, Action<IQuestHost>? arrange, params int[] carried)
        : this([file], startEvent, npc, nation, arrange, carried)
    {
    }

    public QuestTalk(IReadOnlyList<string> files, string? startEvent, int npc, int nation, Action<IQuestHost>? arrange, params int[] carried)
    {
        var programs = files.Select(file =>
        {
            var compilation = QuestCompilation.CreateFromFile(QuestPath(file));
            compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
            return compilation.Program;
        }).ToList();
        program = QuestProgramComposer.Compose("vendor", npc, Moradon, programs);
        Host = Substitute.For<IQuestHost>();
        Host.PlayerZone.Returns(Moradon);
        Host.PlayerLevel.Returns(70);
        Host.PlayerNation.Returns(nation);
        foreach (var item in carried)
            Host.ItemCount(item).Returns(1);
        Host.HasRoomForItem(Arg.Any<int>(), Arg.Any<int>()).Returns(true);
        Host.CanReceiveStacks(Arg.Any<int>()).Returns(true);
        Host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c =>
            {
                Header = c.ArgAt<DialogLine>(2);
                Shown = c.ArgAt<IReadOnlyList<DialogButton>>(3);
            });
        arrange?.Invoke(Host);
        int start;
        if (startEvent == null)
            program.TryGetGreeting(out start).Should().BeTrue();
        else
            program.EventNames.TryGetValue(startEvent, out start).Should().BeTrue();
        new QuestInterpreter(program, Host).Run(start).Failure.Should().BeNull();
    }

    public QuestTalk Follow(params string[] labels)
    {
        foreach (var label in labels)
        {
            var button = Shown.Single(b => b.Label.Text == label);
            new QuestInterpreter(program, Host).Run(button.TargetEvent).Failure.Should().BeNull();
        }
        return this;
    }

    public IReadOnlyList<string> Labels => Shown.Select(b => b.Label.Text ?? string.Empty).ToList();

    public static string QuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));
}
