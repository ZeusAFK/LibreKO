using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PartyLeaderRightsTests : GameTestBase
{
    private const byte Moradon = 21;

    [Fact]
    public async Task AMemberWhoAsksToDisbandOnlyLeaves()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var leader = CreateMember(sessions, 410);
        var member = CreateMember(sessions, 411);
        var third = CreateMember(sessions, 412);
        var party = PartyOf(sessions, leader, member, third);

        await provider.GetRequiredService<IPartyPacketCoordinator>().HandleAsync(member.Client, Request(PartyRequest.Delete));

        sessions.Parties.GetParty(party.Index).Should().NotBeNull();
        party.LeaderId.Should().Be((short)leader.CharacterId);
        party.FindMember((short)member.CharacterId).Should().BeNegative();
        member.PartyIndex.Should().Be(-1);
        third.PartyIndex.Should().Be(party.Index);
    }

    [Fact]
    public async Task APlayerWhoWasOnlyInvitedCannotDisbandTheParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var leader = CreateMember(sessions, 420);
        var member = CreateMember(sessions, 421);
        var invitee = CreateMember(sessions, 422);
        var party = PartyOf(sessions, leader, member);
        invitee.InvitedToParty = party.Index;

        await provider.GetRequiredService<IPartyPacketCoordinator>().HandleAsync(invitee.Client, Request(PartyRequest.Delete));

        sessions.Parties.GetParty(party.Index).Should().NotBeNull();
        leader.PartyIndex.Should().Be(party.Index);
        member.PartyIndex.Should().Be(party.Index);
    }

    [Fact]
    public async Task OnlyTheLeaderInvitesIntoAParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var leader = CreateMember(sessions, 430);
        var member = CreateMember(sessions, 431);
        var stranger = CreateMember(sessions, 432);
        PartyOf(sessions, leader, member, CreateMember(sessions, 433));

        await provider.GetRequiredService<IPartyPacketCoordinator>()
            .HandleAsync(member.Client, Request(PartyRequest.Insert, stranger.Name));

        stranger.PartyIndex.Should().Be(-1);
        stranger.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_PARTY, "no invite was sent");
    }

    [Fact]
    public async Task TheLeaderStillDisbandsAndInvites()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var leader = CreateMember(sessions, 440);
        var member = CreateMember(sessions, 441);
        var stranger = CreateMember(sessions, 442);
        var party = PartyOf(sessions, leader, member, CreateMember(sessions, 443));
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        await coordinator.HandleAsync(leader.Client, Request(PartyRequest.Insert, stranger.Name));
        stranger.InvitedToParty.Should().Be(party.Index);
        stranger.IsInParty.Should().BeFalse("an invite is not membership until it is accepted");

        await coordinator.HandleAsync(leader.Client, Request(PartyRequest.Delete));
        sessions.Parties.GetParty(party.Index).Should().BeNull();
        member.PartyIndex.Should().Be(-1);
    }

    [Fact]
    public async Task AMembersDeclineDoesNotTakeThemOutOfTheParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var leader = CreateMember(sessions, 444);
        var member = CreateMember(sessions, 445);
        var party = PartyOf(sessions, leader, member, CreateMember(sessions, 446));

        await provider.GetRequiredService<IPartyPacketCoordinator>().HandleAsync(member.Client, Permit(accept: false));

        member.PartyIndex.Should().Be(party.Index, "a member who never left stays in the party, so they cannot join a second one");
        party.FindMember((short)member.CharacterId).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OnlyThePartysLeaderPromotes()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var leader = CreateMember(sessions, 447);
        var member = CreateMember(sessions, 448);
        var party = PartyOf(sessions, leader, member);
        member.IsPartyLeader = true;

        await provider.GetRequiredService<IPartyPacketCoordinator>().HandleAsync(member.Client, Promote(member.CharacterId));

        party.LeaderId.Should().Be((short)leader.CharacterId, "a leftover leader flag is not the party's leader");
    }

    [Fact]
    public async Task OnlyThePartysLeaderAppointsTheCommander()
    {
        using var provider = CreateProvider(_ => { });
        var sessions = provider.GetRequiredService<SessionManager>();
        var leader = CreateMember(sessions, 449);
        var commander = CreateMember(sessions, 450);
        var third = CreateMember(sessions, 451);
        var party = PartyOf(sessions, leader, commander, third);
        party.CommandLeaderId = (short)commander.CharacterId;

        await provider.GetRequiredService<IPartyPacketCoordinator>().HandleAsync(commander.Client, CommandPromote(third.CharacterId));

        party.CommandLeaderId.Should().Be((short)commander.CharacterId);
    }

    private static Packet Permit(bool accept)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)PartyRequest.Permit);
        packet.WriteByte(accept ? (byte)1 : (byte)0);
        packet.ResetOffset();
        return packet;
    }

    private static Packet Promote(int characterId)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)PartyRequest.Promote);
        packet.WriteInt(characterId);
        packet.ResetOffset();
        return packet;
    }

    private static Packet CommandPromote(int characterId)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)PartyRequest.CommandPromote);
        packet.WriteInt(characterId);
        packet.ResetOffset();
        return packet;
    }

    private static PartyGroup PartyOf(SessionManager sessions, UserSession leader, params UserSession[] members)
    {
        var party = sessions.Parties.CreateParty((short)leader.CharacterId);
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        for (var i = 0; i < members.Length; i++)
        {
            party.MemberIds[i + 1] = (short)members[i].CharacterId;
            members[i].PartyIndex = party.Index;
        }
        return party;
    }

    private static Packet Request(PartyRequest request, string? targetName = null)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)request);
        if (targetName != null)
            packet.WriteString(targetName);
        packet.ResetOffset();
        return packet;
    }

    private static UserSession CreateMember(SessionManager sessions, int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessions.CreateSession(client, characterId, accountId: characterId + 100);
        session.Name = $"Player{characterId}";
        session.Class = 105;
        session.Level = 40;
        session.Nation = AccountNation.Karus;
        session.ZoneId = Moradon;
        session.X = 100;
        session.Z = 100;
        session.Hp = 300;
        session.MaxHp = 300;
        sessions.Regions.AddToRegion(session);
        return session;
    }
}
