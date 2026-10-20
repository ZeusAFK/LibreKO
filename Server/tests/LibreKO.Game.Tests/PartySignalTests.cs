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

public class PartySignalTests : GameTestBase
{
    private const byte Moradon = 21;
    private long now = 5_000_000;

    [Fact]
    public async Task AnEchoedProbeBecomesTheMembersSmoothedRoundTrip()
    {
        using var provider = CreateSignalProvider();
        var (signal, coordinator, sessionManager) = Resolve(provider);
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 700, leaderSent);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 701, memberSent);
        FormParty(sessionManager, leader, member);

        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 80, (leader, leaderSent));
        await EchoAfterAsync(coordinator, 40, (member, memberSent));
        await signal.TickAsync();

        TablesIn(leaderSent).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(Table((leader, 80), (member, 120)));
        TablesIn(memberSent).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(Table((leader, 80), (member, 120)));

        await EchoAfterAsync(coordinator, 80, (leader, leaderSent));
        await EchoAfterAsync(coordinator, 140, (member, memberSent));
        await signal.TickAsync();

        TablesIn(leaderSent).Last().Should().BeEquivalentTo(Table((leader, 80), (member, 150)));
        signal.ShownMs(member.CharacterId).Should().Be(150);
    }

    [Fact]
    public async Task ThreeUnansweredProbesMarkTheMemberAsNoSignalAndAnAnswerClearsIt()
    {
        using var provider = CreateSignalProvider();
        var (signal, coordinator, sessionManager) = Resolve(provider);
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 710, leaderSent);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 711, memberSent);
        FormParty(sessionManager, leader, member);

        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 100, (leader, leaderSent), (member, memberSent));
        await signal.TickAsync();
        TablesIn(leaderSent).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(Table((leader, 100), (member, 100)));

        await EchoAfterAsync(coordinator, 100, (leader, leaderSent));
        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 100, (leader, leaderSent));
        await signal.TickAsync();
        TablesIn(leaderSent).Should().HaveCount(1, "two unanswered probes are not enough to lose the signal");

        await EchoAfterAsync(coordinator, 100, (leader, leaderSent));
        await signal.TickAsync();
        TablesIn(leaderSent).Should().HaveCount(2);
        TablesIn(leaderSent).Last().Should().BeEquivalentTo(Table((leader, 100), (member, PartyPacketWriter.NoSignal)));
        signal.ShownMs(member.CharacterId).Should().Be(PartyPacketWriter.NoSignal);

        await EchoAfterAsync(coordinator, 60, (leader, leaderSent), (member, memberSent));
        await signal.TickAsync();
        TablesIn(leaderSent).Last().Should().BeEquivalentTo(Table((leader, 90), (member, 90)));
    }

    [Fact]
    public async Task TheTableGoesOnlyToThatPartyAndOnlyWhenAShownValueChanges()
    {
        using var provider = CreateSignalProvider();
        var (signal, coordinator, sessionManager) = Resolve(provider);
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 720, leaderSent);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 721, memberSent);
        var otherLeaderSent = new List<Packet>();
        var otherLeader = CreateMember(sessionManager, 722, otherLeaderSent);
        var otherMemberSent = new List<Packet>();
        var otherMember = CreateMember(sessionManager, 723, otherMemberSent);
        var outsiderSent = new List<Packet>();
        CreateMember(sessionManager, 724, outsiderSent);
        FormParty(sessionManager, leader, member);
        FormParty(sessionManager, otherLeader, otherMember);

        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 80, (leader, leaderSent), (member, memberSent));
        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 84, (leader, leaderSent), (member, memberSent));
        await signal.TickAsync();

        TablesIn(leaderSent).Should().ContainSingle("80 ms and 81 ms both show as 80 ms")
            .Which.Should().BeEquivalentTo(Table((leader, 80), (member, 80)));
        TablesIn(otherLeaderSent).Should().BeEmpty();
        TablesIn(otherMemberSent).Should().BeEmpty();
        outsiderSent.Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_PARTY).Should().BeEmpty();
        ProbesIn(otherLeaderSent).Should().HaveCount(3);

        await EchoAfterAsync(coordinator, 200, (leader, leaderSent), (member, memberSent));
        await signal.TickAsync();

        TablesIn(leaderSent).Should().HaveCount(2);
        TablesIn(leaderSent).Last().Should().BeEquivalentTo(Table((leader, 120), (member, 120)));
        TablesIn(otherLeaderSent).Should().ContainSingle("the other party never answered, so it loses the signal on its own")
            .Which.Should().BeEquivalentTo(Table((otherLeader, PartyPacketWriter.NoSignal), (otherMember, PartyPacketWriter.NoSignal)));
        outsiderSent.Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_PARTY).Should().BeEmpty();
    }

    [Fact]
    public async Task AJoinerGetsThePartysCurrentTableAtOnce()
    {
        using var provider = CreateSignalProvider();
        var (signal, coordinator, sessionManager) = Resolve(provider);
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 730, leaderSent);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 731, memberSent);
        var joinerSent = new List<Packet>();
        var joiner = CreateMember(sessionManager, 732, joinerSent);
        var party = FormParty(sessionManager, leader, member);

        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 70, (leader, leaderSent));
        await EchoAfterAsync(coordinator, 50, (member, memberSent));
        await signal.TickAsync();

        joiner.InvitedToParty = party.Index;
        await coordinator.HandleAsync(joiner.Client, Accept());

        TablesIn(joinerSent).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(Table((leader, 70), (member, 120)));

        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 30, (leader, leaderSent), (member, memberSent), (joiner, joinerSent));
        await signal.TickAsync();

        TablesIn(leaderSent).Last().Should().ContainKey(joiner.CharacterId);
    }

    [Fact]
    public async Task AnAnswerToATokenThatWasNeverSentIsIgnored()
    {
        using var provider = CreateSignalProvider();
        var (signal, coordinator, sessionManager) = Resolve(provider);
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 740, leaderSent);
        var member = CreateMember(sessionManager, 741);
        FormParty(sessionManager, leader, member);

        await signal.TickAsync();
        var token = ProbesIn(leaderSent).Single();
        now += 50;
        await coordinator.HandleAsync(leader.Client, EchoOf(unchecked(token + 1)));

        signal.ShownMs(leader.CharacterId).Should().BeNull();
    }

    [Fact]
    public async Task AMemberWhoLeavesThePartyOrDisconnectsIsForgotten()
    {
        using var provider = CreateSignalProvider();
        var (signal, coordinator, sessionManager) = Resolve(provider);
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 750, leaderSent);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 751, memberSent);
        var thirdSent = new List<Packet>();
        var third = CreateMember(sessionManager, 752, thirdSent);
        var party = FormParty(sessionManager, leader, member);
        party.MemberIds[2] = (short)third.CharacterId;
        third.PartyIndex = party.Index;

        await signal.TickAsync();
        await EchoAfterAsync(coordinator, 90, (leader, leaderSent), (member, memberSent), (third, thirdSent));
        signal.ShownMs(member.CharacterId).Should().Be(90);
        signal.ShownMs(third.CharacterId).Should().Be(90);

        party.MemberIds[1] = -1;
        member.PartyIndex = -1;
        sessionManager.RemoveSession(third);
        await signal.TickAsync();

        signal.ShownMs(member.CharacterId).Should().BeNull();
        signal.ShownMs(third.CharacterId).Should().BeNull();
        signal.ShownMs(leader.CharacterId).Should().Be(90);
    }

    [Theory]
    [InlineData(0, PartySignalService.SignalLevel.Strong)]
    [InlineData(100, PartySignalService.SignalLevel.Strong)]
    [InlineData(110, PartySignalService.SignalLevel.Good)]
    [InlineData(200, PartySignalService.SignalLevel.Good)]
    [InlineData(210, PartySignalService.SignalLevel.Weak)]
    [InlineData(350, PartySignalService.SignalLevel.Weak)]
    [InlineData(360, PartySignalService.SignalLevel.Poor)]
    [InlineData(PartyPacketWriter.NoSignal, PartySignalService.SignalLevel.NoSignal)]
    public void SignalLevelsFollowTheThresholds(int shownMs, PartySignalService.SignalLevel expected)
    {
        PartySignalService.LevelOf((ushort)shownMs).Should().Be(expected);
    }

    private ServiceProvider CreateSignalProvider() => CreateProvider(_ => { }, configureServices: services =>
        services.AddSingleton(sp => new PartySignalService(
            sp.GetRequiredService<SessionManager>(), NullLogger<PartySignalService>.Instance, () => now)));

    private static (IPartySignalService Signal, IPartyPacketCoordinator Coordinator, SessionManager Sessions) Resolve(
        ServiceProvider provider) => (
        provider.GetRequiredService<IPartySignalService>(),
        provider.GetRequiredService<IPartyPacketCoordinator>(),
        provider.GetRequiredService<SessionManager>());

    private async Task EchoAfterAsync(
        IPartyPacketCoordinator coordinator, long elapsedMs, params (UserSession Session, List<Packet> Sent)[] answering)
    {
        now += elapsedMs;
        foreach (var (session, sent) in answering)
            await coordinator.HandleAsync(session.Client, EchoOf(ProbesIn(sent).Last()));
    }

    private static Packet EchoOf(uint token)
    {
        var echo = new Packet(GameOpcodes.GS_PARTY);
        echo.WriteByte((byte)PartyRequest.SignalEcho);
        echo.WriteUInt(token);
        echo.ResetOffset();
        return echo;
    }

    private static Packet Accept()
    {
        var accept = new Packet(GameOpcodes.GS_PARTY);
        accept.WriteByte((byte)PartyRequest.Permit);
        accept.WriteByte(1);
        accept.ResetOffset();
        return accept;
    }

    private static Dictionary<int, ushort> Table(params (UserSession Member, ushort Ms)[] rows) =>
        rows.ToDictionary(row => row.Member.CharacterId, row => row.Ms);

    private static List<uint> ProbesIn(List<Packet> sent)
    {
        var tokens = new List<uint>();
        foreach (var packet in PartyPackets(sent, PartySubOpcode.SignalProbe))
            tokens.Add(packet.ReadUInt());
        return tokens;
    }

    private static List<Dictionary<int, ushort>> TablesIn(List<Packet> sent)
    {
        var tables = new List<Dictionary<int, ushort>>();
        foreach (var packet in PartyPackets(sent, PartySubOpcode.SignalTable))
        {
            var table = new Dictionary<int, ushort>();
            var count = packet.ReadByte();
            for (var index = 0; index < count; index++)
                table[packet.ReadInt()] = packet.ReadUShort();
            packet.RemainingBytes.Should().Be(0);
            tables.Add(table);
        }
        return tables;
    }

    private static IEnumerable<Packet> PartyPackets(List<Packet> sent, PartySubOpcode sub)
    {
        foreach (var packet in sent.ToList())
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_PARTY)
                continue;

            packet.ResetOffset();
            if (packet.ReadByte() == (byte)sub)
                yield return packet;
        }
    }

    private static PartyGroup FormParty(SessionManager sessionManager, UserSession leader, UserSession member)
    {
        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        member.PartyIndex = party.Index;
        return party;
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
