using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class ItemCountChangeWriterTests
{
    [Fact]
    public void Writer_EmitsRetailShape()
    {
        var packet = new ItemCountChangePacketWriter()
            .Add((byte)(InventoryConstants.InventoryStart + 3), 379_022_000, 12, 8_500, isNewItem: true)
            .Build();
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(ItemCountChangePacketWriter.KindUpdate);
        packet.ReadByte().Should().Be(3);
        packet.ReadInt().Should().Be(379_022_000);
        packet.ReadInt().Should().Be(12);
        packet.ReadByte().Should().Be(ItemCountChangePacketWriter.FlagNewItem);
        packet.ReadShort().Should().Be(8_500);
        packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Writer_CountFieldIsEntryCountNotSlotIndex()
    {
        var packet = new ItemCountChangePacketWriter()
            .Add((byte)(InventoryConstants.InventoryStart + 27), 1, 1, 0)
            .Add((byte)(InventoryConstants.InventoryStart + 5), 2, 2, 0)
            .Build();
        packet.ResetOffset();

        packet.ReadShort().Should().Be(2);
        packet.RemainingBytes.Should().Be(2 * ItemCountChangePacketWriter.EntryBytes);
    }

    [Fact]
    public void Writer_NormalisesAbsoluteSlotToBackpackPosition()
    {
        var packet = new ItemCountChangePacketWriter()
            .Add((byte)(InventoryConstants.InventoryStart), 5, 1, 0)
            .Build();
        packet.ResetOffset();
        packet.ReadShort();
        packet.ReadByte();

        packet.ReadByte().Should().Be(0);
    }
}

public class ItemRemoveWriterTests
{
    [Fact]
    public void Removed_IsSingleByte()
    {
        var packet = ItemRemovePacketWriter.Removed();
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ITEM_REMOVE);
        packet.ReadByte().Should().Be((byte)ItemRemoveResult.Removed);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Failed_CarriesTheReasonByteRetailReads()
    {
        var packet = ItemRemovePacketWriter.Failed();
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)ItemRemoveResult.Failed);
        packet.ReadByte().Should().Be((byte)ItemRemoveReason.SystemError);
        packet.RemainingBytes.Should().Be(0);
    }
}

public class BundleOpenWriterTests
{
    [Fact]
    public void Bundle_AlwaysEmitsRetailsTwelveSlots()
    {
        var writer = new BundleOpenPacketWriter { BundleId = 42 };
        writer.Add(379_022_000, 3).Add(900_000_000, 1);

        var packet = writer.Build();
        packet.ResetOffset();

        packet.ReadInt().Should().Be(42);
        packet.ReadByte().Should().Be(1);
        packet.RemainingBytes.Should().Be(BundleOpenPacketWriter.WireSlots * BundleOpenPacketWriter.EntryBytes);
    }

    [Fact]
    public void EmptyBundle_StopsAfterTheFlag()
    {
        var packet = new BundleOpenPacketWriter { BundleId = 7 }.Build();
        packet.ResetOffset();

        packet.ReadInt().Should().Be(7);
        packet.ReadByte().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
