using System.Text.Json;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Runtime.CompilerServices;

namespace LibreKO.Game.Tests;

public class QuestClanTrialTests
{
    private const int Coliseum = (int)ZoneId.CaitharosArena;
    private const int AbyssOfHell = (int)ZoneId.IsiloonArena;
    private const int CaveOfTheFireDragon = (int)ZoneId.FelankorArena;
    private const int TrialSet = 1;
    private const int Caitharos = 2690;
    private const int Isiloon = 5701;
    private const int Felankor = 5702;
    private const int Judge = 13010;
    private const int EnvoyOfConfession = 18030;
    private const int KeyOfHonor = 910045000;
    private const int EmblemOfHonor = 389221000;
    private const int RoyalEmblem = 389222000;
    private const string Challenge = "Yes, I'll level a challenge.";

    private static string BakedQuestPath(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static string SeedPath(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Seed", "Data", name));

    private static IReadOnlyList<DialogButton> Shown(IQuestHost host) =>
        (IReadOnlyList<DialogButton>)host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[3]!;

    private static void Run(QuestProgram program, IQuestHost host, int eventId) =>
        new QuestInterpreter(program, host).Run(eventId).Failure.Should().BeNull();

    private static UserSession Online(SessionManager sessions, int id, byte zone, short clan, ushort room = 0)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessions.CreateSession(client, id, id);
        session.Name = $"knight{id}";
        session.ZoneId = zone;
        session.KnightsId = clan;
        session.Room = room;
        session.X = 100f + id;
        session.Z = 200f + id;
        return session;
    }

    [Fact]
    public void TeleportClanCompilesWithAndWithoutASpotAndReachesTheHost()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            On greeting
                Say "The coliseum awaits."
                Topic "Go" do
                    Teleport clan to 54 at 150 150
                    Teleport clan to 93
            """, "trial.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());

        var program = QuestProgramComposer.Compose("npc", 100, 21, [result.Program]);
        var host = Substitute.For<IQuestHost>();
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        Run(program, host, greeting);
        Run(program, host, Shown(host).Single().TargetEvent);

        host.Received(1).TeleportClanToZone(Coliseum, 150, 150);
        host.Received(1).TeleportClanToZone(AbyssOfHell, 0, 0);
        host.DidNotReceive().TeleportToZone(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void EnterClanInstanceCompilesWithAndWithoutASpotAndReachesTheHost()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            On greeting
                Say "The coliseum awaits."
                Topic "Go" do
                    Enter clan instance 54 set 1 at 150 150
                    Enter clan instance 93 set 2
            """, "trial.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());

        var program = QuestProgramComposer.Compose("npc", 100, 21, [result.Program]);
        var host = Substitute.For<IQuestHost>();
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        Run(program, host, greeting);
        Run(program, host, Shown(host).Single().TargetEvent);

        host.Received(1).EnterClanInstance(Coliseum, 1, 150, 150);
        host.Received(1).EnterClanInstance(AbyssOfHell, 2, 0, 0);
        host.DidNotReceive().EnterInstance(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Theory]
    [InlineData("11610.quest", 280, ClanType.Training, 3, Coliseum, 150, 150)]
    [InlineData("21610.quest", 280, ClanType.Training, 3, Coliseum, 150, 150)]
    [InlineData("11610.quest", 110, ClanType.Promoted, 1, AbyssOfHell, 63, 474)]
    [InlineData("21610.quest", 110, ClanType.Promoted, 1, AbyssOfHell, 63, 474)]
    [InlineData("11610.quest", 120, ClanType.Accredited1, 1, CaveOfTheFireDragon, 110, 20)]
    [InlineData("21610.quest", 120, ClanType.Accredited1, 1, CaveOfTheFireDragon, 110, 20)]
    public void TheKnightClerkOpensTheClansOwnRoomOfTheTrial(
        string file, int menu, ClanType rank, int grade, int zone, int x, int z)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath(file));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        host.IsClanLeader.Returns(true);
        host.ClanRank.Returns((int)rank);
        host.ClanGrade.Returns(grade);

        Run(compilation.Program, host, compilation.Program.EventNames[$"event_{menu}"]);
        var topics = Shown(host);
        topics.Select(t => t.Label.Text).Should().Equal(
            "I'll listen to your story further.", "I brought what you asked for.", Challenge);

