using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class ItemPacketWriterTests
{
    [Fact]
    public void ItemDrop_IsNineBytes()
    {
        var packet = ItemDropPacketWriter.Dropped(1234, 99, hasItems: true);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ITEM_DROP);
        packet.ReadInt().Should().Be(1234);
        packet.ReadInt().Should().Be(99);
        packet.ReadByte().Should().Be(1);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemGet_FailureIsResultOnly()
    {
        var packet = ItemGetPacketWriter.Failed(ItemGetPacketWriter.ResultNoSlot);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ItemGetPacketWriter.ResultNoSlot);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemGet_SuccessMatchesRetailCaseOneOrder()
    {
        var packet = ItemGetPacketWriter.Looted(77, 3, 379_022_000, 5, 12_345, 2);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ItemGetPacketWriter.ResultSuccess);
        packet.ReadInt().Should().Be(77);
        packet.ReadByte().Should().Be(3);
        packet.ReadInt().Should().Be(379_022_000);
        packet.ReadUShort().Should().Be(5);
        packet.ReadInt().Should().Be(12_345);
        packet.ReadUShort().Should().Be(2);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemGet_GoldUsesThePositionSentinel()
    {
        var packet = ItemGetPacketWriter.LootedGold(1, 900_000_000, 1, 500, 0);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadInt();

        packet.ReadByte().Should().Be(ItemGetPacketWriter.PositionGold);
    }

    [Fact]
    public void ItemMove_FailedResponseCarriesNoStatBlock()
    {
        var packet = ItemMovePacketWriter.Move((byte)ItemMoveSubOpcode.Failed).Build();
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)ItemMoveSubOpcode.Move);
        packet.ReadByte().Should().Be((byte)ItemMoveSubOpcode.Failed);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemMove_ArrangeUsesNineteenByteEntriesForEveryBagSlot()
    {
        var writer = ItemMovePacketWriter.Arranged();
        for (var i = 0; i < 28; i++)
            writer.AddInventorySlot(i, 100, 1, 0);

        var packet = writer.Build();
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)ItemMoveSubOpcode.ArrangeInventory);
        packet.ReadByte().Should().Be((byte)ArrangeResult.Succeeded);

        packet.RemainingBytes.Should().Be(28 * ItemMovePacketWriter.InventoryEntryBytes);
    }

    [Fact]
    public void ItemMove_NegativeDurabilityClampsToZero()
    {
        var packet = ItemMovePacketWriter.Arranged()
            .AddInventorySlot(5, -12, 1, 0)
            .Build();
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt();

        packet.ReadUShort().Should().Be(0);
    }

    [Fact]
    public void ItemTrade_ErrorCarriesTheCode()
    {
        var packet = ItemTradePacketWriter.Failed(ItemTradeRefusal.InventoryFull);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)ItemTradeResult.Refused);
        packet.ReadByte().Should().Be((byte)ItemTradeRefusal.InventoryFull);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemTrade_MovedIsSubOpcodeOnly()
    {
        var packet = ItemTradePacketWriter.Moved();
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)ItemTradeResult.Moved);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemTrade_TheLoyaltyGroupByteIsWhatMarksALoyaltyTrade()
    {
        var without = ItemTradePacketWriter.Traded(100, 20);
        without.ResetOffset();
        without.ReadByte();
        without.ReadInt();
        without.ReadInt();
        without.RemainingBytes.Should().Be(0);

        var with = ItemTradePacketWriter.Traded(100, 20, loyaltySellingGroup: 9);
        with.ResetOffset();
        with.ReadByte();
        with.ReadInt();
        with.ReadInt();
        with.ReadByte().Should().Be(9);
    }

    [Fact]
    public void ItemRepair_AlwaysCarriesMoney()
    {
        var packet = ItemRepairPacketWriter.Repaired(555);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ITEM_REPAIR);
        packet.ReadByte().Should().Be((byte)ItemRepairResult.Repaired);
        packet.ReadInt().Should().Be(555);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemUpgrade_AnvilOpenIsSubPlusId()
    {
        var packet = ItemUpgradePacketWriter.AnvilOpen(4242);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ITEM_UPGRADE);
        packet.ReadByte().Should().Be((byte)ItemUpgradeSubOpcode.AnvilOpen);
        packet.ReadInt().Should().Be(4242);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemUpgrade_ResultPadsToTenSlots()
    {
        var packet = ItemUpgradePacketWriter.UpgradeResult(
            ItemUpgradeSubOpcode.Upgrade, 1, 1, [10, 20], [3, 4]);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();

        packet.RemainingBytes.Should().Be(ItemUpgradePacketWriter.UpgradeSlotCount * 5);
    }

    [Fact]
    public void ItemUpgrade_ResultUsesEmptyPositionSentinelForUnusedSlots()
    {
        var packet = ItemUpgradePacketWriter.UpgradeResult(
            ItemUpgradeSubOpcode.Upgrade, 1, 1, [10], [3]);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt().Should().Be(10);
        packet.ReadByte().Should().Be(3);

        packet.ReadInt().Should().Be(0);
        packet.ReadByte().Should().Be(ItemUpgradePacketWriter.EmptyPosition);
    }
}

public class CharacterStateWriterTests
{
    [Fact]
    public void Helmet_MatchesRetailTwoFlagShape()
    {
        var packet = HelmetPacketWriter.Visibility(4242, hideHelmet: true, hideCospreHelmet: false);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_HELMET);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(4242);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Helmet_CharacterIdSitsAtOffsetTwo()
    {
        var packet = HelmetPacketWriter.Visibility(0x11223344, false, false);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();

        packet.ReadInt().Should().Be(0x11223344);
    }
}
