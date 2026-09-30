using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class KnightsPacketWriterTests
{
    [Fact]
    public void ResultIsSubThenCode()
    {
        var packet = KnightsPacketWriter.Result(KnightsSubOpcode.Withdraw, KnightsPacketWriter.Failed);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Withdraw);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Failed);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClanCreatedCarriesTheGradeThenTheRankingThenTheGold()
    {
        var packet = KnightsPacketWriter.ClanCreated(4321, 42, "Wolves", 3, 1, 1_000_000);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Create);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        packet.ReadInt().Should().Be(4321);
        packet.ReadShort().Should().Be(42);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadByte().Should().Be(3);
        packet.ReadByte().Should().Be(1);
        packet.ReadInt().Should().Be(1_000_000);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void JoinAcceptedCarriesTheAllianceCapeMarkAndStanding()
    {
        var packet = KnightsPacketWriter.JoinAccepted(new KnightsPacketWriter.JoinedState(
            CharacterId: 4321, ClanId: 42, ClanName: "Wolves", Fame: 5, ClanType: 3, AllianceId: 7,
            MarkVersion: 9, CapeId: 133, CapeColour: 0x030201, Grade: 1, Ranking: 2));
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Join);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        packet.ReadInt().Should().Be(4321);
        packet.ReadShort().Should().Be(42);
        packet.ReadByte().Should().Be(5);
        packet.ReadByte().Should().Be(3);
        packet.ReadShort().Should().Be(7);
        packet.ReadShort().Should().Be(133);
        packet.ReadInt().Should().Be(0x030201);
        packet.ReadShort().Should().Be(9);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void WithdrewAndFameChangedNameThePlayerForTheWholeRegion()
    {
        var withdrew = KnightsPacketWriter.Withdrew(4321, 42, 5);
        withdrew.ResetOffset();
        withdrew.ReadByte().Should().Be((byte)KnightsSubOpcode.Withdraw);
        withdrew.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        withdrew.ReadInt().Should().Be(4321);
        withdrew.ReadShort().Should().Be(42);
        withdrew.ReadByte().Should().Be(5);
        withdrew.RemainingBytes.Should().Be(0);

        var fame = KnightsPacketWriter.FameChanged(4321, 0, 0);
        fame.ResetOffset();
        fame.ReadByte().Should().Be((byte)KnightsSubOpcode.ModifyFame);
        fame.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        fame.ReadInt().Should().Be(4321);
        fame.ReadShort().Should().Be(0);
        fame.ReadByte().Should().Be(0);
        fame.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void InvitationNamesTheInviterTheClanAndItsName()
    {
        var packet = KnightsPacketWriter.Invitation(77, 42, "Wolves");
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Invite);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        packet.ReadInt().Should().Be(77);
        packet.ReadShort().Should().Be(42);
        packet.ReadString().Should().Be("Wolves");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PresenceCarriesAOneByteLengthName()
    {
        var packet = KnightsPacketWriter.MemberOnline("Aurelia");
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.MemberOnline);
        packet.ReadSByteString().Should().Be("Aurelia");
        packet.RemainingBytes.Should().Be(0);

        KnightsPacketWriter.MemberOffline("Aurelia").GetData()[0].Should().Be((byte)KnightsSubOpcode.MemberOffline);
    }

    [Fact]
    public void ClanBrowseListCarriesOnlyIdAndName()
    {
        var packet = KnightsPacketWriter.ClanBrowseList(new[]
        {
            new KnightsPacketWriter.BrowseEntry(42, "Wolves"),
        });
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_KNIGHTS_LIST);
        packet.ReadByte().Should().Be(KnightsPacketWriter.BrowseListPage);
        packet.ReadShort().Should().Be(1);
        packet.ReadShort().Should().Be(42);
        packet.ReadString().Should().Be("Wolves");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void MemberListCarriesTheOnlineCountTheCapTheNoticeAndEightFieldEntries()
    {
        var packet = KnightsPacketWriter.MemberList(
            1, 50, "raid at eight", new[]
            {
                new KnightsPacketWriter.Member("Aurelia", 1, 62, 105, true),
                new KnightsPacketWriter.Member("Dorin", 5, 60, 102, false, "tank", 187),
            });
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.MemberRequest);
        packet.ReadByte().Should().Be(KnightsPacketWriter.MemberListPage);
        packet.ReadShort().Should().Be(KnightsPacketWriter.Reserved);
        packet.ReadShort().Should().Be(1);
        packet.ReadShort().Should().Be(50);
        packet.ReadString().Should().Be("raid at eight");
        packet.ReadShort().Should().Be(2);

        packet.ReadString().Should().Be("Aurelia");
        packet.ReadByte().Should().Be(1);
        packet.ReadSByteString().Should().Be(string.Empty);
        packet.ReadByte().Should().Be(62);
        packet.ReadShort().Should().Be(105);
        packet.ReadByte().Should().Be(1);
        packet.ReadString().Should().Be(string.Empty);
        packet.ReadInt().Should().Be(0);

        packet.ReadString().Should().Be("Dorin");
        packet.ReadByte().Should().Be(5);
        packet.ReadSByteString().Should().Be(string.Empty);
        packet.ReadByte().Should().Be(60);
        packet.ReadShort().Should().Be(102);
        packet.ReadByte().Should().Be(0);
        packet.ReadString().Should().Be("tank");
        packet.ReadInt().Should().Be(187);

        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PointStatusAndDonationReplyCarryTheFundAfterThePlayersPoints()
    {
        var status = KnightsPacketWriter.PointStatus(12_345, 250_000);
        status.ResetOffset();
        status.ReadByte().Should().Be((byte)KnightsSubOpcode.PointRequest);
        status.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        status.ReadInt().Should().Be(12_345);
        status.ReadInt().Should().Be(250_000);
        status.RemainingBytes.Should().Be(0);

        var donated = KnightsPacketWriter.DonateAccepted(11_345, 251_000, 1_000);
        donated.ResetOffset();
        donated.ReadByte().Should().Be((byte)KnightsSubOpcode.DonatePoints);
        donated.ReadByte().Should().Be((byte)KnightsDonateResult.Succeeded);
        donated.ReadInt().Should().Be(11_345);
        donated.ReadInt().Should().Be(251_000);
        donated.ReadByte().Should().Be(0);
        donated.ReadInt().Should().Be(1_000);
        donated.RemainingBytes.Should().Be(0);

        var refused = KnightsPacketWriter.DonateRefused(KnightsDonateResult.ClanNotAccredited);
        refused.ResetOffset();
        refused.ReadByte().Should().Be((byte)KnightsSubOpcode.DonatePoints);
        refused.ReadByte().Should().Be(6);
        refused.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void DonationListCountsInOneByteAndLeaderPointsInTwo()
    {
        var donations = KnightsPacketWriter.DonationList(new[] { new KnightsPacketWriter.Donator("Aurelia", 41_200) });
        donations.ResetOffset();
        donations.ReadByte().Should().Be((byte)KnightsSubOpcode.DonationList);
        donations.ReadByte().Should().Be(1);
        donations.ReadString().Should().Be("Aurelia");
        donations.ReadInt().Should().Be(41_200);
        donations.RemainingBytes.Should().Be(0);

        var points = KnightsPacketWriter.LeaderPoints(new[] { new KnightsPacketWriter.Donator("Aurelia", 300) });
        points.ResetOffset();
        points.ReadByte().Should().Be((byte)KnightsSubOpcode.LeaderPoints);
        points.ReadUShort().Should().Be(1);
        points.ReadString().Should().Be("Aurelia");
        points.ReadInt().Should().Be(300);
        points.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClanUpdateEndsWithThePointFund()
    {
        var packet = KnightsPacketWriter.ClanUpdate(42, 3, 133, 1, 2, 3, 250_000);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Update);
        packet.ReadShort().Should().Be(42);
        packet.ReadByte().Should().Be(3);
        packet.ReadShort().Should().Be(133);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be(3);
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(250_000);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void StandingRefreshListsClanIdGradeAndRanking()
    {
        var packet = KnightsPacketWriter.StandingRefresh(new[]
        {
            new KnightsPacketWriter.StandingEntry(42, 1, 2),
        });
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.AllListRequest);
        packet.ReadShort().Should().Be(1);
        packet.ReadShort().Should().Be(42);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AllianceRepliesCarryTheCapeAndItsColour()
    {
        var invite = KnightsPacketWriter.AllianceInvite(KnightsSubOpcode.AllyReq, "Wolves", 42);
        invite.ResetOffset();
        invite.ReadByte().Should().Be((byte)KnightsSubOpcode.AllyReq);
        invite.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        invite.ReadSByteString().Should().Be("Wolves");
        invite.ReadShort().Should().Be(42);
        invite.RemainingBytes.Should().Be(0);

        var joined = KnightsPacketWriter.AllianceJoined(42, 43, 133, 0x030201);
        joined.ResetOffset();
        joined.ReadByte().Should().Be((byte)KnightsSubOpcode.AllyInsert);
        joined.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        joined.ReadShort().Should().Be(42);
        joined.ReadShort().Should().Be(43);
        joined.ReadShort().Should().Be(133);
        joined.ReadInt().Should().Be(0x030201);
        joined.RemainingBytes.Should().Be(0);

        var left = KnightsPacketWriter.AllianceLeft(KnightsSubOpcode.AllyRemove, 42, 43, -1, 0);
        left.ResetOffset();
        left.ReadByte().Should().Be((byte)KnightsSubOpcode.AllyRemove);
        left.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        left.ReadShort().Should().Be(42);
        left.ReadShort().Should().Be(43);
        left.ReadShort().Should().Be(-1);
        left.ReadInt().Should().Be(0);
        left.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AllianceListNestsTheOfficersUnderEachClan()
    {
        var packet = KnightsPacketWriter.AllianceList("siege sunday", new[]
        {
            new KnightsPacketWriter.AllianceClan(42, "Wolves", true, new[]
            {
                new KnightsPacketWriter.AllianceOfficer(1, "Aurelia"),
                new KnightsPacketWriter.AllianceOfficer(2, "Dorin"),
            }),
        });
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.AllyList);
        packet.ReadByte().Should().Be(1);
        packet.ReadString().Should().Be("siege sunday");
        packet.ReadShort().Should().Be(42);
        packet.ReadSByteString().Should().Be("Wolves");
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be(1);
        packet.ReadSByteString().Should().Be("Aurelia");
        packet.ReadByte().Should().Be(2);
        packet.ReadSByteString().Should().Be("Dorin");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClanMarkPrefixesTheBlobWithItsLength()
    {
        var mark = new byte[] { 1, 2, 3, 4, 5 };
        var packet = KnightsPacketWriter.ClanMark(KnightsSubOpcode.MarkReq, 1, 2, 42, 7, mark);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.MarkReq);
        packet.ReadUShort().Should().Be(1);
        packet.ReadUShort().Should().Be(2);
        packet.ReadUShort().Should().Be(42);
        packet.ReadUShort().Should().Be(7);
        packet.ReadUShort().Should().Be((ushort)mark.Length);
        packet.ReadBytes(mark.Length).Should().Equal(mark);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void CapeNpcOpensTheMantleShopOnSubTwentySeven()
    {
        var packet = KnightsPacketWriter.CapeNpc();
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.ReadByte().Should().Be(0x1B);
        packet.RemainingBytes.Should().Be(0);
        NpcServicePacketWriter.ClanCapeSub.Should().Be(0x1B);
    }

    [Fact]
    public void CapeUsesItsOwnOpcodeNotTheKnightsOne()
    {
        var packet = KnightsPacketWriter.Cape(42, 12, 1, 2, 3);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CAPE);
        packet.ReadShort().Should().Be((short)CapeResult.Changed);
        packet.ReadShort().Should().Be(42);
        packet.ReadShort().Should().Be(KnightsPacketWriter.Reserved);
        packet.ReadShort().Should().Be(12);
        packet.ReadInt().Should().Be(0x030201);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void CapeRefusalIsASignedShort()
    {
        var packet = KnightsPacketWriter.CapeRefused(CapeResult.MissingPurchaseItem);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CAPE);
        packet.ReadShort().Should().Be(-8);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void NoticeRefusalTravelsOnItsOwnSub()
    {
        var packet = KnightsPacketWriter.NoticeRefused(KnightsNoticeResult.NoAuthority);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.NoticeResult);
        packet.ReadByte().Should().Be((byte)KnightsNoticeResult.NoAuthority);
        packet.RemainingBytes.Should().Be(0);
    }
}
