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

    private static UserSession Player(ServiceProvider provider, string name, int id, float x, float z)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: id, accountId: id + 1000);
        session.Name = name;
        session.ZoneId = 21;
        session.X = x;
        session.Z = z;
        sessions.Regions.AddToRegion(session);
        return session;
    }

    [Theory]
    [InlineData("bot*", "bot_0001", true)]
    [InlineData("bot*", "BOT_0042", true)]
    [InlineData("bot*", "robot", false)]
    [InlineData("*_01", "rin_01", true)]
    [InlineData("b*t*", "bat_x", true)]
    [InlineData("*", "anyone", true)]
    [InlineData("a.b*", "axb", false)]
    public void SummonPatternsMatchWholeNamesWithStarAsAnyText(string glob, string name, bool matches)
    {
        AdminPacketCoordinator.NamePattern(glob).IsMatch(name).Should().Be(matches);
    }

    [Fact]
    public async Task SummonWithAPatternBringsOnlyTheMatchingPlayers()
    {
        using var provider = CreateProvider(_ => { }, _ => { });
        var gm = GameMaster(provider, room: 0);
        var bot1 = Player(provider, "bot_0001", 901, 900, 900);
        var bot2 = Player(provider, "bot_0002", 902, 100, 100);
        var player = Player(provider, "Rikka", 903, 700, 700);
        var coordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        await coordinator.HandleGmCommandAsync(gm, "+summon bot*");

        foreach (var bot in new[] { bot1, bot2 })
            MathF.Sqrt((bot.X - gm.X) * (bot.X - gm.X) + (bot.Z - gm.Z) * (bot.Z - gm.Z)).Should().BeLessThan(5f);
        player.X.Should().Be(700);
        player.Z.Should().Be(700);
    }

    [Fact]
    public void BotsCommandCarriesTheCountLevelsAndTheGameMastersPosition()
    {
        using var provider = CreateProvider(_ => { }, _ => { });
        var gm = GameMaster(provider, room: 0);

        AdminPacketCoordinator.BotsSizeCommand(gm, 100, ["100", "60", "80"]).Should().Be("size 100 lvl 60 80 at 21 464 523");
        AdminPacketCoordinator.BotsSizeCommand(gm, 5, ["5"]).Should().Be("size 5 at 21 464 523");
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
