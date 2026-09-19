using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class AchievementTests
{
    [Fact]
    public void AchievementSitsOnTheOpcodeRetailDispatches()
    {
        ((byte)GameOpcodes.GS_ACHIEVEMENT).Should().Be(0x99);
    }

    [Fact]
    public void ListRowsAreTheSevenByteShapeRetailParses()
    {
        var packet = AchievementPacketWriter.List(
        [
            new AchievementPacketWriter.Row(1, AchievementProgressState.Achieved, 10, 10),
            new AchievementPacketWriter.Row(458, AchievementProgressState.InProgress, 3, 100),
        ]);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ACHIEVEMENT);
        packet.ReadByte().Should().Be((byte)AchievementSubOpcode.List);
        packet.ReadUShort().Should().Be(2);

        packet.ReadUShort().Should().Be(1);
        packet.ReadByte().Should().Be((byte)AchievementProgressState.Achieved);
        packet.ReadUShort().Should().Be(10);
        packet.ReadUShort().Should().Be(10);

        packet.ReadUShort().Should().Be(458);
        packet.ReadByte().Should().Be((byte)AchievementProgressState.InProgress);
        packet.ReadUShort().Should().Be(3);
        packet.ReadUShort().Should().Be(100);

        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ACharacterSavedBeforeTheClaimedStateMovedStillReadsBackAsClaimed()
    {
        var state = new AchievementState();
        state.Load([1, 0, 42, 0, 3, 0, 0, 0, 2]);

        state.Of(42).State.Should().Be(AchievementProgressState.Claimed,
            "characters were persisted with 2 for claimed before it moved to retail's 5, and losing "
            + "that would let every claimed achievement be claimed again");
        state.Serialize()[8].Should().Be((byte)AchievementProgressState.Claimed);
    }

    [Fact]
    public void AClaimedRowIsSentHighEnoughForTheRetailButtonToGoInert()
    {
        var packet = AchievementPacketWriter.List(
            [new AchievementPacketWriter.Row(1, AchievementProgressState.Claimed, 10, 10)]);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadUShort();
        packet.ReadUShort();

        packet.ReadByte().Should().BeGreaterThanOrEqualTo(4,
            "the retail row button stays live for any state below 4, so a claimed achievement sent as "
            + "2 keeps offering itself");
    }

    [Theory]
    [InlineData(AchievementPacketWriter.ClaimIssued)]
    [InlineData(AchievementPacketWriter.ClaimInventoryFull)]
    [InlineData(AchievementPacketWriter.ClaimItemMissing)]
    public void ClaimResultCarriesRetailsSignedCode(sbyte result)
    {
        var packet = AchievementPacketWriter.ClaimResult(73, result);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)AchievementSubOpcode.ClaimResult);
        packet.ReadUShort().Should().Be(73);
        ((sbyte)packet.ReadShort()).Should().Be(result);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ProgressBecomesClaimableOnlyOnceTheTargetIsReached()
    {
        var state = new AchievementState();
        var definition = new AchievementData { Id = 5, Target = 3 };

        state.Advance(definition, 1).Should().BeFalse();
        state.Of(5).State.Should().Be(AchievementProgressState.InProgress);

        state.Advance(definition, 1).Should().BeFalse();
        state.Advance(definition, 1).Should().BeTrue();

        state.Of(5).State.Should().Be(AchievementProgressState.Achieved,
            "reaching the target is where the state machine stops; handing the reward over and "
            + "moving it to claimed is the service's job");
        state.Of(5).Progress.Should().Be(3);
    }

    [Fact]
    public void ProgressStopsAtTheTargetAndAfterClaiming()
    {
        var state = new AchievementState();
        var definition = new AchievementData { Id = 5, Target = 2 };

        state.Advance(definition, 10);
        state.Of(5).Progress.Should().Be(2);

        state.MarkClaimed(5);
        state.Advance(definition, 5).Should().BeFalse();
        state.Of(5).State.Should().Be(AchievementProgressState.Claimed);
        state.IsClaimed(5).Should().BeTrue();
    }

    [Fact]
    public void StateSurvivesASaveAndLoadRoundTrip()
    {
        var state = new AchievementState();
        state.Advance(new AchievementData { Id = 7, Target = 100 }, 42);
        state.Advance(new AchievementData { Id = 9, Target = 1 }, 1);
        state.MarkClaimed(9);

        var restored = new AchievementState();
        restored.Load(state.Serialize());

        restored.Of(7).Progress.Should().Be(42);
        restored.Of(7).State.Should().Be(AchievementProgressState.InProgress);
        restored.Of(9).State.Should().Be(AchievementProgressState.Claimed);
    }

    [Fact]
    public void AnEmptyStateSerializesToNothing()
    {
        new AchievementState().Serialize().Should().BeEmpty();

        var restored = new AchievementState();
        restored.Load([]);
        restored.Entries.Should().BeEmpty();
    }

    [Fact]
    public void MonsterAchievementsMatchOnAnyOfTheirNpcs()
    {
        var definition = new AchievementData
        {
            ConditionTable = AchievementConditionTable.Monster,
            Npcs = "11042,21045,32528",
        };

        definition.CountsNpc(21045).Should().BeTrue();
        definition.CountsNpc(32528).Should().BeTrue();
        definition.CountsNpc(999).Should().BeFalse();
    }

    [Fact]
    public void AnAchievementWithNoNpcListMatchesNothing()
    {
        new AchievementData().CountsNpc(1).Should().BeFalse();
    }

    [Fact]
    public void TitleBonusesLandOnTheDerivedStats()
    {
        var title = new AchievementTitleData { Defence = 40, Attack = 12, FireResist = 5 };
        var stats = new DerivedStats { TotalAc = 100, TotalHit = 200, FireR = 10 };

        AbilityCalculator.ApplyTitleBonuses(stats, title);

        stats.TotalAc.Should().Be(140);
        stats.TotalHit.Should().Be(212);
        stats.FireR.Should().Be(15);
    }

    [Fact]
    public void ATitleWithNoBonusIsReportedAsSuch()
    {
        new AchievementTitleData().HasBonus.Should().BeFalse();
        new AchievementTitleData { Defence = 40 }.HasBonus.Should().BeTrue();
    }


    [Fact]
    public void AThresholdOnlyEverMovesForward()
    {
        var state = new AchievementState();
        var definition = new AchievementData { Id = 386, Target = 10 };

        state.Reach(definition, 4).Should().BeFalse();
        state.Of(386).Progress.Should().Be(4);

        state.Reach(definition, 2).Should().BeFalse();
        state.Of(386).Progress.Should().Be(4);

        state.Reach(definition, 40).Should().BeTrue();
        state.Of(386).Progress.Should().Be(10);
        state.Of(386).State.Should().Be(AchievementProgressState.Achieved);
    }

    [Fact]
    public void AnOverCapTargetIsSentAsAProportionRetailsFieldCanHold()
    {
        var packet = AchievementPacketWriter.List(
        [
            new AchievementPacketWriter.Row(
                377, (byte)AchievementProgressState.InProgress, 250_000, 1_000_000),
        ]);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadUShort();
        packet.ReadUShort().Should().Be(377);
        packet.ReadByte();
        packet.ReadUShort().Should().Be(AchievementPacketWriter.ScaledTarget / 4);
        packet.ReadUShort().Should().Be(AchievementPacketWriter.ScaledTarget);
    }

    [Fact]
    public async Task LevelAndContributionAchievementsReadTheCharactersCurrentValues()
    {
        var session = NewSession();
        session.Level = 30;
        session.Loyalty = 600;
        session.KnightsPoints = 100;

        var service = NewProgressService(
            new AchievementData
            {
                Id = 388, ConditionTable = AchievementConditionTable.Normal,
                Kind = (byte)NormalAchievementKind.Level, Target = 30,
            },
            new AchievementData
            {
                Id = 370, ConditionTable = AchievementConditionTable.Normal,
                Kind = (byte)NormalAchievementKind.NationalContribution, Target = 500,
            },
            new AchievementData
            {
                Id = 401, ConditionTable = AchievementConditionTable.Normal,
                Kind = (byte)NormalAchievementKind.KnightsContribution, Target = 500,
            });

        await service.RefreshAsync(session);

        session.Achievements.Of(388).State.Should().Be(AchievementProgressState.Claimed);
        session.Achievements.Of(370).State.Should().Be(AchievementProgressState.Claimed);
        session.Achievements.Of(401).Progress.Should().Be(100);
        session.Achievements.Of(401).State.Should().Be(AchievementProgressState.InProgress);
    }

    [Fact]
    public async Task CompletionAchievementsCountTheQuestsTheyName()
    {
        var session = NewSession();
        session.Quest.QuestMap[60] = (byte)QuestStatus.Completed;
        session.Quest.QuestMap[62] = (byte)QuestStatus.ReadyToTurnIn;

        var service = NewProgressService(new AchievementData
        {
            Id = 88, ConditionTable = AchievementConditionTable.Completion,
            Kind = (byte)CompletionAchievementKind.Quests, Requires = "60,62", Target = 2,
        });

        await service.RefreshAsync(session);
        session.Achievements.Of(88).Progress.Should().Be(1);

        session.Quest.QuestMap[62] = (byte)QuestStatus.Completed;
        await service.RefreshAsync(session);
        session.Achievements.Of(88).State.Should().Be(AchievementProgressState.Claimed);
    }

    [Fact]
    public async Task ACompletionChainResolvesToTheEndInOneRefresh()
    {
        var session = NewSession();

        var service = NewProgressService(
            new AchievementData
            {
                Id = 82, ConditionTable = AchievementConditionTable.Completion,
                Kind = (byte)CompletionAchievementKind.Quests, Requires = "78", Target = 1,
            },
            new AchievementData
            {
                Id = 83, ConditionTable = AchievementConditionTable.Completion,
                Kind = (byte)CompletionAchievementKind.Quests, Requires = "80", Target = 1,
            },
            new AchievementData
            {
                Id = 84, ConditionTable = AchievementConditionTable.Completion,
                Kind = (byte)CompletionAchievementKind.Achievements, Requires = "82,83", Target = 2,
            });

        session.Quest.QuestMap[78] = (byte)QuestStatus.Completed;
        session.Quest.QuestMap[80] = (byte)QuestStatus.Completed;

        await service.RefreshAsync(session);

        session.Achievements.Of(84).State.Should().Be(AchievementProgressState.Claimed);
    }

    [Fact]
    public async Task ClaimingIsNotWhatSatisfiesACompletionRequirement()
    {
        var session = NewSession();

        var service = NewProgressService(
            new AchievementData
            {
                Id = 78, ConditionTable = AchievementConditionTable.Monster, Npcs = "11042", Target = 1,
            },
            new AchievementData
            {
                Id = 82, ConditionTable = AchievementConditionTable.Completion,
                Kind = (byte)CompletionAchievementKind.Achievements, Requires = "78", Target = 1,
            });

        await service.ReportMonsterKillAsync(session, 11042);

        session.Achievements.Of(78).State.Should().Be(AchievementProgressState.Claimed);
        session.Achievements.Of(82).State.Should().Be(AchievementProgressState.Claimed);
    }

    [Fact]
    public async Task TheShippedTableUnlocksTheStarterChainFromRealQuestIds()
    {
        var session = NewSession();
        session.Quest.QuestMap[60] = (byte)QuestStatus.Completed;
        session.Quest.QuestMap[62] = (byte)QuestStatus.Completed;

        var gameData = Substitute.For<IGameDataService>();
        gameData.AchievementTable.Returns(
            new AchievementSeed().GetSeedData().ToDictionary(row => row.Id));
        var service = new AchievementProgressService(
            gameData,
            Substitute.For<IKingSystemRuntimeService>(),
            Substitute.For<IUserNotificationService>(),
            Substitute.For<ILogger<AchievementProgressService>>());

        await service.RefreshAsync(session);

        session.Achievements.Of(87).State.Should().Be(AchievementProgressState.Claimed);
        session.Achievements.Of(88).State.Should().Be(AchievementProgressState.Claimed);
        session.Achievements.Of(85).State.Should().Be(AchievementProgressState.InProgress);
    }

    [Fact]
    public async Task RonarkMonsterKillsOnlyCountInsideRonarkLand()
    {
        var definition = new AchievementData
        {
            Id = 89, ConditionTable = AchievementConditionTable.War,
            Kind = (byte)WarAchievementKind.RonarkMonsterKill, Target = 2,
        };
        var service = NewProgressService(definition);

        var elsewhere = NewSession();
        elsewhere.ZoneId = BattleZoneManager.ZONE_MORADON;
        await service.ReportMonsterKillAsync(elsewhere, 11042);
        elsewhere.Achievements.Of(89).Progress.Should().Be(0);

        var ronark = NewSession();
        ronark.ZoneId = BattleZoneManager.ZONE_RONARK_LAND;
        await service.ReportMonsterKillAsync(ronark, 11042);
        await service.ReportMonsterKillAsync(ronark, 99999);
        ronark.Achievements.Of(89).State.Should().Be(AchievementProgressState.Claimed);
    }

    [Fact]
    public void EveryClaimedTitleAddsItsBonusesTogether()
    {
        var session = NewSession();
        var gameData = Substitute.For<IGameDataService>();
        gameData.AchievementTable.Returns(new Dictionary<int, AchievementData>
        {
            [1] = new() { Id = 1, TitleId = 10, Target = 1 },
            [2] = new() { Id = 2, TitleId = 20, Target = 1 },
            [3] = new() { Id = 3, TitleId = 30, Target = 1 },
        });
        gameData.AchievementTitleTable.Returns(new Dictionary<int, AchievementTitleData>
        {
            [10] = new() { Id = 10, Defence = 40, Strength = 2 },
            [20] = new() { Id = 20, Defence = 100, FireResist = 5 },
            [30] = new() { Id = 30, Defence = 7 },
        });

        session.Achievements.Advance(gameData.AchievementTable[1], 1);
        session.Achievements.MarkClaimed(1);
        session.Achievements.Advance(gameData.AchievementTable[2], 1);
        session.Achievements.MarkClaimed(2);
        session.Achievements.Advance(gameData.AchievementTable[3], 1);

        var totals = session.TitleBonuses(gameData);

        totals.Defence.Should().Be(140);
        totals.Strength.Should().Be(2);
        totals.FireResist.Should().Be(5);
    }

    [Fact]
    public void TheStackedTotalIsRecomputedOnlyAfterItIsInvalidated()
    {
        var session = NewSession();
        var gameData = Substitute.For<IGameDataService>();
        var definition = new AchievementData { Id = 1, TitleId = 10, Target = 1 };
        gameData.AchievementTable.Returns(new Dictionary<int, AchievementData> { [1] = definition });
        gameData.AchievementTitleTable.Returns(new Dictionary<int, AchievementTitleData>
        {
            [10] = new() { Id = 10, Defence = 40 },
        });

        session.TitleBonuses(gameData).Defence.Should().Be(0);

        session.Achievements.Advance(definition, 1);
        session.Achievements.MarkClaimed(1);
        session.TitleBonuses(gameData).Defence.Should().Be(0);

        session.InvalidateTitleBonuses();
        session.TitleBonuses(gameData).Defence.Should().Be(40);
    }

    [Fact]
    public void TheTitleBroadcastNamesTheCharacterItBelongsTo()
    {
        var packet = AchievementPacketWriter.TitleChanged(70_001, 137);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ACHIEVEMENT);
        packet.ReadByte().Should().Be((byte)AchievementSubOpcode.TitleChanged);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadUShort().Should().Be(137);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task AListRequestIsAnsweredWithExactlyTheRowsItAskedFor()
    {
        var sent = new List<Packet>();
        var session = NewSessionCapturing(sent);
        var service = NewProgressService(
            NewDefinition(101), NewDefinition(202), NewDefinition(303), NewDefinition(404));

        await service.SendListAsync(session, [303, 101]);

        var packet = sent.Should().ContainSingle().Subject;
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadUShort().Should().Be(2,
            "the retail window holds five rows and walks the array once per row the count promises, "
            + "so a count larger than the rows it asked for runs it off the end of that array");
        packet.ReadUShort().Should().Be(303, "the reply keeps the order the request asked in");
        packet.ReadByte();
        packet.ReadUShort();
        packet.ReadUShort();
        packet.ReadUShort().Should().Be(101);
        packet.RemainingBytes.Should().Be(5);
    }

    [Fact]
    public async Task ReachingATargetCompletesTheRowRatherThanLeavingItToBeClaimed()
    {
        var sent = new List<Packet>();
        var session = NewSessionCapturing(sent);
        session.Level = 30;
        var service = NewProgressService(new AchievementData
        {
            Id = 388,
            ConditionTable = AchievementConditionTable.Normal,
            Kind = (byte)NormalAchievementKind.Level,
            Target = 30,
        });

        await service.RefreshAsync(session);

        session.Achievements.Of(388).State.Should().Be(AchievementProgressState.Claimed,
            "the client has no send site for the claim, so the server grants on completion; leaving the "
            + "row below 4 makes retail draw it as an unfinished challenge");

        var listRow = sent.Should()
            .ContainSingle(p => p.GetData()[0] == (byte)AchievementSubOpcode.List).Subject;
        listRow.ResetOffset();
        listRow.ReadByte();
        listRow.ReadUShort().Should().Be(1);
        listRow.ReadUShort().Should().Be(388);
        listRow.ReadByte().Should().BeGreaterThanOrEqualTo(4,
            "anything below 4 leaves the retail row button live and the row drawn as incomplete");
    }

    [Fact]
    public async Task ARequestForZeroRowsIsAnsweredWithZeroRows()
    {
        var sent = new List<Packet>();
        var session = NewSessionCapturing(sent);
        var service = NewProgressService(NewDefinition(101), NewDefinition(202));

        await service.SendListAsync(session, []);

        var packet = sent.Should().ContainSingle().Subject;
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadUShort().Should().Be(0,
            "the window clamps its request to zero when no row is on screen and still latches "
            + "waiting for the answer; treating that as 'asked for everything' sends the whole "
            + "table and runs its five-entry array off the end");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task AListRequestWithNoBodyStillReturnsTheWholeTable()
    {
        var sent = new List<Packet>();
        var session = NewSessionCapturing(sent);
        var service = NewProgressService(NewDefinition(101), NewDefinition(202), NewDefinition(303));

        await service.SendListAsync(session);

        var packet = sent.Should().ContainSingle().Subject;
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadUShort().Should().Be(3);
    }

    [Fact]
    public async Task AnUnknownRequestedIdStillGetsARowSoTheCountStaysHonest()
    {
        var sent = new List<Packet>();
        var session = NewSessionCapturing(sent);
        var service = NewProgressService(NewDefinition(101));

        await service.SendListAsync(session, [101, 999]);

        var packet = sent.Should().ContainSingle().Subject;
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadUShort().Should().Be(2,
            "dropping a row while still counting it leaves the client reading past the packet");
        packet.RemainingBytes.Should().Be(14);
    }

    [Fact]
    public async Task AnUnsolicitedUpdateNeverExceedsTheClientsRowArray()
    {
        var sent = new List<Packet>();
        var session = NewSessionCapturing(sent);
        var definitions = Enumerable.Range(1, 12)
            .Select(id => new AchievementData
            {
                Id = id,
                ConditionTable = AchievementConditionTable.Normal,
                Kind = (byte)NormalAchievementKind.Level,
                Target = 1,
            })
            .ToArray();
        var service = NewProgressService(definitions);
        session.Level = 80;

        await service.RefreshAsync(session);

        var packet = sent.Should()
            .ContainSingle(p => p.GetData()[0] == (byte)AchievementSubOpcode.List).Subject;
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadUShort().Should().BeLessThanOrEqualTo(AchievementPacketWriter.PushedRowsMax,
            "the retail window walks a five-entry array once per counted row, so an unsolicited "
            + "update larger than that runs it off the end");
    }

    [Fact]
    public async Task TheProfileSummaryIsTheThirtySixByteBodyTheWindowOpensOn()
    {
        var sent = new List<Packet>();
        var session = NewSessionCapturing(sent);
        var service = NewProgressService(
            NewTabbed(1, AchievementTab.War, points: 20),
            NewTabbed(2, AchievementTab.War, points: 10),
            NewTabbed(3, AchievementTab.Adventure, points: 50),
            NewTabbed(4, AchievementTab.Challenge, points: 30));

        session.Achievements.Load(BuildState(
            (1, AchievementProgressState.Claimed),
            (3, AchievementProgressState.Achieved)));

        await service.SendSummaryAsync(session);

        var packet = sent.Should().ContainSingle().Subject;
        packet.ResetOffset();
        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ACHIEVEMENT);
        packet.ReadByte().Should().Be((byte)AchievementSubOpcode.Summary);

        packet.ReadInt().Should().Be(0, "accumulated play time is not tracked yet");
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(70, "only achievements that were reached score their points");

        packet.ReadUShort().Should().Be(3);
        packet.ReadUShort().Should().Be(1);
        packet.ReadUShort().Should().Be(0);

        packet.ReadUShort().Should().Be(0, "nothing normal was reached");
        packet.ReadUShort().Should().Be(0, "nothing on the quest tab was reached");
        packet.ReadUShort().Should().Be(1, "one of the two war rows was reached");
        packet.ReadUShort().Should().Be(1);
        packet.ReadUShort().Should().Be(0);

        packet.RemainingBytes.Should().Be(0,
            "the window reads five ints, three recent ids and five tab counts, then stops");
    }

    private static byte[] BuildState(params (int Id, AchievementProgressState State)[] entries)
    {
        var data = new byte[2 + entries.Length * 7];
        BitConverter.TryWriteBytes(data.AsSpan(0), (short)entries.Length);
        var offset = 2;
        foreach (var (id, state) in entries)
        {
            BitConverter.TryWriteBytes(data.AsSpan(offset), (short)id);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 2), 1);
            data[offset + 6] = (byte)state;
            offset += 7;
        }
        return data;
    }

    private static AchievementData NewTabbed(int id, AchievementTab tab, short points) => new()
    {
        Id = id, ConditionTable = AchievementConditionTable.Normal, Tab = tab, Points = points, Target = 1,
    };

    private static AchievementData NewDefinition(int id) => new()
    {
        Id = id, ConditionTable = AchievementConditionTable.Normal, Target = 10,
    };

    private static UserSession NewSessionCapturing(List<Packet> sent)
    {
        var client = Substitute.For<IClient>();
        client.SendPacket(Arg.Any<Packet>()).Returns(callInfo =>
        {
            sent.Add(callInfo.Arg<Packet>());
            return Task.CompletedTask;
        });
        return new UserSession(client, characterId: 1, accountId: 1);
    }

    private static UserSession NewSession() =>
        new(Substitute.For<IClient>(), characterId: 1, accountId: 1);

    private static AchievementProgressService NewProgressService(params AchievementData[] definitions)
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.AchievementTable.Returns(definitions.ToDictionary(d => d.Id));
        return new AchievementProgressService(
            gameData,
            Substitute.For<IKingSystemRuntimeService>(),
            Substitute.For<IUserNotificationService>(),
            Substitute.For<ILogger<AchievementProgressService>>());
    }
}
