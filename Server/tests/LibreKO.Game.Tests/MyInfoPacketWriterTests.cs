using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class MyInfoPacketWriterTests
{
    private const int ItemRecordBytes = 19;
    private const int ReservedItemRecords = 4;

    private static Packet BuildMinimal()
    {
        var writer = new MyInfoPacketWriter
        {
            CharacterId = 7,
            Name = "Tester",
            Level = 40,
            KarusMilitary = 1,
            HumanMilitary = 2,
            KarusEslantMilitary = 3,
            HumanEslantMilitary = 4,
            MoradonMilitary = 5,
            GenieTime = 600,
            RebirthLevel = 2,
            CoverTitle = 11,
            SkillTitle = 12,
        };
        var packet = writer.Build();
        packet.ResetOffset();
        return packet;
    }

    private static void SkipHeader(Packet packet)
    {
        packet.ReadInt();
        packet.ReadSByteString();
        packet.ReadBytes(6);
        packet.ReadBytes(9);
        packet.ReadBytes(4);
        packet.ReadBytes(3);
        packet.ReadBytes(16);
        packet.ReadBytes(8);
        packet.ReadBytes(3);
        packet.ReadBytes(14);
        packet.ReadBytes(8);
        packet.ReadBytes(16);
        packet.ReadBytes(10);
        packet.ReadBytes(4);
        packet.ReadBytes(6);
        packet.ReadBytes(7);
        packet.ReadBytes(9);
    }

    [Fact]
    public void Build_ClosesTheItemArrayWithTheFourRecordsTheClientAlsoReads()
    {
        var packet = BuildMinimal();
        SkipHeader(packet);

        var records = InventoryConstants.MyInfoWireTotal + ReservedItemRecords;
        packet.ReadBytes(records * ItemRecordBytes);

        packet.ReadByte().Should().Be(0, "the account status byte follows the last item record");
        packet.ReadByte().Should().Be(0, "the premium list is empty, and this count drives how many "
            + "three-byte records the client reads next");
        packet.ReadByte();
        packet.ReadByte();
    }

    [Fact]
    public void Build_SendsTheMilitaryCampCountsAsSingleBytes()
    {
        var packet = BuildMinimal();
        SkipHeader(packet);
        packet.ReadBytes((InventoryConstants.MyInfoWireTotal + ReservedItemRecords) * ItemRecordBytes);
        packet.ReadBytes(4);

        packet.ReadInt().Should().Be(0);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be(3);
        packet.ReadByte().Should().Be(4);
        packet.ReadByte().Should().Be(5, "a short here would push every later field along by five bytes");
        packet.ReadByte().Should().Be(0);

        packet.ReadShort().Should().Be(600);
        packet.ReadByte().Should().Be(2);
        packet.ReadBytes(5);
        packet.ReadLong().Should().Be(0);
        packet.ReadShort().Should().Be(11);
        packet.ReadShort().Should().Be(12);
        packet.ReadInt().Should().Be(0);
        packet.ReadShort().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.ReadShort().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
