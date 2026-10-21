using FluentAssertions;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class PartyGroupTests
{
    private const short Leader = 1;
    private const int Joiners = PartyGroup.MaxMembers;
    private const int JoinRounds = 200;
    private const int Parties = 500;

    [Fact]
    public void PlayersJoiningAtOnceFillEachSlotOnce()
    {
        for (var round = 0; round < JoinRounds; round++)
        {
            var party = new PartyManager().CreateParty(Leader);
            var joined = new System.Collections.Concurrent.ConcurrentBag<short>();
            using var start = new Barrier(Joiners);
            var threads = Enumerable.Range(Leader + 1, Joiners).Select(joiner => new Thread(() =>
            {
                start.SignalAndWait();
                if (party.TryJoin((short)joiner))
                    joined.Add((short)joiner);
            })).ToList();
            threads.ForEach(thread => thread.Start());
            threads.ForEach(thread => thread.Join());

            party.MemberIds.Should().OnlyHaveUniqueItems();
            party.MemberIds.Skip(1).Should().BeEquivalentTo(joined, "everyone told they joined holds a slot");
        }
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
    public void APlayerInAPartyEntersNoOther()
    {
        var session = new UserSession(NSubstitute.Substitute.For<LibreKO.Common.Infrastructure.Network.IClient>(), Leader, Leader);

        session.TryEnterParty(1).Should().BeTrue();
        session.TryEnterParty(2).Should().BeFalse();
        session.PartyIndex.Should().Be(1);
    }

    [Fact]
    public void APlayerLeavesOnlyThePartyTheyAreIn()
    {
        var session = new UserSession(NSubstitute.Substitute.For<LibreKO.Common.Infrastructure.Network.IClient>(), Leader, Leader);
        session.TryEnterParty(1);

        session.TryLeaveParty(2).Should().BeFalse("a party the player already left cannot clear the one they are in now");
        session.PartyIndex.Should().Be(1);
        session.TryLeaveParty(1).Should().BeTrue();
        session.IsInParty.Should().BeFalse();
    }
}
