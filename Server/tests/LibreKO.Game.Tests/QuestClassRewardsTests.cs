using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestClassRewardsTests
{
    private const int ShadowSeekerHunt = 126;
    private const int ElMorad = 2;
    private const int WolfTailStaff = 972030357;
    private const int WolfHuntingBlade = 972010790;

    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static QuestProgram ShadowSeeker()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("14427_2_126.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose("zalk", 14427, 2, [compilation.Program]);
    }

    private static IEnumerable<int> Offered(QuestRewards rewards) =>
        rewards.Options.Where(a => a.Kind == QuestActionKind.GiveItem).Select(a => a.Arguments.GetInt("item"));

    [Fact]
    public void AMageChoosesAmongTheMageStaffsOnly()
    {
        var rewards = ShadowSeeker().RewardsFor(ShadowSeekerHunt, QuestVocabulary.ClassGroupMage, ElMorad)!;
        Offered(rewards).Should().Contain(WolfTailStaff).And.NotContain(WolfHuntingBlade);
    }

    [Fact]
    public void AKurianIsOfferedTheWarriorWeaponsAsInTheRetailScript()
    {
        var rewards = ShadowSeeker().RewardsFor(ShadowSeekerHunt, QuestVocabulary.ClassGroupKurian, ElMorad)!;
        Offered(rewards).Should().Contain(WolfHuntingBlade);
    }
}
