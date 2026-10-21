using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class TargetVisibilityTests : GameTestBase
{
    private const byte Moradon = 21;
    private const byte RonarkLand = (byte)ZoneId.RonarkLand;
    private const byte HpPoll = 0;
    private const float FarAway = 100 + RegionManager.RegionSize * 4;

    [Fact]
    public async Task TheHpOfAPlayerInAnotherZoneIsNotAnswered()
    {
        using var provider = CreateProvider(_ => { });
        var viewer = CreatePlayer(provider, 1300, AccountNation.Karus, Moradon, x: 100);
        var target = CreatePlayer(provider, 1301, AccountNation.ElMorad, RonarkLand, x: 100);

        await PollHpAsync(provider, viewer, target.Session.CharacterId);

        viewer.Sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_TARGET_HP);
    }

    [Fact]
    public async Task TheHpOfAPlayerOutOfViewIsNotAnswered()
    {
        using var provider = CreateProvider(_ => { });
        var viewer = CreatePlayer(provider, 1302, AccountNation.Karus, Moradon, x: 100);
        var target = CreatePlayer(provider, 1303, AccountNation.ElMorad, Moradon, x: FarAway);

        await PollHpAsync(provider, viewer, target.Session.CharacterId);

        viewer.Sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_TARGET_HP);
    }

    [Fact]
    public async Task TheHpOfAStealthedEnemyIsNotAnswered()
    {
        using var provider = CreateProvider(_ => { });
        var viewer = CreatePlayer(provider, 1304, AccountNation.Karus, Moradon, x: 100);
        var target = CreatePlayer(provider, 1305, AccountNation.ElMorad, Moradon, x: 101);
        target.Session.Invisibility = InvisibilityType.DispelOnAttack;

        await PollHpAsync(provider, viewer, target.Session.CharacterId);

        viewer.Sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_TARGET_HP);
    }

    [Fact]
    public async Task TheHpOfAPlayerInViewIsAnswered()
    {
        using var provider = CreateProvider(_ => { });
        var viewer = CreatePlayer(provider, 1306, AccountNation.Karus, Moradon, x: 100);
        var target = CreatePlayer(provider, 1307, AccountNation.ElMorad, Moradon, x: 101);

        await PollHpAsync(provider, viewer, target.Session.CharacterId);

        var reply = viewer.Sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_TARGET_HP);
        reply.ResetOffset();
        reply.ReadInt().Should().Be(target.Session.CharacterId);
    }

    [Fact]
    public async Task AStrangerCannotLocateAPlayer()
    {
        using var provider = CreateProvider(_ => { });
        var viewer = CreatePlayer(provider, 1308, AccountNation.Karus, Moradon, x: 100);
        var target = CreatePlayer(provider, 1309, AccountNation.ElMorad, Moradon, x: 101);
        target.Session.Hp = 0;

        await LocateCorpseAsync(provider, viewer, target.Session.CharacterId);

        viewer.Sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CORPSE);
    }

    [Fact]
    public async Task ALivingPartyMemberCannotBeLocatedAsACorpse()
    {
        using var provider = CreateProvider(_ => { });
        var viewer = CreatePlayer(provider, 1310, AccountNation.Karus, Moradon, x: 100);
        var member = CreatePlayer(provider, 1311, AccountNation.Karus, Moradon, x: 101);
        viewer.Session.PartyIndex = member.Session.PartyIndex = 7;

        await LocateCorpseAsync(provider, viewer, member.Session.CharacterId);

        viewer.Sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CORPSE);
    }

    [Fact]
    public async Task ADeadPartyMemberInTheSameZoneCanBeLocated()
    {
        using var provider = CreateProvider(_ => { });
        var viewer = CreatePlayer(provider, 1312, AccountNation.Karus, Moradon, x: 100);
        var member = CreatePlayer(provider, 1313, AccountNation.Karus, Moradon, x: FarAway);
        viewer.Session.PartyIndex = member.Session.PartyIndex = 7;
        member.Session.Hp = 0;

        await LocateCorpseAsync(provider, viewer, member.Session.CharacterId);

        var reply = viewer.Sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CORPSE);
        reply.ResetOffset();
        reply.ReadShort().Should().Be((short)member.Session.CharacterId);
    }

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent);

    private static async Task PollHpAsync(IServiceProvider provider, Player viewer, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_TARGET_HP);
        packet.WriteInt(targetId);
        packet.WriteByte(HpPoll);
        await provider.GetRequiredService<ICombatPacketCoordinator>().HandleTargetHpAsync(viewer.Client, packet);
    }

    private static async Task LocateCorpseAsync(IServiceProvider provider, Player viewer, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_CORPSE);
        packet.WriteShort((short)targetId);
        await provider.GetRequiredService<IMiscPacketCoordinator>().HandleCorpseAsync(viewer.Client, packet);
    }

    private static Player CreatePlayer(
        IServiceProvider provider, int characterId, AccountNation nation, byte zoneId, float x)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1000);
        session.Name = $"Seen{characterId}";
        session.Nation = nation;
        session.ZoneId = zoneId;
        session.Hp = 100;
        session.MaxHp = 100;
        session.X = x;
        session.Z = 100;
        sessionManager.Regions.AddToRegion(session);
        return new Player(session, client, sent);
    }
}
