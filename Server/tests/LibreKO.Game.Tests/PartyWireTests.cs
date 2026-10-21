using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PartyWireTests : GameTestBase
{
    private const byte Moradon = 21;
    private const int RaceRounds = 400;
    private const int RaceSweep = 64;
    private const int RaceFirstId = 2000;
    private const int EventFirstId = 3000;
    private const short OutsiderId = 3100;
    private const short OtherLeaderId = 3101;
    private const ushort EventRoom = 1;

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
    public async Task ASecondInviteBeforeTheFirstAnswerJoinsTheFormingParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var sent = new List<Packet>();
        var leader = CreateMember(sessionManager, 514, sent);
        var first = CreateMember(sessionManager, 515);
        var second = CreateMember(sessionManager, 516);

        await coordinator.HandleAsync(leader.Client, InviteOf(first));
        await coordinator.HandleAsync(leader.Client, InviteOf(second));

        RejectionsIn(sent).Should().BeEmpty();
        first.InvitedToParty.Should().Be(leader.PartyIndex);
        second.InvitedToParty.Should().Be(leader.PartyIndex);
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
    public async Task AnInviteWaitsOutsideThePartyUntilItIsAnswered()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var leader = CreateMember(sessionManager, 550);
        var invitee = CreateMember(sessionManager, 551);

        await coordinator.HandleAsync(leader.Client, InviteOf(invitee));

        invitee.IsInParty.Should().BeFalse("an unanswered invite is not a membership");
        invitee.InvitedToParty.Should().Be(leader.PartyIndex);

        await coordinator.HandleAsync(invitee.Client, Permit(accept: true));

        sessionManager.Parties.GetParty(leader.PartyIndex)!.FindMember((short)invitee.CharacterId).Should().BeGreaterThanOrEqualTo(0);
        invitee.InvitedToParty.Should().Be(-1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AStrayAnswerFromAMemberLeavesThePartyAlone(bool accept)
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();

        var leader = CreateMember(sessionManager, 552);
        var member = CreateMember(sessionManager, 553);
        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        member.PartyIndex = party.Index;

        await coordinator.HandleAsync(member.Client, Permit(accept));

        member.PartyIndex.Should().Be(party.Index);
        party.FindMember((short)member.CharacterId).Should().BeGreaterThanOrEqualTo(0);
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
        var eventMate = CreateMember(sessionManager, 549);
        await parties.HandleAsync(inviter.Client, InviteOf(target));
        var invitersParty = inviter.PartyIndex;
        var eventParty = await TempleEventHelpers.FormNationPartyAsync(sessionManager, Substitute.For<ICombatNotificationService>(),
            Substitute.For<IPartyLevelProgressService>(), [target, eventMate], nameof(AMemberWhoAcceptsAnInviteWithdrawsIt), EventRoom,
            NullLogger.Instance);

        await parties.HandleAsync(target.Client, Permit(accept: true));

        target.PartyIndex.Should().Be(eventParty, "an event seats the player while the invite still stands");
        sessionManager.Parties.GetParty(invitersParty).Should().BeNull("the inviter is not left leading a party of one");
    }

    [Fact]
    public async Task APlayerAnsweringTwoInvitesAtOnceJoinsOneParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var characterId = RaceFirstId;
        for (var round = 0; round < RaceRounds; round++)
        {
            var leader = CreateMember(sessionManager, characterId++);
            var player = CreateMember(sessionManager, characterId++);
            var boardInviter = CreateMember(sessionManager, characterId++);
            await parties.HandleAsync(leader.Client, InviteOf(player));
            var invitedTo = leader.PartyIndex;
            await parties.BoardInviteAsync(boardInviter, player.Name, PartyBbsPacketWriter.ModeNormal);

            await AtOnceAsync(round,
                () => parties.HandleAsync(player.Client, Permit(accept: true)),
                () => parties.BoardAcceptAsync(player, boardInviter.Name, PartyBbsPacketWriter.ModeNormal));

            PartiesHolding(sessionManager, player, invitedTo, boardInviter.PartyIndex).Should().Equal([player.PartyIndex],
                "a player stands in the one party their index names");
            NoOneLeadsAPartyOfOne(sessionManager, leader, boardInviter);
        }
    }

    [Fact]
    public async Task AnInviteeWhoseBoardInviteIsAnsweredMeanwhileJoinsOneParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var characterId = RaceFirstId;
        for (var round = 0; round < RaceRounds; round++)
        {
            var leader = CreateMember(sessionManager, characterId++);
            var playerSent = new List<Packet>();
            var player = CreateMember(sessionManager, characterId++, playerSent);
            var boardInvitee = CreateMember(sessionManager, characterId++);
            await parties.HandleAsync(leader.Client, InviteOf(player));
            var invitedTo = leader.PartyIndex;
            await parties.BoardInviteAsync(player, boardInvitee.Name, PartyBbsPacketWriter.ModeNormal);

            await AtOnceAsync(round,
                () => parties.HandleAsync(player.Client, Permit(accept: true)),
                () => parties.BoardAcceptAsync(boardInvitee, player.Name, PartyBbsPacketWriter.ModeNormal));

            PartiesHolding(sessionManager, player, invitedTo, boardInvitee.PartyIndex).Should().Equal([player.PartyIndex],
                "a player who accepted one party while starting another stands in one of them");
            if (boardInvitee.IsInParty)
                sessionManager.Parties.GetParty(boardInvitee.PartyIndex)!.LeaderId.Should().Be((short)player.CharacterId,
                    "a board invite brings the player into a party its sender leads");
            else
                RejectionsIn(playerSent).Should().Contain(PartyInviteError.CannotParty, "a board accept that joins nothing is answered");
            NoOneLeadsAPartyOfOne(sessionManager, leader, player);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheInviterIsToldTheInviteWentOutAndWhenItWasAnswered(bool accept)
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 580, leaderSent);
        var invitee = CreateMember(sessionManager, 581);

        await parties.HandleAsync(leader.Client, InviteOf(invitee));

        IdsIn(leaderSent, PartySubOpcode.InviteSent).Should().Equal(invitee.CharacterId);
        IdsIn(leaderSent, PartySubOpcode.InviteClosed).Should().BeEmpty("the invite is still waiting");

        await parties.HandleAsync(invitee.Client, Permit(accept));

        IdsIn(leaderSent, PartySubOpcode.InviteClosed).Should().Equal(invitee.CharacterId);
    }

    [Fact]
    public async Task TheInviterWithdrawsAnUnansweredInvite()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leaderSent = new List<Packet>();
        var inviteeSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 582, leaderSent);
        var invitee = CreateMember(sessionManager, 583, inviteeSent);
        await parties.HandleAsync(leader.Client, InviteOf(invitee));

        await parties.HandleAsync(leader.Client, CancelInvite(invitee));

        invitee.InvitedToParty.Should().Be(-1);
        IdsIn(inviteeSent, PartySubOpcode.InviteWithdrawn).Should().Equal(leader.CharacterId);
        IdsIn(leaderSent, PartySubOpcode.InviteClosed).Should().Equal(invitee.CharacterId);
        leader.IsInParty.Should().BeFalse("the party formed for this invite had no one else to wait for");

        await parties.HandleAsync(invitee.Client, Permit(accept: true));

        invitee.IsInParty.Should().BeFalse();
    }

    [Fact]
    public async Task OnlyTheInviterWithdrawsAnInvite()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leader = CreateMember(sessionManager, 584);
        var invitee = CreateMember(sessionManager, 585);
        var stranger = CreateMember(sessionManager, 586);
        await parties.HandleAsync(leader.Client, InviteOf(invitee));

        await parties.HandleAsync(stranger.Client, CancelInvite(invitee));

        invitee.InvitedToParty.Should().Be(leader.PartyIndex);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFormingPartyWaitsForEveryInviteStillOpen(bool withdrawn)
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leader = CreateMember(sessionManager, 587);
        var first = CreateMember(sessionManager, 588);
        var second = CreateMember(sessionManager, 589);
        await parties.HandleAsync(leader.Client, InviteOf(first));
        await parties.HandleAsync(leader.Client, InviteOf(second));

        if (withdrawn)
            await parties.HandleAsync(leader.Client, CancelInvite(first));
        else
            await parties.HandleAsync(first.Client, Permit(accept: false));
        await parties.HandleAsync(second.Client, Permit(accept: true));

        second.PartyIndex.Should().Be(leader.PartyIndex);
        sessionManager.Parties.GetParty(leader.PartyIndex)!.FindMember((short)second.CharacterId).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AnInviteeWhoLeavesTheGameEndsTheInvitersWait()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 590, leaderSent);
        var invitee = CreateMember(sessionManager, 591);
        await parties.HandleAsync(leader.Client, InviteOf(invitee));

        await parties.LeaveGameAsync(invitee);

        IdsIn(leaderSent, PartySubOpcode.InviteClosed).Should().Equal(invitee.CharacterId);
    }

    [Fact]
    public async Task AnInviterWhoLeavesTheGameWithdrawsTheInvite()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var inviteeSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 592);
        var invitee = CreateMember(sessionManager, 593, inviteeSent);
        await parties.HandleAsync(leader.Client, InviteOf(invitee));

        await parties.LeaveGameAsync(leader);

        invitee.InvitedToParty.Should().Be(-1);
        IdsIn(inviteeSent, PartySubOpcode.InviteWithdrawn).Should().Equal(leader.CharacterId);
    }

    [Fact]
    public async Task ADisbandWithdrawsTheInvitesToTheParty()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leaderSent = new List<Packet>();
        var inviteeSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 594, leaderSent);
        var invitee = CreateMember(sessionManager, 595, inviteeSent);
        await parties.HandleAsync(leader.Client, InviteOf(invitee));

        await parties.HandleAsync(leader.Client, Request(PartyRequest.Delete));

        invitee.InvitedToParty.Should().Be(-1);
        IdsIn(inviteeSent, PartySubOpcode.InviteWithdrawn).Should().Equal(leader.CharacterId);
        IdsIn(leaderSent, PartySubOpcode.InviteClosed).Should().Equal(invitee.CharacterId);
    }

    private static Packet CancelInvite(UserSession invitee)
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)PartyRequest.InviteCancel);
        packet.WriteInt(invitee.CharacterId);
        packet.ResetOffset();
        return packet;
    }

    private static List<int> IdsIn(List<Packet> sent, PartySubOpcode sub)
    {
        var ids = new List<int>();
        lock (sent)
            foreach (var packet in sent)
            {
                packet.ResetOffset();
                if (packet.GetOpcode() == (byte)GameOpcodes.GS_PARTY && packet.ReadByte() == (byte)sub)
                    ids.Add(packet.ReadInt());
            }
        return ids;
    }

    private static void NoOneLeadsAPartyOfOne(SessionManager sessionManager, params UserSession[] leaders)
    {
        foreach (var leader in leaders.Where(leader => leader.IsInParty))
            sessionManager.Parties.GetParty(leader.PartyIndex)!.MemberCount.Should().BeGreaterThan(1,
                "a party formed for an invite that went elsewhere comes down");
    }

    [Fact]
    public async Task ABoardAcceptWhosePlayerJoinedElsewhereMeanwhileIsAnsweredAndLeavesNoParty()
    {
        var warBoard = Substitute.For<IPartyWarBoardService>();
        using var provider = CreateProvider(_ => { }, configureServices: services => services.AddSingleton(warBoard));
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var parties = provider.GetRequiredService<IPartyPacketCoordinator>();
        var inviterSent = new List<Packet>();
        var inviter = CreateMember(sessionManager, 560, inviterSent);
        var player = CreateMember(sessionManager, 561);
        var otherLeader = CreateMember(sessionManager, 562);
        await parties.BoardInviteAsync(inviter, player.Name, PartyBbsPacketWriter.ModeNormal);
        var elsewhere = sessionManager.Parties.CreateParty((short)otherLeader.CharacterId);
        otherLeader.TryEnterParty(elsewhere.Index);
        warBoard.When(board => board.MemberJoined(Arg.Any<int>(), inviter.CharacterId)).Do(_ =>
        {
            player.TryEnterParty(elsewhere.Index);
            elsewhere.TryJoin((short)player.CharacterId);
        });

        await parties.BoardAcceptAsync(player, inviter.Name, PartyBbsPacketWriter.ModeNormal);

        player.PartyIndex.Should().Be(elsewhere.Index);
        inviter.IsInParty.Should().BeFalse("the party formed for this accept had no one to take in");
        RejectionsIn(inviterSent).Should().Contain(PartyInviteError.CannotParty);
    }

    [Fact]
    public async Task AnEventPartySeatsOnlyPlayersStillFree()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var players = Enumerable.Range(EventFirstId, 4).Select(id => CreateMember(sessionManager, id)).ToList();
        var (first, second, third, last) = (players[0], players[1], players[2], players[3]);
        var lastsParty = sessionManager.Parties.CreateParty((short)last.CharacterId);
        last.TryEnterParty(lastsParty.Index);
        lastsParty.TryJoin(OutsiderId);
        var elsewhere = sessionManager.Parties.CreateParty(OtherLeaderId);
        var notices = Substitute.For<ICombatNotificationService>();
        notices.When(sender => sender.SendToPartyAsync(lastsParty, Arg.Any<Packet>())).Do(_ =>
        {
            foreach (var joiner in new[] { first, third })
            {
                joiner.TryEnterParty(elsewhere.Index);
                elsewhere.TryJoin((short)joiner.CharacterId);
            }
        });

        var index = await TempleEventHelpers.FormNationPartyAsync(sessionManager, notices,
            Substitute.For<IPartyLevelProgressService>(), players, nameof(AnEventPartySeatsOnlyPlayersStillFree), EventRoom, NullLogger.Instance);

        var party = sessionManager.Parties.GetParty(index)!;
        party.LeaderId.Should().Be((short)second.CharacterId, "the first player still free leads");
        party.MemberIds.Where(id => id >= 0).Should().BeEquivalentTo([(short)second.CharacterId, (short)last.CharacterId],
            "a player who joined another party while the event party formed stays in it");
        first.PartyIndex.Should().Be(elsewhere.Index);
        third.PartyIndex.Should().Be(elsewhere.Index);
    }

    [Fact]
    public async Task AnEventPartyLeavesOutWhoeverFindsItFull()
    {
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var players = Enumerable.Range(EventFirstId, PartyGroup.MaxMembers + 1).Select(id => CreateMember(sessionManager, id)).ToList();

        var index = await TempleEventHelpers.FormNationPartyAsync(sessionManager, Substitute.For<ICombatNotificationService>(),
            Substitute.For<IPartyLevelProgressService>(), players, nameof(AnEventPartyLeavesOutWhoeverFindsItFull), EventRoom, NullLogger.Instance);

        sessionManager.Parties.GetParty(index)!.MemberCount.Should().Be(PartyGroup.MaxMembers);
        players[^1].IsInParty.Should().BeFalse("a player with no seat holds no party's index");
    }

    private static async Task AtOnceAsync(int round, Func<Task> first, Func<Task> second)
    {
        var shift = round % RaceSweep;
        var (firstShift, secondShift) = round / RaceSweep % 2 == 0 ? (shift, 0) : (0, shift);
        using var start = new Barrier(2);
        await Task.WhenAll(
            Task.Run(async () => { start.SignalAndWait(); Thread.SpinWait(firstShift); await first(); }),
            Task.Run(async () => { start.SignalAndWait(); Thread.SpinWait(secondShift); await second(); }));
    }

    private static List<int> PartiesHolding(SessionManager sessionManager, UserSession player, params int[] indexes) =>
        [.. indexes.Distinct().Where(index => sessionManager.Parties.GetParty(index)?.FindMember((short)player.CharacterId) >= 0)];

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
            client.SendPacket(Arg.Do<Packet>(packet => { lock (sent) sent.Add(packet); }), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
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
