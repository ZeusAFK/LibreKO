using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class GenieTownTests : GameTestBase
{
    private const short TownX = 512;
    private const int GenieSeconds = 30 * 60;
    private const int StunRow = 110560;

    [Fact]
    public async Task HomeStopsARunningGenie()
    {
        using var provider = CreateProvider(_ => { }, StartInTown);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (player, playerSent) = Join(sessions, characterId: 900, x: 100);
        var (_, nearbySent) = Join(sessions, characterId: 901, x: 110);
        player.GenieTime.Load(GenieSeconds);
        player.GenieActive = true;

        await provider.GetRequiredService<IWorldMovementService>().HandleHomeAsync(player.Client);

        player.GenieActive.Should().BeFalse();
        player.X.Should().BeApproximately(TownX, 0.5f);
        int stopped = playerSent.FindIndex(p => IsGenie(p, GenieSystemPacketWriter.Stop));
        int warp = playerSent.FindIndex(p => p.GetOpcode() == (byte)GameOpcodes.GS_WARP);
        stopped.Should().BeGreaterThanOrEqualTo(0);
        stopped.Should().BeLessThan(warp, "the Genie stops before the warp reaches the client");
        var off = nearbySent.Single(p => IsGenie(p, GenieSystemPacketWriter.Activated));
        off.ReadInt().Should().Be(player.CharacterId);
        off.ReadByte().Should().Be(GenieSystemPacketWriter.Inactive);
    }

    [Fact]
    public async Task HomeWithoutGenieSendsNoGeniePackets()
    {
        using var provider = CreateProvider(_ => { }, StartInTown);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (player, playerSent) = Join(sessions, characterId: 900, x: 100);
        var (_, nearbySent) = Join(sessions, characterId: 901, x: 110);
        player.GenieTime.Load(GenieSeconds);

        await provider.GetRequiredService<IWorldMovementService>().HandleHomeAsync(player.Client);

        player.X.Should().BeApproximately(TownX, 0.5f);
        playerSent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_GENIE_SYSTEM);
        nearbySent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_GENIE_SYSTEM);
    }

    [Fact]
    public async Task ARefusedHomeKeepsTheGenieRunning()
    {
        using var provider = CreateProvider(_ => { }, StartInTown);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (player, playerSent) = Join(sessions, characterId: 900, x: 100);
        player.GenieTime.Load(GenieSeconds);
        player.GenieActive = true;
        player.Hp = (short)(player.MaxHp / 2 - 1);

        await provider.GetRequiredService<IWorldMovementService>().HandleHomeAsync(player.Client);

        player.GenieActive.Should().BeTrue();
        playerSent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_GENIE_SYSTEM);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HomeIsRefusedUnderNoRecallOrWhileHeld(bool noRecall, bool stunned)
    {
        using var provider = CreateProvider(_ => { }, StartInTown);
        var sessions = provider.GetRequiredService<SessionManager>();
        var (player, _) = Join(sessions, characterId: 900, x: 100);
        player.CanTeleport = !noRecall;
        if (stunned)
            player.SetBuff(StunRow, new ActiveBuff
            {
                MagicId = StunRow, CasterId = 1, BuffType = BuffType.Stun, Duration = 2, ExpireTicks = DateTime.UtcNow.AddSeconds(2).Ticks,
            });

        await provider.GetRequiredService<IWorldMovementService>().HandleHomeAsync(player.Client);

        player.X.Should().BeApproximately(100, 0.5f);
    }

    private static void StartInTown(IGameDataService gameData) =>
        gameData.GetStartPosition((byte)ZoneId.Moradon).Returns(new StartPositionData
        {
            ZoneId = (byte)ZoneId.Moradon,
            KarusX = TownX,
            KarusZ = TownX,
            ElmoradX = TownX,
            ElmoradZ = TownX
        });

    private static (UserSession Session, List<Packet> Sent) Join(SessionManager sessions, int characterId, float x)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId, accountId: characterId);
        session.Nation = AccountNation.Karus;
        session.ZoneId = (byte)ZoneId.Moradon;
        session.X = x;
        session.Z = 100;
        session.Hp = 500;
        session.MaxHp = 500;
        sessions.Regions.AddToRegion(session);
        return (session, sent);
    }

    private static bool IsGenie(Packet packet, byte command)
    {
        if (packet.GetOpcode() != (byte)GameOpcodes.GS_GENIE_SYSTEM) return false;
        packet.ResetOffset();
        return packet.ReadByte() == GenieSystemPacketWriter.InfoRequest && packet.ReadByte() == command;
    }
}
