using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class PartySessionTests
{
    private const int RejoinId = 7;
    private const string RejoinName = "Rikka";
    private const int RejoinMaxMembers = 8;

    private const int SelfId = 7;
    private const byte Joined = 1;
    private const byte Leader = 100;
    private const byte Poisoned = 3;

    private static PartySession NewParty()
    {
        var self = new SelfSession();
        self.ApplyCharacterId(SelfId);
        return new PartySession(self);
    }

    private static PartyMember Member(int id, byte code = Joined, string name = "Velkan") =>
        new(id, code, name, 100, 90, 60, 105, 50, 40);

    [Fact]
    public void MembershipFollowsTheMemberList()
    {
        var party = NewParty();

        party.Apply(Member(9));

        Assert.True(party.IsMember(9));
        Assert.True(party.IsMember(SelfId));
        Assert.False(party.IsMember(11));
    }

    [Fact]
    public void TheFirstMemberRecordPutsUsInFrontOfThem()
    {
        var party = NewParty();

        party.Apply(Member(9));

        Assert.Equal(new[] { SelfId, 9 }, party.Members.Select(m => m.CharId));
        Assert.True(party.InParty);
    }

    [Fact]
    public void WhileJoiningWeWaitForTheServerToListUs()
    {
        var party = NewParty();
        party.Joining = true;

        party.Apply(Member(9));
        Assert.Equal(new[] { 9 }, party.Members.Select(m => m.CharId));

        party.Apply(Member(SelfId, name: "Me"));
        Assert.False(party.Joining);
        Assert.Equal(new[] { 9, SelfId }, party.Members.Select(m => m.CharId));
    }

    [Fact]
    public void ANewLeaderMovesToTheFront()
    {
        var party = NewParty();
        party.Apply(Member(9));
        party.Apply(Member(11));

        party.Apply(Member(11, Leader));

        Assert.Equal(11, party.Members[0].CharId);
        Assert.Equal(3, party.Members.Count);
    }

    [Fact]
    public void RemovingAMemberForgetsItsStatusAndCommanderRole()
    {
        var party = NewParty();
        party.Apply(Member(9, name: "Aria"));
        party.SetStatus(9, Poisoned, true);
        party.ApplyCommander(new CommanderNamedReply(9, "Aria"));

        string name = party.Remove(9);

        Assert.Equal("Aria", name);
        Assert.Null(party.StatusOf(9));
        Assert.Equal(PartySession.NoCommander, party.PartyCommanderId);
        Assert.DoesNotContain(party.Members, m => m.CharId == 9);
    }

    [Fact]
    public void AStatusIsAddedThenRemoved()
    {
        var party = NewParty();
        party.Apply(Member(9));

        party.SetStatus(9, Poisoned, true);
        Assert.Contains(Poisoned, party.StatusOf(9)!);

        party.SetStatus(9, Poisoned, false);
        Assert.DoesNotContain(Poisoned, party.StatusOf(9)!);
    }

    [Fact]
    public void ClearDropsEverything()
    {
        var party = NewParty();
        party.Joining = true;
        party.Apply(Member(9));
        party.SetBuffs(9, new[] { 1, 2 });
        party.SetPing(9, 42);
        party.SetLevelPermille(9, 500);
        party.ApplyCommander(new CommanderNamedReply(9, "Aria"));

        party.Clear();

        Assert.False(party.InParty);
        Assert.False(party.Joining);
        Assert.Empty(party.BuffsOf(9));
        Assert.Equal(PartySignal.Unknown, party.PingOf(9));
        Assert.Null(party.LevelPermilleOf(9));
        Assert.Equal(PartySession.NoCommander, party.PartyCommanderId);
    }

    [Fact]
    public void UpdateRewritesOneMemberInPlace()
    {
        var party = NewParty();
        party.Apply(Member(9));

        party.Update(9, m => new PartyMember(m.CharId, m.SuccessCode, m.Name, m.MaxHp, 1, m.Level, m.Class, m.MaxMp, m.Mp));

        Assert.Equal(1, party.Members[party.IndexOf(9)].Hp);
    }

    [Fact]
    public void LeavingThePartyForgetsRejoinsButKeepsTheSwitch()
    {
        var party = NewParty();
        party.Rejoin.Remember(RejoinId, RejoinName);
        party.Rejoin.Toggle();

        party.Clear();

        Assert.False(party.Rejoin.On);
        party.Rejoin.Toggle();
        Assert.False(party.Rejoin.Recall(RejoinName, amLeader: true, partyCount: 2, maxMembers: RejoinMaxMembers, alreadyMember: false));
    }
}
