using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class PartyWarBoardTests
{
    private const int LeaderId = 1;
    private const int MemberId = 2;

    private readonly SessionManager sessions = new();
    private readonly Dictionary<int, List<Packet>> sent = [];
    private readonly PartyWarBoardService board;
    private readonly UserSession leader;
    private readonly UserSession member;

    public PartyWarBoardTests()
    {
        board = new PartyWarBoardService(sessions, Substitute.For<ILogger<PartyWarBoardService>>());
        leader = Player(LeaderId);
        member = Player(MemberId);
        var party = sessions.Parties.CreateParty(LeaderId);
        party.MemberIds[1] = MemberId;
        leader.PartyIndex = member.PartyIndex = party.Index;
        board.MemberJoined(party.Index, LeaderId);
        board.MemberJoined(party.Index, MemberId);
    }

    private UserSession Player(int id)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var packets = new List<Packet>();
        sent[id] = packets;
        client.SendPacket(Arg.Do<Packet>(p => packets.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return sessions.CreateSession(client, id, id);
    }

    private static (byte Target, byte Mode, List<(int Id, short Share)> Rows) Read(Packet packet)
    {
        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_PARTY);
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)PartySubOpcode.WarBoard);
        byte target = packet.ReadByte();
        byte mode = packet.ReadByte();
        int count = packet.ReadByte();
        var rows = new List<(int, short)>();
        for (int i = 0; i < count; i++)
            rows.Add((packet.ReadInt(), packet.ReadShort()));
        return (target, mode, rows);
    }

    [Fact]
    public async Task ModesSentWithPartyCreateDecideWhatTheLeaderIsPushed()
    {
        board.SeedModes(leader.PartyIndex, LeaderId, PartyWarBoardService.ModeHealing, PartyWarBoardService.ModeDamage);
        board.CountDamage(member, 50);
        sent[LeaderId].Clear();

        await board.PushAsync();

        var pushed = sent[LeaderId].Select(Read).Select(r => (r.Target, r.Mode)).ToList();
        pushed.Should().BeEquivalentTo([(PartyWarBoardService.TargetRows, PartyWarBoardService.ModeHealing),
            (PartyWarBoardService.TargetBoard, PartyWarBoardService.ModeDamage)]);
    }

    [Fact]
    public void SharesAreThousandthsOfThePartyTotal()
    {
        var shares = PartyWarBoardService.ShareOf([(LeaderId, 300), (MemberId, 100)]);

        shares.Should().Equal(
            new PartyPacketWriter.WarBoardShare(LeaderId, 750),
            new PartyPacketWriter.WarBoardShare(MemberId, 250));
    }

    [Fact]
    public void NoTotalGivesEveryoneZero()
    {
        PartyWarBoardService.ShareOf([(LeaderId, 0), (MemberId, 0)])
            .Select(s => s.Share).Should().Equal((ushort)0, (ushort)0);
    }

    [Fact]
    public async Task AskingForAModeAnswersWithTheCurrentTable()
    {
        board.CountHealing(member, 40);

        await board.SetModeAsync(leader, PartyWarBoardService.TargetBoard, PartyWarBoardService.ModeHealing);

        var reply = Read(sent[LeaderId].Single());
        reply.Target.Should().Be(PartyWarBoardService.TargetBoard);
        reply.Mode.Should().Be(PartyWarBoardService.ModeHealing);
        reply.Rows.Should().Equal((LeaderId, (short)0), (MemberId, (short)PartyWarBoardService.ShareScale));
    }

    [Fact]
    public async Task TurningAMeterOffSendsNothing()
    {
        await board.SetModeAsync(leader, PartyWarBoardService.TargetRows, PartyWarBoardService.ModeOff);

        sent[LeaderId].Should().BeEmpty();
    }

    [Fact]
    public async Task APushGoesOnlyToMembersWatchingThatMode()
    {
        await board.SetModeAsync(member, PartyWarBoardService.TargetRows, PartyWarBoardService.ModeHealing);
        sent[MemberId].Clear();
        board.CountDamage(leader, 90);
        board.CountDamage(member, 10);

        await board.PushAsync();

        var leaderPush = Read(sent[LeaderId].Single());
        leaderPush.Target.Should().Be(PartyWarBoardService.TargetRows);
        leaderPush.Mode.Should().Be(PartyWarBoardService.ModeDamage);
        leaderPush.Rows.Should().Equal((LeaderId, (short)900), (MemberId, (short)100));
        var memberPush = Read(sent[MemberId].Single());
        memberPush.Mode.Should().Be(PartyWarBoardService.ModeHealing);
    }

    [Fact]
    public async Task NothingIsPushedWhenNothingChanged()
    {
        board.CountDamage(leader, 5);
        await board.PushAsync();
        sent[LeaderId].Clear();

        await board.PushAsync();

        sent[LeaderId].Should().BeEmpty();
    }

    [Fact]
    public async Task AJoinerStartsFromZero()
    {
        board.CountDamage(member, 50);
        board.MemberJoined(member.PartyIndex, MemberId);
        board.CountDamage(leader, 50);

        await board.SetModeAsync(leader, PartyWarBoardService.TargetBoard, PartyWarBoardService.ModeDamage);

        Read(sent[LeaderId].Single()).Rows.Should().Equal((LeaderId, (short)1000), (MemberId, (short)0));
    }
}
