using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestNationRewardsTests
{
    private const string EmblemScript = """
        Bind Npc 24203 Zone 1 for karus
        Bind Npc 14203 Zone 2 for elmorad
        Quest 217 "Collect Enemy Emblem"
            Journal "Bring the emblem to Drake."
            Collect 3 of 910090000 for karus
            Collect 3 of 910091000 for elmorad

        Requires player level >= 68

        Rewards
            Give 50000000 experience
        """;

    private const string LetterScript = """
        Bind Npc 13009 Zone 21
        Quest 437 "The Beginning of a New Adventure 1"
            Journal "Seek out the captain of your castle."

        Requires player level >= 35

        Rewards for karus
            Give 1 of 910181000

        Rewards for elmorad
            Give 1 of 910180000
        """;

    private static QuestProgram Compose(string source, string name, int npc, int zone)
    {
        var compilation = QuestCompilation.Create(source, name);
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose(name, npc, zone, [compilation.Program]);
    }

    private static IQuestHost HostOf(int nation, int classGroup = 1)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerNation.Returns(nation);
        host.PlayerClassGroup.Returns(classGroup);
        host.PlayerZone.Returns(nation);
        return host;
    }

    [Theory]
    [InlineData(1, 910090000)]
    [InlineData(2, 910091000)]
    public void ANationScopedCollectSplitsOneRewardsBlockByNation(int nation, int emblem)
    {
        var program = Compose(EmblemScript, "emblem.quest", 24203, 1);
        program.QuestRewards.Should().HaveCount(2);
        program.QuestRewards.Select(r => r.Nation).Should().BeEquivalentTo([1, 2]);

        var rewards = program.RewardsFor(217, 1, nation)!;
        rewards.Nation.Should().Be(nation);
        rewards.Transfers.Where(a => a.Kind == QuestActionKind.TakeItem)
            .Select(a => a.Arguments.GetInt("item")).Should().Equal(emblem);
        rewards.Transfers.Should().ContainSingle(a => a.Kind == QuestActionKind.GiveExperience);
    }

    [Theory]
    [InlineData(1, 910181000)]
    [InlineData(2, 910180000)]
    public void RewardsForANationPaysThatNationOnly(int nation, int letter)
    {
        var program = Compose(LetterScript, "letter.quest", 13009, 21);
        program.QuestRewards.Select(r => r.Nation).Should().BeEquivalentTo([1, 2]);
        var rewards = program.RewardsFor(437, 3, nation)!;
        rewards.Transfers.Should().ContainSingle(a =>
            a.Kind == QuestActionKind.GiveItem && a.Arguments.GetInt("item") == letter);
    }

    [Theory]
    [InlineData(1, 910181000)]
    [InlineData(2, 910180000)]
    public void TheClaimPaysThePlayersOwnNation(int nation, int letter)
    {
        var program = Compose(LetterScript, "letter.quest", 13009, 21);
        program.TryGetEntry(QuestProgram.FulfilEvent, 437, out var panel).Should().BeTrue();
        var host = HostOf(nation);
        host.PlayerZone.Returns(21);
        host.QuestStatus(437).Returns(1);
        new QuestInterpreter(program, host).Run(panel).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 1 && actions[0].Arguments.GetInt("item") == letter));
        host.Received(1).SetQuestState(437, 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TheClaimPaysThePlayersOwnClass(int classGroup)
    {
        var program = Compose("""
            Bind Npc 100 Zone 21
            Quest 9 "Split"
                Journal "One quest, two payouts."

            Rewards for warrior
                Give 1 of 111

            Rewards for rogue
                Give 1 of 222
            """, "split.quest", 100, 21);
        program.TryGetEntry(QuestProgram.FulfilEvent, 9, out var panel).Should().BeTrue();
        var host = HostOf(1, classGroup);
        host.PlayerZone.Returns(21);
        host.QuestStatus(9).Returns(1);
        new QuestInterpreter(program, host).Run(panel).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 1 && actions[0].Arguments.GetInt("item") == (classGroup == 1 ? 111 : 222)));
    }

    [Fact]
    public void ANationAndAClassMayScopeOneBlockTogether()
    {
        var program = Compose("""
            Bind Npc 100 Zone 21
            Quest 9 "Split"
                Journal "Four payouts."
                Collect 1 of 500 for karus warrior

            Rewards for karus warrior
                Give 1 of 111

            Rewards for elmorad warrior
                Give 1 of 222

            Rewards for karus rogue
                Give 1 of 333

            Rewards for elmorad rogue
                Give 1 of 444
            """, "split.quest", 100, 21);
        program.QuestRewards.Should().HaveCount(4);
        var karusWarrior = program.RewardsFor(9, 1, 1)!;
        karusWarrior.Transfers.Select(a => a.Arguments.GetInt("item")).Should().Equal(500, 111);
        program.RewardsFor(9, 1, 2)!.Transfers.Select(a => a.Arguments.GetInt("item")).Should().Equal(222);
        program.RewardsFor(9, 2, 2)!.Transfers.Select(a => a.Arguments.GetInt("item")).Should().Equal(444);
    }

    [Theory]
    [InlineData("Rewards for karus\n    Give 1 of 1\nRewards for karus\n    Give 1 of 2")]
    [InlineData("Rewards for karus\n    Give 1 of 1\nRewards\n    Give 1 of 2")]
    [InlineData("Rewards for karus\n    Give 1 of 1\nRewards for warrior\n    Give 1 of 2")]
    [InlineData("Rewards for karus elmorad\n    Give 1 of 1")]
    [InlineData("Rewards for nowhere\n    Give 1 of 1")]
    [InlineData("Rewards for karus\n    Give 1 of 1\nRewards for elmorad\n    Give 1 of 2\nRewards for karus warrior\n    Give 1 of 3")]
    public void RewardScopesAreOneKindNamedOnce(string blocks)
    {
        var source = "Bind Npc 100 Zone 21\nQuest 9 \"Split\"\n    Journal \"x\"\n\n" + blocks;
        QuestCompilation.Create(source, "split.quest").Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Collect 1 of 500 for nowhere")]
    [InlineData("Collect 1 of 500 for karus elmorad")]
    [InlineData("Collect 1 of 500 for")]
    public void ACollectScopeNamesANationOrAClass(string collect)
    {
        var source = "Bind Npc 100 Zone 21\nQuest 9 \"Split\"\n    Journal \"x\"\n    " + collect + "\n\nRewards none";
        QuestCompilation.Create(source, "split.quest").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void RewardlessDeliveriesStillTakeEachNationsOwnItem()
    {
        var program = Compose("""
            Bind Npc 21510 Zone 1 for karus
            Bind Npc 11510 Zone 2 for elmorad
            Quest 438 "The Beginning of a New Adventure 1"
                Journal "Hand the letter to the captain."
                Collect 1 of 910181000 for karus
                Collect 1 of 910180000 for elmorad

            Requires player level >= 35

            Rewards none
            """, "captains.quest", 21510, 1);
        program.QuestRewards.Should().HaveCount(2);
        program.RewardsFor(438, 0, 1)!.Transfers.Single().Arguments.GetInt("item").Should().Be(910181000);
        program.RewardsFor(438, 0, 2)!.Transfers.Single().Arguments.GetInt("item").Should().Be(910180000);
    }
}
