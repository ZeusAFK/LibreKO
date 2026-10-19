using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PartyUserVoteTests : GameTestBase
{
    private const byte Moradon = 21;
    private const byte RudeLanguage = 1;
    private const int Rich = 10_000_000;

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    [Fact]
    public void AWarningNeedsMoreYesThanNo()
    {
        PartyUserVoteRules.Warns(new byte[] { 1, 1, 0 }).Should().BeTrue();
        PartyUserVoteRules.Warns(new byte[] { 1, 0, 2 }).Should().BeFalse();
        PartyUserVoteRules.Warns(new byte[] { 2, 2 }).Should().BeFalse();
    }

    [Fact]
    public void WarningsLapseAfterTheirTime()
    {
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        PartyUserVoteRules.Standing(3, now.AddDays(-2), now, TimeSpan.FromDays(7)).Should().Be(3);
        PartyUserVoteRules.Standing(3, now.AddDays(-8), now, TimeSpan.FromDays(7)).Should().Be(0);
        PartyUserVoteRules.Barred(3, 3).Should().BeTrue();
        PartyUserVoteRules.Barred(2, 3).Should().BeFalse();
    }

    [Fact]
    public async Task AVoteOutsideJuraidIsRefused()
    {
        using var provider = Provider();
        var (vote, members, sent) = Party(provider, Moradon, 3);

        await vote.StartAsync(members[0], members[1].CharacterId, RudeLanguage, "");

        LastShort(sent[0], PartySubOpcode.UserVote).Should().Be(PartyUserVoteRules.NotHere);
        members[0].Money.Should().Be(Rich);
    }

    [Fact]
    public async Task TwoMembersAreTooFewToVote()
    {
        using var provider = Provider();
        var (vote, members, sent) = Party(provider, PartyUserVoteRules.JuraidZone, 2);

        await vote.StartAsync(members[0], members[1].CharacterId, RudeLanguage, "");

        LastShort(sent[0], PartySubOpcode.UserVote).Should().Be(PartyUserVoteRules.TooFewVoters);
    }

    [Fact]
    public async Task AYesMajorityWarnsTheTargetAndChargesTheStarter()
    {
        using var provider = Provider();
        var (vote, members, sent) = Party(provider, PartyUserVoteRules.JuraidZone, 3);
        var target = members[1];

        await vote.StartAsync(members[0], target.CharacterId, RudeLanguage, "");
        members[0].Money.Should().Be(Rich - PartyUserVoteRules.Cost);
        LastShort(sent[0], PartySubOpcode.UserVote).Should().Be(PartyUserVoteRules.Started);
        LastShort(sent[2], PartySubOpcode.UserVote).Should().Be(PartyUserVoteRules.Started);
        sent[1].Should().BeEmpty();

        await vote.AnswerAsync(members[0], PartyUserVoteRules.AnswerYes);
        await vote.AnswerAsync(members[2], PartyUserVoteRules.AnswerYes);

        var warning = sent[1].Single(p => Sub(p) == (byte)PartySubOpcode.UserVoteResult);
        warning.ResetOffset();
        warning.ReadByte();
        warning.ReadShort().Should().Be(1);
        warning.ReadByte().Should().Be(2);
        warning.ReadShort().Should().Be(1);
        warning.ReadShort().Should().Be(3);
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ICharacterRepository>().GetJuraidWarningsAsync(target.CharacterId))
            .Warnings.Should().Be(1);
    }

    [Fact]
    public async Task AnUnansweredVoteIsVoidedWhenTimeRunsOut()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        using var provider = Provider(clock);
        var (vote, members, sent) = Party(provider, PartyUserVoteRules.JuraidZone, 3);

        await vote.StartAsync(members[0], members[1].CharacterId, RudeLanguage, "");
        clock.Advance(TimeSpan.FromSeconds(PartyUserVoteRules.AnswerSeconds + 1));
        await vote.ExpireDueAsync();

        LastShort(sent[0], PartySubOpcode.UserVoteResult).Should().Be(0);
        sent[1].Should().BeEmpty();
    }

    [Fact]
    public async Task EnoughWarningsBarJuraidUntilTheyLapse()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));
        using var provider = Provider(clock);
        var (vote, members, _) = Party(provider, PartyUserVoteRules.JuraidZone, 3);
        using (var scope = provider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
            for (int i = 0; i < 3; i++)
                await repo.AddJuraidWarningAsync(members[1].CharacterId, clock.GetUtcNow().UtcDateTime, TimeSpan.FromDays(7));
        }

        (await vote.JuraidBarAsync(members[1].CharacterId)).Should().Be(((short)3, (short)3));
        clock.Advance(TimeSpan.FromDays(8));
        (await vote.JuraidBarAsync(members[1].CharacterId)).Should().BeNull();
    }

    private static ServiceProvider Provider(TimeProvider? clock = null) =>
        CreateProvider(db =>
        {
            for (int i = 0; i < 3; i++)
                db.Characters.Add(new Character { AccountId = 50 + i, Slot = 0, Name = $"Voter{i}", Level = 70, Class = 101, MapId = 87 });
            db.SaveChanges();
        }, configureServices: clock == null ? null : services => services.AddSingleton(clock));

    private static (PartyUserVoteService Vote, UserSession[] Members, List<Packet>[] Sent) Party(ServiceProvider provider, byte zone, int size)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        var members = new UserSession[size];
        var sent = new List<Packet>[size];
        var party = sessions.Parties.CreateParty(0);
        for (int i = 0; i < size; i++)
        {
            sent[i] = new List<Packet>();
            var log = sent[i];
            var client = Substitute.For<IClient>();
            client.Id.Returns(Guid.NewGuid());
            client.SendPacket(Arg.Do<Packet>(log.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            int id = GetCharacterIdAsync(provider, $"Voter{i}").GetAwaiter().GetResult();
            var member = sessions.CreateSession(client, id, 50 + i);
            member.Name = $"Voter{i}";
            member.Nation = AccountNation.Karus;
            member.ZoneId = zone;
            member.Money = Rich;
            member.PartyIndex = party.Index;
            party.MemberIds[i] = (short)id;
            members[i] = member;
        }
        var vote = new PartyUserVoteService(sessions, provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IUserNotificationService>(), provider.GetRequiredService<IOptions<GameServerSettings>>(),
            provider.GetService<TimeProvider>());
        return (vote, members, sent);
    }

    private static byte Sub(Packet packet)
    {
        packet.ResetOffset();
        return packet.ReadByte();
    }

    private static short LastShort(List<Packet> sent, PartySubOpcode sub)
    {
        var packet = sent.Last(p => Sub(p) == (byte)sub);
        packet.ResetOffset();
        packet.ReadByte();
        return packet.ReadShort();
    }
}
