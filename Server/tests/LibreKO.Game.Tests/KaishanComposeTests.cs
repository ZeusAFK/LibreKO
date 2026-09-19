using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using Xunit;

namespace LibreKO.Game.Tests;

public class KaishanComposeTests
{
    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    [Fact]
    public void KaishansLoadedScriptsComposeIntoOneGreeting()
    {
        var programs = new List<QuestProgram>();
        foreach (var file in new[] { "18004_21.quest", "18004_21_661.quest", "18004_21_71.quest", "18004_21_72.quest" })
        {
            var compilation = QuestCompilation.CreateFromFile(BakedQuestPath(file));
            compilation.Succeeded.Should().BeTrue(file + ": " + compilation.RenderDiagnostics());
            programs.Add(compilation.Program);
        }

        var act = () => QuestProgramComposer.Compose("kaishan", 18004, 21, programs);
        var program = act.Should().NotThrow().Subject;
        program.TryGetGreeting(out _).Should().BeTrue();
    }
}
