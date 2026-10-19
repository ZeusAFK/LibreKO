using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class KillStreakTests
{
    private readonly SessionManager sessions = new();
    private readonly Dictionary<int, List<Packet>> sent = [];
    private readonly KillStreakService service;

    public KillStreakTests()
    {
        service = new KillStreakService(sessions, Substitute.For<ILogger<KillStreakService>>());
    }

    private UserSession Player(int id, AccountNation nation, byte zone = 71)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var log = sent[id] = [];
        client.SendPacket(Arg.Do<Packet>(p => log.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId: id, accountId: id * 10);
        session.Name = $"Player{id}";
        session.Nation = nation;
        session.ZoneId = zone;
        return session;
    }

    private static (byte Kind, int Id, string Name, byte Nation, ushort Count) Read(Packet packet)
    {
        packet.ResetOffset();
        packet.ReadByte().Should().Be(KillAssistPacketWriter.KillSub);
        return (packet.ReadByte(), packet.ReadInt(), packet.ReadString(), packet.ReadByte(), packet.ReadUShort());
    }

    [Fact]
    public async Task EachKillRaisesTheKillersOwnSplash()
    {
        var killer = Player(1, AccountNation.Karus);
        var victim = Player(2, AccountNation.ElMorad);

        await service.OnPlayerKilledAsync(victim, killer);
        await service.OnPlayerKilledAsync(victim, killer);

        var last = Read(sent[1].Last());
        last.Kind.Should().Be(KillAssistPacketWriter.OwnStreak);
        last.Name.Should().Be(killer.Name);
        last.Nation.Should().Be((byte)AccountNation.Karus);
        last.Count.Should().Be(2);
    }

    [Fact]
    public async Task DyingEndsTheStreakAndSaysSo()
    {
        var killer = Player(1, AccountNation.Karus);
        var victim = Player(2, AccountNation.ElMorad);
        await service.OnPlayerKilledAsync(victim, killer);

        await service.OnPlayerDiedAsync(killer);

        service.StreakOf(killer.CharacterId).Should().Be(0);
        Read(sent[1].Last()).Count.Should().Be(0);
        await service.OnPlayerDiedAsync(victim);
        sent[2].Should().BeEmpty();
    }

    [Fact]
    public async Task EveryTenthKillIsAnnouncedInTheZone()
    {
        var killer = Player(1, AccountNation.Karus);
        var victim = Player(2, AccountNation.ElMorad);
        var bystander = Player(3, AccountNation.ElMorad);
        var elsewhere = Player(4, AccountNation.ElMorad, zone: 21);

        for (int i = 0; i < KillStreakService.AnnouncementStep; i++)
            await service.OnPlayerKilledAsync(victim, killer);

        var heard = Read(sent[3].Single());
        heard.Kind.Should().Be(KillAssistPacketWriter.StreakAnnouncement);
        heard.Count.Should().Be((ushort)KillStreakService.AnnouncementStep);
        sent[4].Should().BeEmpty();
    }
}
