using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestPacketWriterTests
{
    [Fact]
    public void ScriptObjectivesMatchTheGodotReaderFixture()
    {
        var objectives = new LibreKO.Quests.Runtime.QuestObjectives(777,
            [new(7, [100, 101]), new(3, [200])], LibreKO.Quests.Runtime.ObjectiveRule.Any);
        Convert.ToHexString(QuestPacketWriter.Objectives(objectives).GetData())
            .Should().Be("0E090301020700026400000065000000030001C8000000");
    }
    [Fact]
    public void ClockMatchesTheRetailTimeStruct()
    {
        var packet = QuestPacketWriter.Clock(new DateTime(2026, 8, 27, 13, 45, 9));
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_QUEST);
        packet.ReadByte().Should().Be((byte)QuestSubOpcode.Clock);
        packet.ReadUShort().Should().Be(2026);
        packet.ReadByte().Should().Be(8);
        packet.ReadByte().Should().Be(27);
        packet.ReadByte().Should().Be(13);
        packet.ReadByte().Should().Be(45);
        packet.ReadByte().Should().Be(9);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void QuestListIsACountedListOfIdAndState()
    {
        var packet = QuestPacketWriter.QuestList(new[]
        {
            new QuestPacketWriter.QuestEntry(500, QuestStatus.Active),
            new QuestPacketWriter.QuestEntry(501, QuestStatus.ReadyToTurnIn),
        });
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)QuestSubOpcode.QuestList);
        packet.ReadShort().Should().Be(2);
        packet.ReadShort().Should().Be(500);
        packet.ReadByte().Should().Be((byte)QuestStatus.Active);
        packet.ReadShort().Should().Be(501);
        packet.ReadByte().Should().Be((byte)QuestStatus.ReadyToTurnIn);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void NpcEventSendsTheNpcIdAsTheShortRetailReads()
    {
        var packet = QuestPacketWriter.NpcEvent(500, 720);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)QuestSubOpcode.NpcEvent);
        packet.ReadInt().Should().Be(500);
        packet.ReadShort().Should().Be(720);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void KillCountsAlwaysSendsFourSlots()
    {
        var packet = QuestPacketWriter.KillCounts(500, new ushort[] { 3, 1 });
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)QuestSubOpcode.KillCounts);
        packet.ReadByte().Should().Be((byte)QuestKillCountForm.All);
        packet.ReadShort().Should().Be(500);
        packet.ReadUShort().Should().Be(3);
        packet.ReadUShort().Should().Be(1);
        packet.ReadUShort().Should().Be(0);
        packet.ReadUShort().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void KillCountUpdateUsesTheSingleFormAndAOneBasedGroup()
    {
        var packet = QuestPacketWriter.KillCountUpdate(500, groupIndex: 0, count: 4);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)QuestSubOpcode.KillCounts);
        packet.ReadByte().Should().Be((byte)QuestKillCountForm.Single);
        packet.ReadShort().Should().Be(500);
        packet.ReadByte().Should().Be(1);
        packet.ReadUShort().Should().Be(4);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void StateChangeIsSubTwo()
    {
        var packet = QuestPacketWriter.StateChange(500, QuestStatus.Completed);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)QuestSubOpcode.StateChange);
        packet.ReadShort().Should().Be(500);
        packet.ReadByte().Should().Be((byte)QuestStatus.Completed);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void QuestTextMatchesTheGodotReaderFixture()
    {
        var packet = QuestPacketWriter.Texts(
            [new QuestPacketWriter.TextEntry(61, "Silk Spool", "Menissiah quiere 2 manzanas.")]);
        Convert.ToHexString(packet.GetData()).Should().Be(
            "0F01003D000A0053696C6B2053706F6F6C1C004D656E697373696168207175696572652032206D616E7A616E61732E");
    }

    [Fact]
    public void QuestTextCarriesTheTitleAndJournalAsUtf8()
    {
        var packet = QuestPacketWriter.Texts(new[]
        {
            new QuestPacketWriter.TextEntry(61, "Silk Spool", "Menissiah quiere 2 manzanas."),
        });
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)QuestSubOpcode.Text);
        packet.ReadShort().Should().Be(1);
        packet.ReadShort().Should().Be(61);
        packet.ReadUtf8String().Should().Be("Silk Spool");
        packet.ReadUtf8String().Should().Be("Menissiah quiere 2 manzanas.");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AQuestJournalLongerThanAByteStillFits()
    {
        var journal = new string('a', 300);
        var packet = QuestPacketWriter.Texts(
            [new QuestPacketWriter.TextEntry(61, "Silk Spool", journal)]);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadUtf8String();
        packet.ReadUtf8String().Should().Be(journal);
    }

    [Fact]
    public void RewardRefusalIsSubThirteenAndAReasonByte()
    {
        var packet = QuestPacketWriter.RewardRefused(QuestRewardRefusal.InventoryFull);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)QuestSubOpcode.RewardRefused);
        packet.ReadByte().Should().Be((byte)QuestRewardRefusal.InventoryFull);
        packet.RemainingBytes.Should().Be(0);
    }
}
