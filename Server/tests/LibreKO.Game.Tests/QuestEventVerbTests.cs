using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestEventVerbTests
{
    private const int HellfireDragonFighter = 450;

    private static (QuestProgram Program, IQuestHost Host, int Entry) Ready(string body)
    {
        var result = QuestCompilation.Create($"""
            Bind Npc 100
            Quest 61 "Verbs"

            On fulfil
            {body}
            """, "verbs.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();
        return (result.Program, host, entry);
    }

    private static IQuestHost Run(string body)
    {
        var (program, host, entry) = Ready(body);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        return host;
    }

    [Fact]
    public void AnAwardedAchievementReachesTheHost()
    {
        Run($"    Give achievement {HellfireDragonFighter}")
            .Received().GiveAchievement(HellfireDragonFighter);
    }

    [Fact]
    public void ATempleEventSignUpReachesTheHost()
    {
        Run("    Join temple event").Received().JoinTempleEvent();
    }

    [Fact]
    public void ALevelSetReachesTheHost()
    {
        Run("    Set player level to 75").Received().SetPlayerLevel(75);
    }

    [Fact]
    public void TheJobChangePanelReachesTheHost()
    {
        Run("    Open job change panel").Received().OpenJobChangePanel();
    }

    [Fact]
    public void AMiningExchangeReachesTheHost()
    {
        Run("    Exchange mining 2").Received().RunMiningExchange(2);
    }

    [Fact]
    public void ALevelAndExperienceGateAsksTheHostForBoth()
    {
        var (program, host, entry) = Ready("""
                If player reached level 83 at 100 percent
                    Give 1 of 379156000
            """);
        host.ReachedLevel(83, 100).Returns(true);

        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();

        host.Received().ReachedLevel(83, 100);
        host.Received().GiveItem(379156000, 1, 0);
    }

    [Fact]
    public void CollectAndAcceptGrantsBecomeTheQuestsHandInAndItsStartingGift()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 25072 Zone 2
            Quest 1241 "Pocket money that cannot be spent"
                Journal "Deliver the gift."
                Give 1 of 900670000 on accept
                Collect 1 of 900659000

            Requires player level >= 35

            Rewards
                Give 300000 experience

            On offer
                Show quest "Can you deliver this gift to my mother?"

            On claimable
                Show quest "Did my mother like it?"
            """, "sadi.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("sadi", 25072, 2, [result.Program]);

        program.QuestRewards.Single().Transfers.Should().SatisfyRespectively(
            take =>
            {
                take.Kind.Should().Be(LibreKO.Quests.Binding.QuestActionKind.TakeItem);
                take.Arguments.GetInt("item").Should().Be(900659000);
            },
            give => give.Kind.Should().Be(LibreKO.Quests.Binding.QuestActionKind.GiveExperience));

        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(2);
        host.PlayerLevel.Returns(40);
        program.TryGetEntry(QuestProgram.AcceptEvent, 1241, out var accept).Should().BeTrue();
        new QuestInterpreter(program, host).Run(accept).Failure.Should().BeNull();

        host.Received(1).SetQuestState(1241, 1);
        host.Received(1).GiveItem(900670000, 1, 0);
        host.DidNotReceive().ShowQuestView(Arg.Any<QuestView>());
    }

    [Fact]
    public void ARollGateDrawsOverTheWholeRangeAndComparesIt()
    {
        var (program, host, entry) = Ready("""
                If roll of 21 < 10
                    Give 1 of 379156000
                Else
                    Give 1 of 379157000
            """);
        host.RollDice(20).Returns(9);

        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();

        host.Received().RollDice(20);
        host.Received().GiveItem(379156000, 1, 0);
        host.DidNotReceive().GiveItem(379157000, 1, 0);
    }

    [Fact]
    public void ARollGateTakesTheOtherArmWhenTheDrawIsOutside()
    {
        var (program, host, entry) = Ready("""
                If roll of 21 < 10
                    Give 1 of 379156000
                Else
                    Give 1 of 379157000
            """);
        host.RollDice(20).Returns(10);

        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();

        host.Received().GiveItem(379157000, 1, 0);
        host.DidNotReceive().GiveItem(379156000, 1, 0);
    }
}
