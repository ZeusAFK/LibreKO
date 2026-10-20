using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
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
        invitee.InvitedToParty = party.Index;

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
        invitee.InvitedToParty = party.Index;

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

    [Theory]
    [InlineData((byte)ZoneId.RonarkLand, AccountNation.ElMorad, (byte)ZoneId.RonarkLand, PartyInviteError.OtherNation)]
    [InlineData((byte)ZoneId.RonarkLand, AccountNation.Karus, (byte)ZoneId.Ardream, PartyInviteError.OtherZone)]
    [InlineData((byte)ZoneId.Moradon, AccountNation.ElMorad, (byte)ZoneId.Moradon2, PartyInviteError.OtherZone)]
    [InlineData((byte)ZoneId.ChaosDungeon, AccountNation.Karus, (byte)ZoneId.ChaosDungeon, PartyInviteError.RegionBarred)]
    public async Task AnInviteIsRefusedWithTheRetailCode(byte zone, AccountNation targetNation, byte targetZone, short expected)
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var sent = new List<Packet>();
        var leader = CreateMember(sessionManager, 510, sent);
        leader.ZoneId = zone;
        var target = CreateMember(sessionManager, 511);
        target.Nation = targetNation;
        target.ZoneId = targetZone;

        await coordinator.HandleAsync(leader.Client, InviteOf(target));

        RejectionsIn(sent).Should().Equal(expected);
        target.PartyIndex.Should().Be(-1);
    }

    [Fact]
    public async Task TheOtherNationCanBeInvitedInMoradon()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var sent = new List<Packet>();
        var leader = CreateMember(sessionManager, 512, sent);
        var target = CreateMember(sessionManager, 513);
        target.Nation = AccountNation.ElMorad;

        await coordinator.HandleAsync(leader.Client, InviteOf(target));

        RejectionsIn(sent).Should().BeEmpty();
        target.InvitedToParty.Should().Be(leader.PartyIndex);
    }

    [Fact]
    public async Task ABoardInviteRelaysTheInviterToTheTargetWithoutJoiningThem()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var board = provider.GetRequiredService<IPartyBbsPacketCoordinator>();

        var inviter = CreateMember(sessionManager, 520);
        var targetSent = new List<Packet>();
        var target = CreateMember(sessionManager, 521, targetSent);

        await board.HandleAsync(inviter.Client, BoardPacket(PartyBbsPacketWriter.SubInvite, target.Name));

        target.PartyIndex.Should().Be(-1);
        var prompt = targetSent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_PARTY_BBS);
        prompt.ResetOffset();
        prompt.ReadByte().Should().Be(PartyBbsPacketWriter.ModeNormal);
        prompt.ReadByte().Should().Be(PartyBbsPacketWriter.SubInvite);
        prompt.ReadByte().Should().Be(PartyBbsPacketWriter.Succeeded);
        prompt.ReadString().Should().Be(inviter.Name);
        prompt.ReadByte().Should().Be(inviter.Level);
        prompt.ReadShort().Should().Be((short)inviter.Class);
    }

    [Fact]
    public async Task AcceptingABoardInviteFormsThePartyUnderTheInviter()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var board = provider.GetRequiredService<IPartyBbsPacketCoordinator>();

        var inviter = CreateMember(sessionManager, 522);
        var target = CreateMember(sessionManager, 523);

        await board.HandleAsync(inviter.Client, BoardPacket(PartyBbsPacketWriter.SubInvite, target.Name));
        await board.HandleAsync(target.Client, BoardPacket(PartyBbsPacketWriter.SubInviteAccept, inviter.Name));

        inviter.IsPartyLeader.Should().BeTrue();
        var party = sessionManager.Parties.GetParty(inviter.PartyIndex);
        party.Should().NotBeNull();
        party!.FindMember((short)target.CharacterId).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ABoardAcceptWithoutAnInviteJoinsNobody()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var board = provider.GetRequiredService<IPartyBbsPacketCoordinator>();
        var leader = CreateMember(sessionManager, 524);
        var stranger = CreateMember(sessionManager, 525);

        await board.HandleAsync(stranger.Client, BoardPacket(PartyBbsPacketWriter.SubInviteAccept, leader.Name));

        stranger.IsInParty.Should().BeFalse();
        leader.IsInParty.Should().BeFalse("nobody is put at the head of a party they did not form");
    }

    [Fact]
    public async Task ABoardInviteIsAnsweredOnce()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var board = provider.GetRequiredService<IPartyBbsPacketCoordinator>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var inviter = CreateMember(sessionManager, 526);
        var target = CreateMember(sessionManager, 527);
        await board.HandleAsync(inviter.Client, BoardPacket(PartyBbsPacketWriter.SubInvite, target.Name));
        await board.HandleAsync(target.Client, BoardPacket(PartyBbsPacketWriter.SubInviteAccept, inviter.Name));
        await parties.RemoveMemberAsync(target, (short)target.CharacterId);

        await board.HandleAsync(target.Client, BoardPacket(PartyBbsPacketWriter.SubInviteAccept, inviter.Name));

        target.IsInParty.Should().BeFalse();
    }

    [Fact]
    public async Task ASecondInviteWaitsUntilTheFirstIsAnswered()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var first = CreateMember(sessionManager, 530);
        var secondSent = new List<Packet>();
        var second = CreateMember(sessionManager, 531, secondSent);
        var target = CreateMember(sessionManager, 532);

        await parties.HandleAsync(first.Client, InviteOf(target));
        await parties.HandleAsync(second.Client, InviteOf(target));
        await parties.HandleAsync(target.Client, Permit(accept: true));

        target.PartyIndex.Should().Be(first.PartyIndex, "the answer goes to the invite the player saw first");
        second.IsInParty.Should().BeFalse("a refused inviter is not left in a party the client never shows");
        RejectionsIn(secondSent).Should().Contain((short)PartyInviteError.CannotParty);
    }

    [Fact]
    public async Task AnInviteWhosePartyIsGoneStandsInNoOnesWay()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var first = CreateMember(sessionManager, 533);
        var second = CreateMember(sessionManager, 534);
        var target = CreateMember(sessionManager, 535);
        await parties.HandleAsync(first.Client, InviteOf(target));
        await parties.HandleAsync(first.Client, Request(PartyRequest.Delete));

        await parties.HandleAsync(second.Client, InviteOf(target));

        target.InvitedToParty.Should().Be(second.PartyIndex);
    }

    [Fact]
    public async Task EachOpenBoardInviteCanStillBeAccepted()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var board = provider.GetRequiredService<IPartyBbsPacketCoordinator>();
        var first = CreateMember(sessionManager, 536);
        var second = CreateMember(sessionManager, 537);
        var target = CreateMember(sessionManager, 538);
        await board.HandleAsync(first.Client, BoardPacket(PartyBbsPacketWriter.SubInvite, target.Name));
        await board.HandleAsync(second.Client, BoardPacket(PartyBbsPacketWriter.SubInvite, target.Name));

        await board.HandleAsync(target.Client, BoardPacket(PartyBbsPacketWriter.SubInviteAccept, first.Name));

        target.PartyIndex.Should().Be(first.PartyIndex);
        first.IsPartyLeader.Should().BeTrue();
    }

    [Fact]
    public async Task JoiningThroughTheBoardWithdrawsAnOpenInvite()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var board = provider.GetRequiredService<IPartyBbsPacketCoordinator>();
        var inviter = CreateMember(sessionManager, 539);
        var boardInviter = CreateMember(sessionManager, 540);
        var target = CreateMember(sessionManager, 541);
        await parties.HandleAsync(inviter.Client, InviteOf(target));
        var invitersParty = inviter.PartyIndex;
        await board.HandleAsync(boardInviter.Client, BoardPacket(PartyBbsPacketWriter.SubInvite, target.Name));

        await board.HandleAsync(target.Client, BoardPacket(PartyBbsPacketWriter.SubInviteAccept, boardInviter.Name));

        target.PartyIndex.Should().Be(boardInviter.PartyIndex);
        target.InvitedToParty.Should().Be(-1);
        sessionManager.Parties.GetParty(invitersParty).Should().BeNull("the other inviter's party of one was only waiting on this player");
        inviter.IsInParty.Should().BeFalse();
    }

    [Fact]
    public async Task AnAcceptAfterTheLeaderDisbandedJoinsNothing()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leader = CreateMember(sessionManager, 542);
        var target = CreateMember(sessionManager, 543);
        await parties.HandleAsync(leader.Client, InviteOf(target));
        var party = sessionManager.Parties.GetParty(leader.PartyIndex)!;
        sessionManager.Parties.DeleteParty(party.Index);

        await parties.HandleAsync(target.Client, Permit(accept: true));

        target.IsInParty.Should().BeFalse("a party that came down takes no one in");
        party.FindMember((short)target.CharacterId).Should().BeNegative();
    }

    [Fact]
    public async Task JoiningAPartySpendsTheBoardInvitesBeforeIt()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var board = provider.GetRequiredService<IPartyBbsPacketCoordinator>();
        var boardInviter = CreateMember(sessionManager, 546);
        var leader = CreateMember(sessionManager, 547);
        var target = CreateMember(sessionManager, 548);
        await board.HandleAsync(boardInviter.Client, BoardPacket(PartyBbsPacketWriter.SubInvite, target.Name));
        await parties.HandleAsync(leader.Client, InviteOf(target));
        await parties.HandleAsync(target.Client, Permit(accept: true));
        await parties.HandleAsync(target.Client, Request(PartyRequest.Delete));

        await board.HandleAsync(target.Client, BoardPacket(PartyBbsPacketWriter.SubInviteAccept, boardInviter.Name));

        target.IsInParty.Should().BeFalse("joining a party answered every invite still open");
        boardInviter.IsInParty.Should().BeFalse();
    }

    [Fact]
    public async Task AMemberWhoAcceptsAnInviteWithdrawsIt()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var inviter = CreateMember(sessionManager, 544);
        var target = CreateMember(sessionManager, 545);
        await parties.HandleAsync(inviter.Client, InviteOf(target));
        var invitersParty = inviter.PartyIndex;
        var eventParty = sessionManager.Parties.CreateParty((short)target.CharacterId);
        target.PartyIndex = eventParty.Index;

        await parties.HandleAsync(target.Client, Permit(accept: true));

        target.PartyIndex.Should().Be(eventParty.Index);
        sessionManager.Parties.GetParty(invitersParty).Should().BeNull("the inviter is not left leading a party of one");
    }

    private static Packet Permit(bool accept)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)PartyRequest.Permit);
        packet.WriteByte(accept ? (byte)1 : (byte)0);
        packet.ResetOffset();
        return packet;
    }

    private static Packet Request(PartyRequest request)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)request);
        packet.ResetOffset();
        return packet;
    }

    private static Packet BoardPacket(byte sub, string name)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY_BBS);
        packet.WriteByte(PartyBbsPacketWriter.ModeNormal);
        packet.WriteByte(sub);
        packet.WriteString(name);
        return packet;
    }

    private static Packet InviteOf(UserSession target)
    {
        var invite = new Packet(GameOpcodes.GS_PARTY);
        invite.WriteByte((byte)PartyRequest.Create);
        invite.WriteString(target.Name);
        return invite;
    }

    private static List<short> RejectionsIn(List<Packet> sent)
    {
        var codes = new List<short>();
        foreach (var packet in sent)
        {
            packet.ResetOffset();
            if (packet.ReadByte() != (byte)PartySubOpcode.MemberInfo || packet.RemainingBytes != 2) continue;
            codes.Add(packet.ReadShort());
        }
        return codes;
    }

    private static UserSession CreateMember(SessionManager sessionManager, int characterId, List<Packet>? sent = null)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        if (sent != null)
            client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        else
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