        Run(compilation.Program, host, topics.Single(t => t.Label.Text == Challenge).TargetEvent);
        host.Received(1).EnterClanInstance(zone, TrialSet, x, z);
        host.DidNotReceive().TeleportClanToZone(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void TheJudgeLetsTheClanLeaveTheColiseumForMoradon()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("13010.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        compilation.Program.TryGetGreeting(out var greeting).Should().BeTrue();
        Run(compilation.Program, host, greeting);
        var topics = Shown(host);
        topics.Select(t => t.Label.Text).Should().Equal("Yes. I want to leave.", "No. I don't want to leave.");

        Run(compilation.Program, host, topics.First().TargetEvent);
        host.Received(1).TeleportToZone((int)ZoneId.Moradon, 817, 451);
    }

    [Theory]
    [InlineData(AccountNation.ElMorad, (int)ZoneId.ElMoradCamp1, 1705, 306)]
    [InlineData(AccountNation.Karus, (int)ZoneId.KarusCamp1, 360, 1742)]
    public void TheEnvoyOfConfessionSendsTheClanHomeByNation(AccountNation nation, int zone, int x, int z)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("18030.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        host.PlayerNation.Returns((int)nation);
        compilation.Program.TryGetGreeting(out var greeting).Should().BeTrue();
        Run(compilation.Program, host, greeting);
        var topics = Shown(host);
        topics.Select(t => t.Label.Text).Should().Equal("Yes. I want to leave.", "No. I don't want to leave.");

        Run(compilation.Program, host, topics.First().TargetEvent);
        host.Received(1).TeleportToZone(zone, x, z);
    }

    [Theory]
    [InlineData("13010.quest")]
    [InlineData("18030.quest")]
    public void TheArenaExitScriptsAreInTheManifest(string file)
    {
        var manifest = JsonSerializer.Deserialize<string[]>(File.ReadAllText(BakedQuestPath("quest-manifest.json")));
        manifest.Should().Contain(file, "an exit NPC that is not in the manifest says nothing");
    }

    [Theory]
    [InlineData("NpcPositions.zone054.json", Caitharos, Judge)]
    [InlineData("NpcPositions.zone093.json", Isiloon, EnvoyOfConfession)]
    [InlineData("NpcPositions.zone094.json", Felankor, EnvoyOfConfession)]
    public void TheTrialBossAndItsExitNpcLiveInTheRoomSetNotTheSharedZone(string file, int boss, int exit)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SeedPath(file)));
        var rows = document.RootElement.EnumerateArray()
            .Select(row => (NpcId: row.GetProperty("NpcId").GetInt32(), Room: row.GetProperty("Room").GetInt32()))
            .ToList();

        rows.Should().Contain((boss, TrialSet));
        rows.Should().Contain((exit, TrialSet));
        rows.Should().OnlyContain(row => row.Room == TrialSet, "a row in the shared zone would spawn a boss two clans could fight over");
    }

    [Theory]
    [InlineData("Items.slot17.json", KeyOfHonor)]
    [InlineData("Items.slot15.json", EmblemOfHonor)]
    [InlineData("Items.slot15.json", RoyalEmblem)]
    public void TheTrialItemsCannotChangeHands(string file, int item)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SeedPath(file)));
        var row = document.RootElement.EnumerateArray().Single(r => r.GetProperty("Num").GetInt32() == item);
        var data = new ItemData { Num = item, Race = row.GetProperty("Race").GetByte() };

        data.IsUntradeable.Should().BeTrue("whoever picks the key up is the one who hands it in");
        ExchangeTransferService.IsTradableItem(data, item, 0).Should().BeFalse();
    }

    [Fact]
    public async Task TheTrialRoomTakesEveryOnlineClanMemberWhoIsFree()
    {
        var sessions = new SessionManager();
        const short Knights = 7;
        var chief = Online(sessions, 1, (byte)ZoneId.Moradon, Knights);
        var mate = Online(sessions, 2, (byte)ZoneId.ElMoradCamp1, Knights);
        var atWar = Online(sessions, 3, (byte)ZoneId.BorderDefenseWar, Knights);
        var busy = Online(sessions, 4, (byte)ZoneId.Moradon, Knights, room: 9);
        var stranger = Online(sessions, 5, (byte)ZoneId.Moradon, 8);

        var gameData = Substitute.For<IGameDataService>();
        gameData.NpcPositions.Returns(new List<NpcPosData>());
        var transitions = Substitute.For<IZoneTransitionService>();
        var registry = new InstanceRoomRegistry(sessions, Substitute.For<ILogger<InstanceRoomRegistry>>());
        var service = new InstanceEntryService(
            sessions, gameData, Substitute.For<IMonsterAggressionPolicy>(), transitions, registry,
            Substitute.For<ILogger<InstanceEntryService>>());

        await service.EnterClanAsync(chief, (byte)Coliseum, TrialSet, 150, 150);

        var room = registry.Rooms.Should().ContainSingle().Subject;
        room.ZoneId.Should().Be((byte)Coliseum);
        room.Set.Should().Be(TrialSet);
        room.Members.Keys.Should().BeEquivalentTo([chief.CharacterId, mate.CharacterId]);
        chief.Room.Should().Be(room.Id);
        mate.Room.Should().Be(room.Id);
        atWar.Room.Should().Be(0);
        busy.Room.Should().Be(9);
        stranger.Room.Should().Be(0);

        await transitions.Received(1).ChangeZoneAsync(chief, (byte)Coliseum, 150, 150);
        await transitions.Received(1).ChangeZoneAsync(mate, (byte)Coliseum, 150, 150);
        await transitions.DidNotReceive().ChangeZoneAsync(atWar, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        await transitions.DidNotReceive().ChangeZoneAsync(busy, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        await transitions.DidNotReceive().ChangeZoneAsync(stranger, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());

        chief.InstanceReturn.Should().Be(((byte)ZoneId.Moradon, chief.X, chief.Z));
        mate.InstanceReturn.Should().Be(((byte)ZoneId.ElMoradCamp1, mate.X, mate.Z), "every member goes back to their own spot");
    }

    [Fact]
    public async Task TwoClansGetTwoRoomsWithTheirOwnBoss()
    {
        var sessions = new SessionManager();
        var firstChief = Online(sessions, 1, (byte)ZoneId.Moradon, 7);
        var secondChief = Online(sessions, 2, (byte)ZoneId.Moradon, 8);

        var caitharos = new NpcPosData
        {
            Index = 1, ZoneId = (byte)Coliseum, NpcId = Caitharos, ActType = 1, LeftX = 119, TopZ = 128, NumNPC = 1, Room = TrialSet,
        };
        var gameData = Substitute.For<IGameDataService>();
        gameData.NpcPositions.Returns(new List<NpcPosData> { caitharos });
        gameData.GetSpawnProto(caitharos).Returns(new NpcData { Id = Caitharos, Name = "Caitharos", IsMonster = true, Hp = 250_000 });
        var registry = new InstanceRoomRegistry(sessions, Substitute.For<ILogger<InstanceRoomRegistry>>());
        var service = new InstanceEntryService(
            sessions, gameData, Substitute.For<IMonsterAggressionPolicy>(), Substitute.For<IZoneTransitionService>(),
            registry, Substitute.For<ILogger<InstanceEntryService>>());

        await service.EnterClanAsync(firstChief, (byte)Coliseum, TrialSet, 150, 150);
        await service.EnterClanAsync(secondChief, (byte)Coliseum, TrialSet, 150, 150);

        registry.Rooms.Should().HaveCount(2);
        firstChief.Room.Should().NotBe(secondChief.Room);
        foreach (var room in registry.Rooms)
        {
            var boss = room.Npcs.Should().ContainSingle().Subject;
            boss.NpcId.Should().Be(Caitharos);
            boss.Room.Should().Be(room.Id);
            boss.RespawnType.Should().Be(NpcRespawnType.Never);
        }
    }

    [Fact]
    public async Task AClanlessPlayerOpensNoRoom()
    {
        var sessions = new SessionManager();
        var loner = Online(sessions, 1, (byte)ZoneId.Moradon, 0);
        var transitions = Substitute.For<IZoneTransitionService>();
        var registry = new InstanceRoomRegistry(sessions, Substitute.For<ILogger<InstanceRoomRegistry>>());
        var service = new InstanceEntryService(
            sessions, Substitute.For<IGameDataService>(), Substitute.For<IMonsterAggressionPolicy>(), transitions, registry,
            Substitute.For<ILogger<InstanceEntryService>>());

        await service.EnterClanAsync(loner, (byte)Coliseum, TrialSet, 150, 150);

        registry.Rooms.Should().BeEmpty();
        loner.Room.Should().Be(0);
        await transitions.DidNotReceive().ChangeZoneAsync(Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task TheScriptsClanInstanceEntryReachesTheRoomService()
    {
        var sessions = new SessionManager();
        var chief = Online(sessions, 1, (byte)ZoneId.Moradon, 7);
        var gameData = Substitute.For<IGameDataService>();
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData> { [(short)Coliseum] = new() { ZoneNo = (short)Coliseum } });
        var entry = Substitute.For<IInstanceEntryService>();
        var provider = new ServiceCollection().AddSingleton(sessions).AddSingleton(entry).BuildServiceProvider();

        var context = new QuestScriptContext(chief, null, gameData, sessions, Substitute.For<ILogger>(), 1);
        context.RequestClanInstance(Coliseum, TrialSet, 150, 150);
        await new ScriptEffectApplier(
                gameData, Substitute.For<ICharacterStatePersister>(), provider, Substitute.For<ILogger<ScriptEffectApplier>>())
            .ApplyAsync(chief, context, "trial.quest");

        await entry.Received(1).EnterClanAsync(chief, (byte)Coliseum, TrialSet, 150, 150);
        await entry.DidNotReceive().EnterAsync(Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<short>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task TheClanTeleportMovesEveryOnlineClanMemberExceptThoseInATempleEvent()
    {
        var sessions = new SessionManager();
        const short Knights = 7;
        var chief = Online(sessions, 1, (byte)ZoneId.Moradon, Knights);
        var mate = Online(sessions, 2, (byte)ZoneId.ElMoradCamp1, Knights);
        var alreadyThere = Online(sessions, 3, (byte)Coliseum, Knights);
        var atWar = Online(sessions, 4, (byte)ZoneId.BorderDefenseWar, Knights);
        var stranger = Online(sessions, 5, (byte)ZoneId.Moradon, 8);

        var gameData = Substitute.For<IGameDataService>();
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
        {
            [(short)Coliseum] = new() { ZoneNo = (short)Coliseum, InitX = 1390, InitZ = 1390 },
        });
        var transitions = Substitute.For<IZoneTransitionService>();
        var movement = Substitute.For<IWorldPacketCoordinator>();
        var provider = new ServiceCollection()
            .AddSingleton(sessions)
            .AddSingleton(transitions)
            .AddSingleton(movement)
            .BuildServiceProvider();

        var context = new QuestScriptContext(chief, null, gameData, sessions, Substitute.For<ILogger>(), 1);
        context.RequestClanZoneChange(Coliseum, 150, 150);
        var applier = new ScriptEffectApplier(
            gameData, Substitute.For<ICharacterStatePersister>(), provider,
            Substitute.For<ILogger<ScriptEffectApplier>>());
        await applier.ApplyAsync(chief, context, "trial.quest");

        await transitions.Received(1).ChangeZoneAsync(chief, (byte)Coliseum, 150, 150);
        await transitions.Received(1).ChangeZoneAsync(mate, (byte)Coliseum, 150, 150);
        await movement.Received(1).WarpAsync(alreadyThere, 1500, 1500);
        await transitions.DidNotReceive().ChangeZoneAsync(atWar, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        await transitions.DidNotReceive().ChangeZoneAsync(stranger, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        await movement.DidNotReceive().WarpAsync(atWar, Arg.Any<ushort>(), Arg.Any<ushort>());
    }

    [Fact]
    public async Task AClanlessPlayerIsNotMovedByTheClanTeleport()
    {
        var sessions = new SessionManager();
        var loner = Online(sessions, 1, (byte)ZoneId.Moradon, 0);

        var gameData = Substitute.For<IGameDataService>();
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
        {
            [(short)Coliseum] = new() { ZoneNo = (short)Coliseum },
        });
        var transitions = Substitute.For<IZoneTransitionService>();
        var provider = new ServiceCollection()
            .AddSingleton(sessions)
            .AddSingleton(transitions)
            .AddSingleton(Substitute.For<IWorldPacketCoordinator>())
            .BuildServiceProvider();

        var context = new QuestScriptContext(loner, null, gameData, sessions, Substitute.For<ILogger>(), 1);
        context.RequestClanZoneChange(Coliseum, 150, 150);
        await new ScriptEffectApplier(
                gameData, Substitute.For<ICharacterStatePersister>(), provider,
                Substitute.For<ILogger<ScriptEffectApplier>>())
            .ApplyAsync(loner, context, "trial.quest");

        await transitions.DidNotReceive().ChangeZoneAsync(
            Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
    }
}
