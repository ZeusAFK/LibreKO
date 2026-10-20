using FluentAssertions;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class PartyGroupTests
{
    private const short Leader = 1;
    private const int Joiners = 40;
    private const int Parties = 500;

    [Fact]
    public void PlayersJoiningAtOnceFillEachSlotOnce()
    {
        var party = new PartyManager().CreateParty(Leader);
        var joined = new System.Collections.Concurrent.ConcurrentBag<short>();

        Parallel.For(Leader + 1, Leader + 1 + Joiners, joiner =>
        {
            if (party.TryJoin((short)joiner))
                joined.Add((short)joiner);
        });

        party.MemberIds.Should().OnlyHaveUniqueItems();
        party.MemberIds.Skip(1).Should().BeEquivalentTo(joined, "everyone told they joined holds a slot");
    }

    [Fact]
    public void APlayerJoinsAPartyOnce()
    {
        var party = new PartyManager().CreateParty(Leader);

        party.TryJoin(Leader + 1).Should().BeTrue();
        party.TryJoin(Leader + 1).Should().BeFalse();
    }

    [Fact]
    public void NoOneJoinsAPartyThatCameDown()
    {
        var parties = new PartyManager();
        var party = parties.CreateParty(Leader);

        parties.DeleteParty(party.Index).Should().Equal(Leader);
        party.TryJoin(Leader + 1).Should().BeFalse();
        parties.DeleteParty(party.Index).Should().BeNull("a party comes down once");
    }

    [Fact]
    public void OnlyTheLeaderPromotesAMember()
    {
        var party = new PartyManager().CreateParty(Leader);
        party.TryJoin(Leader + 1);
        party.TryJoin(Leader + 2);

        party.TryPromote(Leader + 1, Leader + 2).Should().BeFalse();
        party.TryPromote(Leader, Leader + 2).Should().BeTrue();
        party.LeaderId.Should().Be(Leader + 2);
    }

    [Fact]
    public void PartiesFormedAtOnceEachGetTheirOwnIndex()
    {
        var parties = new PartyManager();

        var indexes = new int[Parties];
        Parallel.For(0, Parties, i => indexes[i] = parties.CreateParty((short)i).Index);

        indexes.Should().OnlyHaveUniqueItems();
        indexes.Should().OnlyContain(index => parties.GetParty(index) != null);
    }

    [Fact]
    public void APlayerInAPartyStartsNoOther()
    {
        var session = new UserSession(NSubstitute.Substitute.For<LibreKO.Common.Infrastructure.Network.IClient>(), Leader, Leader);

        session.TryStartParty(1).Should().BeTrue();
        session.TryStartParty(2).Should().BeFalse();
        session.PartyIndex.Should().Be(1);
    }
}
