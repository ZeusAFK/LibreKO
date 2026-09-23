using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GmSummonTests : GameTestBase
{
    private const int KecoonBandit = 7000;

    private static UserSession GameMaster(ServiceProvider provider, ushort room)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: 500 + room, accountId: 600 + room);
        session.Name = $"Gm{room}";
        session.IsGM = true;
        session.ZoneId = 21;
        session.Room = room;
        session.X = 464;
        session.Z = 523;
        sessions.Regions.AddToRegion(session);
        return session;
    }

    [Fact]
    public async Task MonsummonSpawnsNonRespawningMonstersAtTheGameMasterInTheirRoom()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetNpc(KecoonBandit).Returns(new NpcData
            {
                Id = KecoonBandit, Name = "Kecoon Bandit", IsMonster = true, Hp = 800, ActType = 1,
            }));
        var gm = GameMaster(provider, room: 3);
        var bystander = GameMaster(provider, room: 0);
        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        await coordinator.HandleGmCommandAsync(gm, "+monsummon 7000 3");

        var regions = provider.GetRequiredService<SessionManager>().Regions;
        var bandits = regions.GetNearbyNpcs(gm).Where(n => n.NpcId == KecoonBandit).ToList();
        bandits.Should().HaveCount(3);
        bandits.Should().OnlyContain(n => n.IsAlive && !n.CanRespawn && n.Room == 3 && n.ZoneId == 21);
        regions.GetNearbyNpcs(bystander).Should().BeEmpty();
    }

    [Fact]
    public async Task MonsummonDefaultsToOneAndRefusesUnknownIds()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetNpc(KecoonBandit).Returns(new NpcData
            {
                Id = KecoonBandit, Name = "Kecoon Bandit", IsMonster = true, Hp = 800, ActType = 1,
            }));
        var gm = GameMaster(provider, room: 0);
        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();
        var regions = provider.GetRequiredService<SessionManager>().Regions;

        await coordinator.HandleGmCommandAsync(gm, "+monsummon 424242");
        regions.GetNearbyNpcs(gm).Should().BeEmpty();

        await coordinator.HandleGmCommandAsync(gm, "+monsummon 7000");
        regions.GetNearbyNpcs(gm).Should().ContainSingle(n => n.NpcId == KecoonBandit);
    }
}
