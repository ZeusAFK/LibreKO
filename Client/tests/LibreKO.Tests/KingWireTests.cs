using System.Linq;
using System.Text;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class KingWireTests
{
    private static Packet Reply(params object[] parts)
    {
        var p = new Packet(GameOpcodes.GS_KING);
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte b: p.WriteByte(b); break;
                case short s: p.WriteShort(s); break;
                case uint u: p.WriteUInt(u); break;
                case int i: p.WriteInt(i); break;
                case string text: p.WriteSByteString(text); break;
            }
        }
        p.ResetOffset();
        return p;
    }

    private static Packet Long(Packet p, string text)
    {
        p.WriteString(text);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheScheduleCarriesItsKindAndDate()
    {
        var schedule = KingWire.ReadSchedule(Reply((byte)1, (byte)10, (byte)12, (byte)20, (byte)30));

        Assert.Equal(KingElection.ScheduleElection, schedule.Kind);
        Assert.Equal((10, 12, 20, 30), (schedule.Month, schedule.Day, schedule.Hour, schedule.Minute));
    }

    [Fact]
    public void AScheduleWithoutAKindReadsTheSenatorState()
    {
        Assert.Equal(4, KingWire.ReadSchedule(Reply((byte)0, (byte)4)).SenatorState);
        var empty = KingWire.ReadSchedule(Reply());
        Assert.Equal(KingElection.ScheduleNone, empty.Kind);
        Assert.Equal(0, empty.SenatorState);
    }

    [Fact]
    public void EachCandidateLeadsWithItsNumber()
    {
        var list = KingWire.ReadCandidates(Reply((short)1, (byte)2, (byte)7, "Rikka", "Valhalla", (byte)9, "Zeus", ""));

        Assert.Equal(KingElection.Success, list.Result);
        Assert.Equal([7, 9], list.Candidates.Select(c => c.Number));
        Assert.Equal(["Rikka", "Zeus"], list.Candidates.Select(c => c.Name));
        Assert.Equal("Valhalla", list.Candidates[0].Clan);
    }

    [Fact]
    public void ARefusedCandidateListIsEmpty()
    {
        var list = KingWire.ReadCandidates(Reply((short)-1));

        Assert.Equal(KingElection.Refused, list.Result);
        Assert.Empty(list.Candidates);
    }

    [Fact]
    public void APlanLeadsWithTheResultAndHasAWordLength()
    {
        var plan = KingWire.ReadPlan(Long(Reply((short)1), "Fair taxes for all."));

        Assert.Equal(KingElection.Success, plan.Result);
        Assert.Equal("Fair taxes for all.", plan.Text);
        Assert.Equal("", KingWire.ReadPlan(Reply((short)-2)).Text);
    }

    [Fact]
    public void TheShoutListsEveryNomineesPlan()
    {
        var shout = KingWire.ReadShout(Long(Reply((short)1, (byte)1, (byte)3, "Rikka"), "Vote Rikka"));

        Assert.Equal(new KingShoutLine(3, "Rikka", "Vote Rikka"), shout.Lines.Single());
    }

    [Fact]
    public void SenatorsAreAListOfNames()
    {
        var senators = KingWire.ReadSenators(Reply((short)1, (byte)2, "Rikka", "Hermes"));

        Assert.Equal(["Rikka", "Hermes"], senators.Names);
    }

    [Fact]
    public void TheKingSeesTributeAndTreasuryWhileACitizenSeesTheTreasury()
    {
        var king = KingWire.ReadTreasury(Reply((short)1, 12_500_000u, 845_300_000u));
        var citizen = KingWire.ReadTreasury(Reply((short)2, 845_300_000u, 0u));

        Assert.Equal((12_500_000L, 845_300_000L), (king.Tribute, king.Treasury));
        Assert.Equal(845_300_000L, citizen.Treasury);
        Assert.Equal(0L, citizen.Tribute);
        Assert.Equal(KingElection.Refused, KingWire.ReadTreasury(Reply((short)-1)).View);
    }

    [Fact]
    public void FundAndTariffRepliesLeadWithAWordResult()
    {
        var fund = KingWire.ReadCoins(Reply((short)1, 900u, 400u));
        var tariff = KingWire.ReadTariff(Reply((short)1, (byte)4));

        Assert.Equal((900L, 400L), (fund.Coins, fund.Amount));
        Assert.Equal(4, tariff.Tariff);
        Assert.Equal(0, KingWire.ReadTariff(Reply((short)-1)).Tariff);
    }

    [Fact]
    public void TheTreasuryNoticeIsShownOnlyWhenFlagged()
    {
        var notice = KingWire.ReadTreasuryNotice(Reply((byte)1, 5_000, 70_000));

        Assert.True(notice.Shown);
        Assert.Equal((5_000, 70_000), (notice.Used, notice.Left));
        Assert.False(KingWire.ReadTreasuryNotice(Reply((byte)0)).Shown);
    }

    [Fact]
    public void ATruncatedNameReadsAsEmptyInsteadOfThrowing()
    {
        var p = new Packet(GameOpcodes.GS_KING);
        p.WriteByte(10);
        p.WriteBytes(Encoding.ASCII.GetBytes("Rik"));
        p.ResetOffset();

        Assert.Equal("", KingWire.Str8(p));
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ThePlanIsSentWithAWordLength()
    {
        var bytes = KingWire.Plan("Hi").GetBytes();

        Assert.Equal(new byte[] { 0x78, 1, 3, 1, 2, 0, (byte)'H', (byte)'i' }, bytes);
    }

    [Fact]
    public void TheIntroductionIsSentWithAWordLength()
    {
        Assert.Equal(new byte[] { 0x78, 6, 2, 1, 0, (byte)'A' }, KingWire.Intro("A").GetBytes());
    }

    [Fact]
    public void AVoteNamesTheCandidate()
    {
        var bytes = KingWire.Named("Zeus", KingElection.Election, KingElection.Poll, KingElection.PollVote).GetBytes();

        Assert.Equal(new byte[] { 0x78, 1, 4, 2, 4, (byte)'Z', (byte)'e', (byte)'u', (byte)'s' }, bytes);
    }

    [Fact]
    public void BallotsSendOneForAndTwoAgainst()
    {
        Assert.Equal(1, KingWire.Ballot(true));
        Assert.Equal(2, KingWire.Ballot(false));
        Assert.Equal(new byte[] { 0x78, 2, 4, 2 },
            KingWire.Request(KingElection.Impeachment, KingElection.ImpeachPublicVote, KingWire.Ballot(false)).GetBytes());
    }
}
