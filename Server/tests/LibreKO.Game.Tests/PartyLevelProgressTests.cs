using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PartyLevelProgressTests : GameTestBase
{
    private const byte Moradon = 21;
    private const long ExpPerLevel = 10_000;

    [Fact]
    public async Task TheGaugeGoesToThePartyOnlyWhenTheWholePercentChanges()
    {
        using var provider = CreateLevelProvider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var progression = provider.GetRequiredService<IPlayerProgressionService>();
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 800, leaderSent);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 801, memberSent, experience: 3_700);
        var outsiderSent = new List<Packet>();
        CreateMember(sessionManager, 802, outsiderSent);
        FormParty(sessionManager, leader, member);
        await provider.GetRequiredService<IPartyLevelProgressService>().AnnounceAsync(member);
        leaderSent.Clear();
        memberSent.Clear();

        await progression.ChangeExperienceAsync(member, 50);

        ProgressIn(leaderSent).Should().BeEmpty("37.0% and 37.5% are the same whole percent");

        await progression.ChangeExperienceAsync(member, 60);

        ProgressIn(leaderSent).Should().Equal(Gauge(member, 381));
        ProgressIn(memberSent).Should().Equal(Gauge(member, 381));
        ProgressIn(outsiderSent).Should().BeEmpty();
    }

    [Fact]
    public async Task ALevelChangeSendsTheGaugeEvenAtTheSamePercent()
    {
        using var provider = CreateLevelProvider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var progression = provider.GetRequiredService<IPlayerProgressionService>();
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 810, leaderSent);
        var member = CreateMember(sessionManager, 811, experience: 9_950);
        FormParty(sessionManager, leader, member);
        await provider.GetRequiredService<IPartyLevelProgressService>().AnnounceAsync(member);
        leaderSent.Clear();

        await progression.ChangeExperienceAsync(member, ExpPerLevel);

        member.Level.Should().Be(41);
        ProgressIn(leaderSent).Should().Equal(Gauge(member, 995));
    }

    [Fact]
    public async Task AJoinerGetsEveryMembersGaugeAndThePartyGetsTheJoiners()
    {
        using var provider = CreateLevelProvider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var coordinator = provider.GetRequiredService<IPartyPacketCoordinator>();
        var leaderSent = new List<Packet>();
        var leader = CreateMember(sessionManager, 820, leaderSent, experience: 1_000);
        var memberSent = new List<Packet>();
        var member = CreateMember(sessionManager, 821, memberSent, experience: 2_500);
        var joinerSent = new List<Packet>();
        var joiner = CreateMember(sessionManager, 822, joinerSent, experience: 7_777);
        var party = FormParty(sessionManager, leader, member);
        joiner.InvitedToParty = party.Index;

        await coordinator.HandleAsync(joiner.Client, Accept());

        ProgressIn(joinerSent).Should().Equal(Gauge(leader, 100), Gauge(member, 250), Gauge(joiner, 777));
        ProgressIn(leaderSent).Should().Equal(Gauge(joiner, 777));
        ProgressIn(memberSent).Should().Equal(Gauge(joiner, 777));
    }

    [Fact]
    public async Task OutsideAPartyAnExperienceChangeSendsNoGauge()
    {
        using var provider = CreateLevelProvider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var sent = new List<Packet>();
        var loner = CreateMember(sessionManager, 830, sent);

        await provider.GetRequiredService<IPlayerProgressionService>().ChangeExperienceAsync(loner, 5_000);

        ProgressIn(sent).Should().BeEmpty();
    }

    [Fact]
    public async Task AnEventFormedPartyGetsEveryMembersGauge()
    {
        using var provider = CreateLevelProvider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var firstSent = new List<Packet>();
        var first = CreateMember(sessionManager, 840, firstSent, experience: 1_000);
        var secondSent = new List<Packet>();
        var second = CreateMember(sessionManager, 841, secondSent, experience: 2_000);
        var third = CreateMember(sessionManager, 842, experience: 3_000);

        await TempleEventHelpers.FormNationPartyAsync(
            sessionManager,
            provider.GetRequiredService<ICombatNotificationService>(),
            provider.GetRequiredService<IPartyLevelProgressService>(),
            [first, second, third],
            "Test Event",
            roomId: 1,
            NullLogger.Instance);

        ProgressIn(firstSent).Should().Equal(Gauge(first, 100), Gauge(second, 200), Gauge(third, 300));
        ProgressIn(secondSent).Should().Equal(Gauge(first, 100), Gauge(second, 200), Gauge(third, 300));
    }

    [Theory]
    [InlineData(0L, 10_000L, 0)]
    [InlineData(3_759L, 10_000L, 375)]
    [InlineData(10_000L, 10_000L, 1000)]
    [InlineData(15_000L, 10_000L, 1000)]
    [InlineData(-5L, 10_000L, 0)]
    [InlineData(5L, 0L, 0)]
    public void PermilleIsClampedToTheLevel(long experience, long required, int expected)
    {
        PartyLevelProgressService.Permille(experience, required).Should().Be((ushort)expected);
    }

    [Fact]
    public void TheGaugeUsesTheRebirthExperienceForTheLevel()
    {
        using var provider = CreateLevelProvider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var reborn = CreateMember(sessionManager, 850, experience: 5_000);
        reborn.RebirthLevel = 1;

        provider.GetRequiredService<IPartyLevelProgressService>().PermilleOf(reborn).Should().Be(250);
    }

    private static ServiceProvider CreateLevelProvider() => CreateProvider(_ => { },
        configureGameData: gameData => gameData.GetMaxExpForLevel(Arg.Any<byte>()).Returns(ExpPerLevel));

    private static Packet Accept()
    {
        var accept = new Packet(GameOpcodes.GS_PARTY);
        accept.WriteByte((byte)PartyRequest.Permit);
        accept.WriteByte(1);
        accept.ResetOffset();
        return accept;
    }

    private static string Gauge(UserSession member, int permille) => $"{member.CharacterId}:{permille}";

    private static List<string> ProgressIn(List<Packet> sent)
    {
        var gauges = new List<string>();
        foreach (var packet in sent)
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_PARTY)
                continue;

            packet.ResetOffset();
            if (packet.ReadByte() != (byte)PartySubOpcode.LevelProgress)
                continue;

            gauges.Add($"{packet.ReadInt()}:{packet.ReadUShort()}");
        }
        return gauges;
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

    private static UserSession CreateMember(
        SessionManager sessionManager, int characterId, List<Packet>? sent = null, long experience = 0)
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
        session.Experience = experience;
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
