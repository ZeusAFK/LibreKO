using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PartyDisconnectTests : GameTestBase
{
    private const byte Moradon = 21;

    [Fact]
    public async Task DroppingTheConnectionTakesThePlayerOutOfTheParty()
    {
        using var provider = CreateProvider(db =>
        {
            db.Characters.AddRange(
                new Character { Id = 400, AccountId = 500, Name = "Player400" },
                new Character { Id = 401, AccountId = 501, Name = "Player401" });
        });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var leaderPackets = new List<Packet>();
        var leader = CreateMember(sessionManager, 400, leaderPackets);
        var memberPackets = new List<Packet>();
        var member = CreateMember(sessionManager, 401, memberPackets);

        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        member.PartyIndex = party.Index;

        leaderPackets.Clear();
        memberPackets.Clear();

        await provider.GetRequiredService<ISessionTerminationService>()
            .DisconnectAsync(member.Client);

        leader.PartyIndex.Should().Be(-1, "the last player standing is not a party");
        sessionManager.Parties.GetParty(party.Index).Should().BeNull();
        leaderPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_PARTY);
    }

    [Fact]
    public async Task LoggingBackInTakesTheAbandonedSessionOutOfTheParty()
    {
        using var provider = CreateProvider(db =>
        {
            db.Characters.AddRange(
                new Character { Id = 400, AccountId = 500, Name = "Player400" },
                new Character { Id = 401, AccountId = 501, Name = "Player401" });
        });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var leaderPackets = new List<Packet>();
        var leader = CreateMember(sessionManager, 400, leaderPackets);
        var member = CreateMember(sessionManager, 401, new List<Packet>());

        var party = sessionManager.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = party.Index;
        leader.IsPartyLeader = true;
        member.PartyIndex = party.Index;

        await provider.GetRequiredService<ISessionTerminationService>()
            .EvictForTakeoverAsync(member);

        sessionManager.GetByCharacterId(member.CharacterId).Should().BeNull();
        leader.PartyIndex.Should().Be(-1, "an evicted session must not leave a ghost in the party");
        sessionManager.Parties.GetParty(party.Index).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TakeoverRemovesTheOldSessionEvenWhenSavingFails(bool throws)
    {
        using var provider = CreateProvider(_ => { });
        var manager = provider.GetRequiredService<SessionManager>();
        var oldSession = CreateMember(manager, 400, new List<Packet>());
        oldSession.GenieTime.Load(120);
        oldSession.GenieActive = true;
        var persister = Substitute.For<ICharacterStatePersister>();
        persister.SaveAsync(oldSession, Arg.Any<CancellationToken>()).Returns(
            throws ? Task.FromException<bool>(new InvalidOperationException("Save failed")) : Task.FromResult(false));
        var service = new SessionTerminationService(manager, persister,
            provider.GetRequiredService<IChallengePacketCoordinator>(),
            provider.GetRequiredService<IEventSystemsPacketCoordinator>(),
            provider.GetRequiredService<IExchangePacketCoordinator>(),
            provider.GetRequiredService<IMerchantPacketCoordinator>(),
            provider.GetRequiredService<IPartyPacketCoordinator>(),
            provider.GetRequiredService<IWorldPacketCoordinator>(),
            provider.GetRequiredService<INpcLifecycleService>(),
            provider.GetRequiredService<InstanceRoomRegistry>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionTerminationService>.Instance);

        await service.EvictForTakeoverAsync(oldSession);

        manager.GetByCharacterId(400).Should().BeNull();
        manager.GetByClientId(oldSession.Client.Id).Should().BeNull();
        oldSession.GenieTime.IsRunning.Should().BeFalse();
        oldSession.GenieActive.Should().BeFalse();
        await persister.Received(1).SaveAsync(oldSession, Arg.Any<CancellationToken>());
        var replacement = CreateMember(manager, 400, new List<Packet>());
        manager.GetByCharacterId(400).Should().BeSameAs(replacement);
    }

    private static UserSession CreateMember(SessionManager sessionManager, int characterId, List<Packet> sent)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.CharacterId.Returns(characterId);
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

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
