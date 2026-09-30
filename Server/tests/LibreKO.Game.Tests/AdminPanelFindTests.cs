using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AdminPanelFindTests
{
    private const byte ReqFind = 14;
    private const byte ReqGo = 15;
    private const byte AckResult = 0x11;
    private const byte AckFind = 0x15;
    private const byte Moradon = 21;
    private const byte RonarkLand = 71;

    private sealed record Hit(int Id, int SpawnRow, string Name, short Level, byte Zone, ushort X, ushort Z, byte Flags);

    private static (AdminPanelPacketCoordinator Coordinator, UserSession Gm, IClient Client, List<Packet> Sent, IZoneTransitionService Zones, IWorldMovementService Movement) Arrange()
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var gm = sessions.CreateSession(client, 1, 1);
        gm.Name = "Zeus";
        gm.IsGM = true;
        gm.ZoneId = Moradon;
        gm.Level = 83;

        var other = Substitute.For<IClient>();
        other.Id.Returns(Guid.NewGuid());
        var rin = sessions.CreateSession(other, 2, 2);
        rin.Name = "Rin";
        rin.Level = 83;
        rin.ZoneId = RonarkLand;
        rin.X = 640;
        rin.Z = 920;

        sessions.Regions.SpawnNpc(new NpcInstance { NpcId = 31741, Name = "[Trader] Julia", Level = 80, ZoneId = Moradon, X = 769, Z = 369 });
        sessions.Regions.SpawnNpc(new NpcInstance { NpcId = 10024, Name = "Spirit of Hero", Level = 80, ZoneId = RonarkLand, X = 1534, Z = 1018, IsMonster = true });
        sessions.Regions.SpawnNpc(new NpcInstance { NpcId = 10024, Name = "Spirit of Hero", Level = 80, ZoneId = RonarkLand, X = 438, Z = 1043, IsMonster = true });

        var gameData = Substitute.For<IGameDataService>();
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
        {
            [Moradon] = new ZoneInfoData { MapName = "Moradon" },
            [RonarkLand] = new ZoneInfoData { MapName = "Ronark Land" },
        });
        var zones = Substitute.For<IZoneTransitionService>();
        var movement = Substitute.For<IWorldMovementService>();
        var coordinator = new AdminPanelPacketCoordinator(
            sessions,
            gameData,
            Substitute.For<IUserNotificationService>(),
            Substitute.For<ICombatNotificationService>(),
            zones,
            movement,
            Substitute.For<INpcSpawnRowService>(),
            Substitute.For<INpcSpawnRowStore>(),
            Substitute.For<IHostEnvironment>(),
            Substitute.For<ICollectionRaceService>(),
            Substitute.For<IPlayerProgressionService>(),
            Substitute.For<ILoyaltyService>(),
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new GameServerSettings()),
            Substitute.For<ILogger<AdminPanelPacketCoordinator>>());
        return (coordinator, gm, client, sent, zones, movement);
    }

    private static Packet FindRequest(byte kind, string query)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(ReqFind);
        packet.WriteByte(kind);
        packet.WriteUtf8String(query);
        packet.ResetOffset();
        return packet;
    }

    private static Packet GoRequest(byte zone, ushort x, ushort z)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(ReqGo);
        packet.WriteByte(zone);
        packet.WriteUShort(x);
        packet.WriteUShort(z);
        packet.ResetOffset();
        return packet;
    }

    private static bool IsFindReply(Packet packet)
    {
        packet.ResetOffset();
        return packet.ReadByte() == AckFind;
    }

    private static (byte Kind, int Total, List<Hit> Hits) ReadFind(List<Packet> sent)
    {
        var packet = sent.Single(IsFindReply);
        packet.ResetOffset();
        packet.ReadByte();
        var kind = packet.ReadByte();
        int total = packet.ReadUShort();
        int count = packet.ReadUShort();
        var hits = new List<Hit>();
        for (var i = 0; i < count; i++)
            hits.Add(new Hit(packet.ReadInt(), packet.ReadInt(), packet.ReadSByteString(), packet.ReadShort(), packet.ReadByte(),
                packet.ReadUShort(), packet.ReadUShort(), packet.ReadByte()));
        return (kind, total, hits);
    }

    private static (bool Ok, string Message) ReadResult(List<Packet> sent)
    {
        var packet = sent.Single(p => { p.ResetOffset(); return p.ReadByte() == AckResult; });
        packet.ResetOffset();
        packet.ReadByte();
        return (packet.ReadByte() == AdminPanelPacketWriter.Granted, packet.ReadString());
    }

    [Fact]
    public async Task ANpcIsFoundByPartOfItsName()
    {
        var (coordinator, _, client, sent, _, _) = Arrange();

        await coordinator.HandleAsync(client, FindRequest(AdminPanelPacketWriter.FindNpcs, "juli"));

        var (kind, total, hits) = ReadFind(sent);
        kind.Should().Be(AdminPanelPacketWriter.FindNpcs);
        total.Should().Be(1);
        hits.Should().ContainSingle().Which.Should().Be(new Hit(31741, 0, "[Trader] Julia", 80, Moradon, 769, 369, 0));
    }

    [Fact]
    public async Task MonstersAreFoundByIdAndFlaggedAndSortedByZoneAndSpot()
    {
        var (coordinator, _, client, sent, _, _) = Arrange();

        await coordinator.HandleAsync(client, FindRequest(AdminPanelPacketWriter.FindMonsters, "10024"));

        var (_, total, hits) = ReadFind(sent);
        total.Should().Be(2);
        hits.Select(h => (h.X, h.Flags)).Should().Equal((438, AdminPanelPacketWriter.FindMonsterFlag), (1534, AdminPanelPacketWriter.FindMonsterFlag));
        hits.Should().OnlyContain(h => h.Zone == RonarkLand && h.Name == "Spirit of Hero");
    }

    [Fact]
    public async Task TheNpcSearchLeavesMonstersOutAndTheMonsterSearchLeavesNpcsOut()
    {
        var (coordinator, _, client, sent, _, _) = Arrange();

        await coordinator.HandleAsync(client, FindRequest(AdminPanelPacketWriter.FindNpcs, "spirit"));
        ReadFind(sent).Hits.Should().BeEmpty();
        sent.Clear();

        await coordinator.HandleAsync(client, FindRequest(AdminPanelPacketWriter.FindMonsters, "juli"));
        ReadFind(sent).Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task PlayersAreFoundByPartOfTheirName()
    {
        var (coordinator, _, client, sent, _, _) = Arrange();

        await coordinator.HandleAsync(client, FindRequest(AdminPanelPacketWriter.FindPlayers, "ri"));

        var (kind, _, hits) = ReadFind(sent);
        kind.Should().Be(AdminPanelPacketWriter.FindPlayers);
        hits.Should().ContainSingle().Which.Should().Be(new Hit(2, 0, "Rin", 83, RonarkLand, 640, 920, 0));
    }

    [Fact]
    public async Task AnEmptySearchIsRefused()
    {
        var (coordinator, _, client, sent, _, _) = Arrange();

        await coordinator.HandleAsync(client, FindRequest(AdminPanelPacketWriter.FindNpcs, "   "));

        ReadResult(sent).Ok.Should().BeFalse();
        sent.Any(IsFindReply).Should().BeFalse();
    }

    [Fact]
    public async Task GoInTheCurrentZoneWarpsInPlaceWithoutAZoneChange()
    {
        var (coordinator, gm, client, sent, zones, movement) = Arrange();

        await coordinator.HandleAsync(client, GoRequest(Moradon, 769, 369));

        ReadResult(sent).Ok.Should().BeTrue();
        await movement.Received(1).WarpAsync(gm, 7690, 3690);
        await zones.DidNotReceive().ChangeZoneAsync(Arg.Any<UserSession>(), Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task GoToAnotherZoneChangesZone()
    {
        var (coordinator, gm, client, sent, zones, movement) = Arrange();

        await coordinator.HandleAsync(client, GoRequest(RonarkLand, 1534, 1018));

        var (ok, message) = ReadResult(sent);
        ok.Should().BeTrue();
        message.Should().Contain("Ronark Land").And.Contain("1534, 1018");
        await zones.Received(1).ChangeZoneAsync(gm, RonarkLand, 1534f, 1018f);
        await movement.DidNotReceive().WarpAsync(Arg.Any<UserSession>(), Arg.Any<ushort>(), Arg.Any<ushort>());
    }

    [Fact]
    public async Task GoRefusesAZoneTheServerDoesNotHave()
    {
        var (coordinator, gm, client, sent, zones, _) = Arrange();

        await coordinator.HandleAsync(client, GoRequest(99, 10, 10));

        ReadResult(sent).Ok.Should().BeFalse();
        await zones.DidNotReceive().ChangeZoneAsync(gm, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
    }
}
