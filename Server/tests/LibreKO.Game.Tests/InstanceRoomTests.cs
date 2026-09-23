using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class InstanceRoomTests
{
    private const byte SuppressionZone = 81;

    private static UserSession Session(int characterId, byte zone, ushort room)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        return new UserSession(client, characterId, characterId + 1000)
        {
            Name = $"Player{characterId}", ZoneId = zone, Room = room, X = 200f, Z = 200f,
        };
    }

    [Fact]
    public void RoomsPartitionTheSameSpotOfTheSameZone()
    {
        var regions = new RegionManager();
        var first = Session(1, SuppressionZone, 1);
        var second = Session(2, SuppressionZone, 2);
        var third = Session(3, SuppressionZone, 1);
        foreach (var session in new[] { first, second, third })
            regions.AddToRegion(session);

        regions.GetNearbyUsers(first).Should().ContainSingle().Which.Should().BeSameAs(third);
        regions.GetNearbyUsers(second).Should().BeEmpty();

        var monster = regions.SpawnNpc(new NpcInstance { NpcId = 7003, ZoneId = SuppressionZone, Room = 2, X = 200f, Z = 200f, Hp = 1 });
        regions.GetNearbyNpcs(second).Should().ContainSingle().Which.Should().BeSameAs(monster);
        regions.GetNearbyNpcs(first).Should().BeEmpty();
        regions.GetNearbyUsersForNpc(monster).Should().ContainSingle().Which.Should().BeSameAs(second);

        regions.RemoveNpc(monster);
        regions.GetNearbyNpcs(second).Should().BeEmpty();
        regions.GetNpc(monster.UniqueId).Should().BeNull();
    }

    [Fact]
    public void ARoomClosesWithItsMonstersWhenTheLastMemberLeaves()
    {
        var sessions = new SessionManager();
        var registry = new InstanceRoomRegistry(sessions, Substitute.For<ILogger<InstanceRoomRegistry>>());
        var room = registry.Open(SuppressionZone, 1, TimeSpan.FromMinutes(30));
        room.Id.Should().NotBe(0);
        registry.Holds(room.Id, SuppressionZone).Should().BeTrue();
        registry.Holds(room.Id, 21).Should().BeFalse();

        var leader = Session(10, 21, 0);
        var friend = Session(11, 21, 0);
        registry.Join(room, leader);
        registry.Join(room, friend);
        leader.Room.Should().Be(room.Id);
        var monster = sessions.Regions.SpawnNpc(new NpcInstance { NpcId = 7003, ZoneId = SuppressionZone, Room = room.Id, X = 53f, Z = 53f, Hp = 1 });
        room.Npcs.Add(monster);

        registry.Leave(leader);
        leader.Room.Should().Be(0);
        registry.Get(room.Id).Should().NotBeNull();
        sessions.Regions.GetNpc(monster.UniqueId).Should().NotBeNull();

        registry.Leave(friend);
        registry.Get(room.Id).Should().BeNull();
        sessions.Regions.GetNpc(monster.UniqueId).Should().BeNull();

        var again = registry.Open(SuppressionZone, 1, TimeSpan.FromMinutes(30));
        again.Id.Should().NotBe(room.Id);
    }

    [Fact]
    public void EnterInstanceCompilesAndReachesTheHost()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 13009
            On greeting
                Say "Do you want to form a bandit suppression squad?"
                Topic "Yes" do
                    Enter instance 81 set 1 at 204 197
                Topic "Later" do
                    Enter instance 82 set 1
            """, "instance.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("npc", 13009, 21, [result.Program]);
        var host = Substitute.For<IQuestHost>();
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        new QuestInterpreter(program, host).Run(greeting).Failure.Should().BeNull();
        var buttons = (IReadOnlyList<DialogButton>)host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[3]!;
        new QuestInterpreter(program, host).Run(buttons[0].TargetEvent).Failure.Should().BeNull();
        new QuestInterpreter(program, host).Run(buttons[1].TargetEvent).Failure.Should().BeNull();
        host.Received(1).EnterInstance(81, 1, 204, 197);
        host.Received(1).EnterInstance(82, 1, 0, 0);
    }
}
