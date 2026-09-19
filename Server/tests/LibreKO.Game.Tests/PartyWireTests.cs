using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PartyWireTests : GameTestBase
{
    private const byte Moradon = 21;

    [Fact]
    public async Task AcceptingAnInviteIsASingleByteOnItsOwnSub()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var leader = CreateMember(sessionManager, 500);
        var invitee = CreateMember(sessionManager, 501);

        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        invitee.PartyIndex = party.Index;

        var accept = new Packet(GameOpcodes.GS_PARTY);
        accept.WriteByte((byte)PartyRequest.Permit);
        accept.WriteByte(1);

        await coordinator.HandleAsync(invitee.Client, accept);

        party.FindMember((short)invitee.CharacterId).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AnAcceptCarryingTrailingBytesIsStillRead()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var leader = CreateMember(sessionManager, 502);
        var invitee = CreateMember(sessionManager, 503);

        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        invitee.PartyIndex = party.Index;

        var accept = new Packet(GameOpcodes.GS_PARTY);
        accept.WriteByte((byte)PartyRequest.Permit);
        accept.WriteByte(1);
        accept.WriteByte(1);
        accept.WriteByte(0);

        var act = async () => await coordinator.HandleAsync(invitee.Client, accept);

        await act.Should().NotThrowAsync("a reader stops at the fields it knows");
        party.FindMember((short)invitee.CharacterId).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task LeavingAPartyNamesTheMemberWithFourBytes()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var leader = CreateMember(sessionManager, 504);
        var member = CreateMember(sessionManager, 505);
        var third = CreateMember(sessionManager, 506);

        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        party.MemberIds[2] = (short)third.CharacterId;
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        member.PartyIndex = party.Index;
        third.PartyIndex = party.Index;

        var leave = new Packet(GameOpcodes.GS_PARTY);
        leave.WriteByte((byte)PartyRequest.Remove);
        leave.WriteInt(member.CharacterId);

        await coordinator.HandleAsync(member.Client, leave);

        member.PartyIndex.Should().Be(-1);
        party.FindMember((short)member.CharacterId).Should().BeLessThan(0);
    }

    private static UserSession CreateMember(SessionManager sessionManager, int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 100);
        session.Name = $"Player{characterId}";
        session.Class = 105;
        session.Level = 40;
        session.Nation = AccountNation.Karus;
        session.ZoneId = Moradon;
        session.X = 100;
        session.Z = 100;
        session.Hp = 300;
        session.MaxHp = 300;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }
}
