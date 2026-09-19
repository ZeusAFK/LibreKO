using FluentAssertions;
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
        using var provider = CreateProvider(_ => { });
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
        using var provider = CreateProvider(_ => { });
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

        leader.PartyIndex.Should().Be(-1, "an evicted session must not leave a ghost in the party");
        sessionManager.Parties.GetParty(party.Index).Should().BeNull();
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
