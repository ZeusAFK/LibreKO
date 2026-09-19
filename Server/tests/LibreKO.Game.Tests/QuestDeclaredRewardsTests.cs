using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestDeclaredRewardsTests
{
    [Fact]
    public void OneRewardDeclarationSuppliesTheInlineAndImplicitPanelClaim()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 100
            Quest 1
            Rewards
                Take 2 of 123
                Give 100 coins
            On greeting
                Say "Here is your reward."
                Topic "Claim" do
                    Claim quest
            """, "rewards.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("combined", 100, 0, [compilation.Program]);
        program.QuestRewards.Should().ContainSingle();
        program.QuestRewards[0].Transfers.Should().HaveCount(2);
        program.TryGetEntry(QuestProgram.FulfilEvent, 1, out var panel).Should().BeTrue();
        var inline = program.Events.Values.Single(e => e.Name is null).Id;
        foreach (var entry in new[] { inline, panel })
        {
            var host = Substitute.For<IQuestHost>();
            host.QuestStatus(1).Returns(1);
            new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
            host.Received(1).ApplyReward(program.QuestRewards[0].Transfers);
            host.Received(1).SetQuestState(1, 2);
        }
    }

    private const string Choice = """
        Bind Npc 100
        Quest 1
        Rewards
            Give 100 coins
            Choose one
                Give 1 of 972280737
                Give 1 of 972350825
        On greeting
            Say "Pick your prize."
            Topic "Claim" do
                Claim quest
        """;

    [Fact]
    public void AChoiceOfRewardsIsAppliedOnlyWithThePlayersPick()
    {
        var compilation = QuestCompilation.Create(Choice, "choice.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("choice", 100, 0, [compilation.Program]);
        var rewards = program.QuestRewards.Single();
        rewards.Transfers.Should().ContainSingle(a => a.Kind == QuestActionKind.GiveGold);
        rewards.Options.Select(o => o.Arguments.GetInt("item")).Should().Equal(972280737, 972350825);
        program.TryGetEntry(QuestProgram.FulfilEvent, 1, out var panel).Should().BeTrue();

        var host = Substitute.For<IQuestHost>();
        host.QuestStatus(1).Returns(1);
        new QuestInterpreter(program, host, 1).Run(panel).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 2 && actions[0].Kind == QuestActionKind.GiveGold
            && actions[1].Arguments.GetInt("item") == 972350825));
        host.Received(1).SetQuestState(1, 2);

        foreach (var unchosen in new[] { -1, 2 })
        {
            var refused = Substitute.For<IQuestHost>();
            refused.QuestStatus(1).Returns(1);
            new QuestInterpreter(program, refused, unchosen).Run(panel).Failure.Should().Contain("none was chosen");
            refused.DidNotReceiveWithAnyArgs().ApplyReward(default!);
            refused.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
        }
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(3, 250)]
    public void PremiumExperiencePaysTheLargerAmountOnlyToAPremiumPlayer(int premiumType, int expected)
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 100 Zone 21
            Quest 1 "Feathers"
                Journal "Bring five feathers."
                Collect 5 of 810295000
            Requires player level >= 1
            Rewards
                Give 100 experience or 250 with premium
            On offer
                Show quest "Bring me feathers."
            """, "premium.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("premium", 100, 21, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(1);
        host.PremiumType.Returns(premiumType);
        host.QuestStatus(1).Returns(1);
        host.ItemCount(810295000).Returns(5);

        var interpreter = new QuestInterpreter(program, host);
        var view = interpreter.BuildView(1);
        view.Rewards.Transfers.Single(a => a.Kind == QuestActionKind.GiveExperience)
            .Arguments.GetInt("amount").Should().Be(expected);

        program.TryGetEntry(QuestProgram.FulfilEvent, 1, out var panel).Should().BeTrue();
        new QuestInterpreter(program, host).Run(panel).Failure.Should().BeNull();
        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Single(a => a.Kind == QuestActionKind.GiveExperience).Arguments.GetInt("amount") == expected));
    }

    [Theory]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Choose one\n        Give 1 of 972280737\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Choose one\n        Take 1 of 972280737\n        Give 1 of 972350825\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Choose one\n        Give 1 of 972280737\n        Promote\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Choose one\n        Give 1 of 972280737\n        Give 1 of 972350825\n    Choose one\n        Give 1 coins\n        Give 2 coins\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Give 1 coins\nOn greeting\n    Choose one\n        Give 1 of 972280737\n        Give 1 of 972350825\n")]
    public void AChoiceNeedsTwoOrMoreGivesAndLivesOnlyInRewards(string source)
    {
        QuestCompilation.Create(source, "bad.quest").Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Bind Npc 100\nQuest 1\nOn fulfil\n    Claim quest\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Give 1 coins\nOn fulfil\n    Claim 2\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Complete quest\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRewards\n    Give 1 coins\nRewards\n    Give 2 coins\n")]
    public void MissingCrossQuestOrNonTransferRewardDeclarationsAreRejected(string source)
    {
        QuestCompilation.Create(source, "bad.quest").Succeeded.Should().BeFalse();
    }
}
