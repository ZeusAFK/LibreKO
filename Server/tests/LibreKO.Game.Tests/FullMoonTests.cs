using System.Text;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class FullMoonTests
{
    private const int BagSlot = 14;

    private static UserSession Member(int id, byte zone = (byte)ZoneId.Moradon, bool voucher = true, short hp = 100)
    {
        var session = new UserSession(Substitute.For<IClient>(), id, id + 1000) { ZoneId = zone, Hp = hp };
        for (int i = 0; i < session.Inventory.Length; i++) session.Inventory[i] = new ItemSlot();
        if (voucher)
        {
            session.Inventory[BagSlot].ItemId = FullMoonRules.VoucherItem;
            session.Inventory[BagSlot].Count = 1;
        }
        return session;
    }

    [Fact]
    public void EntryIsCheckedInThe2619Order()
    {
        var leader = Member(1);
        FullMoonRules.EntryRefusal(null, [], FullMoonRules.FullMoonBoard).Should().Be(FullMoonRules.NoParty);
        FullMoonRules.EntryRefusal(leader, [leader, Member(2)], FullMoonRules.NormalBoard, requireFullMoonBoard: true).Should().Be(FullMoonRules.NotFullMoonParty);
        FullMoonRules.EntryRefusal(leader, [leader, Member(2)], FullMoonRules.NormalBoard, requireFullMoonBoard: false).Should().Be(FullMoonRules.Ready);
        FullMoonRules.EntryRefusal(leader, [leader, Member(2, (byte)ZoneId.RonarkLand)], FullMoonRules.FullMoonBoard)
            .Should().Be(FullMoonRules.NotAllInMoradon);
        FullMoonRules.EntryRefusal(leader, [leader, Member(2, hp: 0)], FullMoonRules.FullMoonBoard).Should().Be(FullMoonRules.MemberDead);
        FullMoonRules.EntryRefusal(leader, [leader, Member(2, voucher: false)], FullMoonRules.FullMoonBoard)
            .Should().Be(FullMoonRules.MemberWithoutVoucher);
    }

    [Fact]
    public void AReadyPartyIsToldReadyToEnter()
    {
        var leader = Member(1);
        FullMoonRules.EntryRefusal(leader, [leader, Member(2)], FullMoonRules.FullMoonBoard).Should().Be(FullMoonRules.Ready);
    }

    [Theory]
    [InlineData(1, FullMoonRules.DifficultyEasy, FullMoonRules.EasyMinStage, FullMoonRules.EasyMaxStage)]
    [InlineData(2, FullMoonRules.DifficultyEasy, FullMoonRules.EasyMinStage, FullMoonRules.EasyMaxStage)]
    [InlineData(3, FullMoonRules.DifficultyEasy, FullMoonRules.EasyMinStage, FullMoonRules.EasyMaxStage)]
    [InlineData(4, FullMoonRules.DifficultyNormal, FullMoonRules.NormalMinStage, FullMoonRules.NormalMaxStage)]
    [InlineData(7, FullMoonRules.DifficultyNormal, FullMoonRules.NormalMinStage, FullMoonRules.NormalMaxStage)]
    [InlineData(8, FullMoonRules.DifficultyHard, FullMoonRules.HardMinStage, FullMoonRules.HardMaxStage)]
    public void DifficultyAndStageRangesMatchPartySizes(int partyCount, byte expectedDiff, int expectedMin, int expectedMax)
    {
        var diff = FullMoonRules.DifficultyForPartySize(partyCount);
        diff.Should().Be(expectedDiff);
        FullMoonRules.StartingStageForDifficulty(diff).Should().Be(expectedMin);
        FullMoonRules.MaxStageForDifficulty(diff).Should().Be(expectedMax);
    }

    [Theory]
    [InlineData(FullMoonRules.DifficultyEasy, 1, 5, 1)]
    [InlineData(FullMoonRules.DifficultyEasy, 5, 5, 5)]
    [InlineData(FullMoonRules.DifficultyNormal, 6, 12, 1)]
    [InlineData(FullMoonRules.DifficultyNormal, 17, 12, 12)]
    [InlineData(FullMoonRules.DifficultyHard, 18, 18, 1)]
    [InlineData(FullMoonRules.DifficultyHard, 35, 18, 18)]
    public void DisplayStageCalculatesCorrectMaxAndCurrent(byte diff, int stageId, byte expectedMax, byte expectedCurrent)
    {
        var (maxDisplay, currentDisplay) = FullMoonRules.DisplayStage(diff, stageId);
        maxDisplay.Should().Be(expectedMax);
        currentDisplay.Should().Be(expectedCurrent);
    }

    [Fact]
    public void StageProviderLoadsAll35Stages()
    {
        var provider = new FullMoonRiftStageProvider();
        provider.Stages.Count.Should().Be(35);

        var stage1 = provider.GetStage(1);
        stage1.Should().NotBeNull();
        stage1!.Difficulty.Should().Be(1);
        stage1.Spawns[0].MonsterId.Should().Be(9927);

        var finalBossStage = provider.GetStage(35);
        finalBossStage.Should().NotBeNull();
        finalBossStage!.Difficulty.Should().Be(3);
        finalBossStage.Spawns[0].MonsterId.Should().Be(9942);
    }

    [Theory]
    [InlineData(0, 0, 0, true, 21, 55, true)]
    [InlineData(2, 0, 0, true, 21, 55, false)]
    [InlineData(3, 0, 0, false, 21, 55, false)]
    [InlineData(0, 21, 0, false, 71, 55, false)]
    [InlineData(0, 0, 6, false, 21, 55, true)]
    [InlineData(0, 0, 6, false, 21, 61, false)]
    [InlineData(0, 0, 9, false, 21, 83, true)]
    public void TheNormalBoardFiltersByKindZoneAndLevelBand(byte state, short zone, byte level, bool party, short candidateZone,
        byte candidateLevel, bool passes) =>
        FullMoonRules.PassesFilter(state, zone, level, party, candidateZone, candidateLevel).Should().Be(passes);

    [Fact]
    public void ThePartyRecordCarriesTheMarkedTargetAndThePartyType()
    {
        var packet = PartyPacketWriter.MemberInfo(new PartyPacketWriter.MemberState(7, "Velkan", 60, 101, 900, 800, 500, 400),
            markedTarget: 42, partyType: FullMoonRules.FullMoonBoard);
        packet.ResetOffset();
        var bytes = packet.ReadBytes(packet.RemainingBytes);

        BitConverter.ToInt32(bytes, bytes.Length - 6).Should().Be(42);
        bytes[^2].Should().Be(FullMoonRules.FullMoonBoard);
    }

    [Fact]
    public void HasEnteredTodayChecksDailyOperationTimestamp()
    {
        var session = Member(1);
        FullMoonRules.HasEnteredToday(session).Should().BeFalse();

        var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.DailyOps[(int)DailyOperation.FullMoonRift] = now;
        FullMoonRules.HasEnteredToday(session, now).Should().BeTrue();

        FullMoonRules.HasEnteredToday(session, now + 1441 * 60).Should().BeFalse();
    }

    [Fact]
    public void HasVoucherRespectsItemExpiry()
    {
        var session = Member(1);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Inventory[BagSlot].ExpiresAt = now + 1000;
        FullMoonRules.HasVoucher(session, now).Should().BeTrue();

        FullMoonRules.HasVoucher(session, now + 1001).Should().BeFalse();
    }

    [Fact]
    public void StageMonsterCountsMatchDesignSpecification()
    {
        var provider = new FullMoonRiftStageProvider();
        int[] expectedCounts =
        [
            2, 1, 2, 2, 1,
            4, 2, 3, 3, 1, 3, 2, 3, 3, 3, 2, 1,
            4, 2, 3, 3, 1, 3, 2, 4, 3, 3, 2, 1, 4, 2, 3, 3, 2, 1
        ];

        for (int stageId = 1; stageId <= 35; stageId++)
        {
            var stage = provider.GetStage(stageId);
            stage.Should().NotBeNull();
            stage!.Spawns[0].Count.Should().Be(expectedCounts[stageId - 1], $"Stage {stageId} count mismatch");
        }
    }
}