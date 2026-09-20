using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Runtime.CompilerServices;

namespace LibreKO.Game.Tests;

public class QuestSummonTests
{
    private const int SuspiciousSmuggler = 1211;
    private const int GuardOfBlackMarketer = 9666;

    private static string BakedQuestPath(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static IReadOnlyList<DialogButton> Shown(IQuestHost host) =>
        (IReadOnlyList<DialogButton>)host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[3]!;

    private static void Run(QuestProgram program, IQuestHost host, int eventId) =>
        new QuestInterpreter(program, host).Run(eventId).Failure.Should().BeNull();

    [Fact]
    public void SummonCompilesWithAndWithoutASpotAndReachesTheHost()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            On greeting
                Say "Guys! Come out!"
                Topic "Fight" do
                    Summon 2 of 9666
                    Summon 1 of 9666 at 467 519
            """, "summon.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("npc", 100, 21, [result.Program]);
        var host = Substitute.For<IQuestHost>();
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        Run(program, host, greeting);
        Run(program, host, Shown(host).Single().TargetEvent);
        host.Received(1).SummonNpc(GuardOfBlackMarketer, 2, 0, 0);
        host.Received(1).SummonNpc(GuardOfBlackMarketer, 1, 467, 519);
    }

    [Fact]
    public void TheBlackMarketerInterrogationEndsInTwoGuardsOnlyWhileTheSmugglerQuestIsOpen()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("25022_21.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("marketer", 25022, 21, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerNation.Returns(1);
        host.QuestStatus(SuspiciousSmuggler).Returns(1);
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        Run(program, host, greeting);
        foreach (var label in new[] { "Suspicious amsangin", "Shows the secret book.", "Were pirates and pestle?" })
            Run(program, host, Shown(host).Single(b => b.Label.Text == label).TargetEvent);
        host.Received(1).PlayEffect(300435);
        Run(program, host, Shown(host).Single(b => b.Label.Text.StartsWith("No. - By pirates")).TargetEvent);
        host.Received(1).PlayEffect(300438);
        host.Received(1).SummonNpc(GuardOfBlackMarketer, 1, 467, 519);
        host.Received(1).SummonNpc(GuardOfBlackMarketer, 1, 469, 523);

        var idle = Substitute.For<IQuestHost>();
        idle.PlayerZone.Returns(21);
        idle.PlayerNation.Returns(1);
        Run(program, idle, greeting);
        Shown(idle).Should().BeEmpty();
        idle.DidNotReceiveWithAnyArgs().SummonNpc(default, default, default, default);
    }

    [Fact]
    public async Task ASummonedGuardStandsInTheWorldAndIsAnnouncedToThePlayersAroundIt()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        session.ZoneId = 21;
        session.X = 464;
        session.Z = 523;
        sessions.Regions.AddToRegion(session);
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetNpc(GuardOfBlackMarketer).Returns(new NpcData
        {
            Id = GuardOfBlackMarketer, Name = "Guard of Black Marketer", IsMonster = true, Hp = 1373, ActType = 5,
        });
        var context = new QuestScriptContext(session, null, gameData, sessions, Substitute.For<ILogger>(), 1);
        context.RequestSummon(GuardOfBlackMarketer, 1, 467, 519);
        var provider = new ServiceCollection()
            .AddSingleton(sessions)
            .AddSingleton(Substitute.For<IMonsterAggressionPolicy>())
            .AddSingleton<INpcLifecycleService>(new NpcLifecycleService(sessions, Substitute.For<ILogger<NpcLifecycleService>>()))
            .BuildServiceProvider();

        await new ScriptEffectApplier(gameData, Substitute.For<ICharacterStatePersister>(), provider,
                Substitute.For<ILogger<ScriptEffectApplier>>())
            .ApplyAsync(session, context, "25022_21.quest");

        var guard = sessions.Regions.GetNearbyNpcs(session).Should().ContainSingle(n => n.NpcId == GuardOfBlackMarketer).Subject;
        guard.IsAlive.Should().BeTrue();
        guard.CanRespawn.Should().BeFalse();
        await client.Received(1).SendPacket(
            Arg.Is<Packet>(p => p.GetOpcode() == (byte)GameOpcodes.GS_NPC_INOUT),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheDispatchOfficerSailsToMoraIslandOnlyWhenAsked()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("29001_21.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("portman", 29001, 21, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerNation.Returns(1);
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        Run(program, host, greeting);
        var topics = Shown(host);
        topics.Select(b => b.Label.Text).Should().Equal("Move to the island!", "No");
        topics.Single(b => b.Label.Text == "No").TargetEvent.Should().Be(-1, "No closes the dialog on the client");
        Run(program, host, topics.Single(b => b.Label.Text == "Move to the island!").TargetEvent);
        host.Received(1).TeleportToZone(21, 780, 52);
    }

    [Fact]
    public void AmeliesSceneFulfilsJedsErrandAndJedWaitsUntilThen()
    {
        const int BigAndShiny = 1223;
        var amelie = QuestCompilation.CreateFromFile(BakedQuestPath("25021_21.quest"));
        amelie.Succeeded.Should().BeTrue(amelie.RenderDiagnostics());
        var scene = QuestProgramComposer.Compose("amelie", 25021, 21, [amelie.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerNation.Returns(1);
        host.QuestStatus(BigAndShiny).Returns(1);
        scene.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        Run(scene, host, greeting);
        var replies = new[] { "Big, shiny", "YesThe mind wants to convey.", "(Suddenly appeared Jigsaw)",
            "Next ", "Next ", "Next ", "Next ", "Next ", "Next ", "Next ", "I got in the end! What is this girl?" };
        foreach (var label in replies)
            Run(scene, host, Shown(host).Single(b => b.Label.Text == label).TargetEvent);
        host.Received(1).SetQuestState(BigAndShiny, 3);

        var jed = QuestCompilation.CreateFromFile(BakedQuestPath("25001_21_1223.quest"));
        jed.Succeeded.Should().BeTrue(jed.RenderDiagnostics());
        var errand = QuestProgramComposer.Compose("jed", 25001, 21, [jed.Program]);
        errand.TryGetEntry(QuestProgram.ViewEvent, BigAndShiny, out var view).Should().BeTrue();
        foreach (var (status, expected) in new[] { (1, QuestViewState.InProgress), (3, QuestViewState.Claimable) })
        {
            var player = Substitute.For<IQuestHost>();
            player.PlayerZone.Returns(21);
            player.PlayerNation.Returns(1);
            player.PlayerLevel.Returns(24);
            player.QuestStatus(BigAndShiny).Returns(status);
            player.QuestStatus(1222).Returns(2);
            Run(errand, player, view);
            var page = (QuestView)player.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ShowQuestView").GetArguments()[0]!;
            page.State.Should().Be(expected, $"status {status}: Jed turns the errand in only once Amelie has taken the ring");
        }
    }

    [Fact]
    public void AFinishedPunishmentReleasesThePrisonerToMoradonFromItsCompletedPage()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("18010_92_813.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("kabal", 18010, 92, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(92);
        host.PlayerNation.Returns(1);
        host.PlayerLevel.Returns(1);
        host.QuestStatus(813).Returns(2);
        program.TryGetEntry(QuestProgram.ViewEvent, 813, out var view).Should().BeTrue();
        Run(program, host, view);
        var page = (QuestView)host.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ShowQuestView").GetArguments()[0]!;
        page.State.Should().Be(QuestViewState.Completed);
        page.Dialogue.Text.Should().StartWith("Mission completed.");
        Run(program, host, page.Topics.Single(b => b.Label.Text == "Oh, right.").TargetEvent);
        host.Received(1).TeleportToZone(21, 817, 448);
    }
}
