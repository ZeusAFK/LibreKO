using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class MerchantPacketWriterTests
{
    private const int StallSlots = 12;

    [Fact]
    public void StallInsertedIsTheReplyTheClientWaitsFor()
    {
        var packet = MerchantPacketWriter.StallInserted(
            MerchantPacketWriter.Succeeded, "cheap gear", characterId: 70123, flags: 0,
            itemIds: [900, 901]);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_MERCHANT);
        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.Insert);
        packet.ReadUShort().Should().Be(MerchantPacketWriter.Succeeded);
        packet.ReadString().Should().Be("cheap gear");
        packet.ReadInt().Should().Be(70123);
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(900);
        packet.ReadInt().Should().Be(901);
        for (var slot = 2; slot < StallSlots; slot++)
            packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void StallInsertedMatchesTheRetailShapeByteForByte()
    {
        var packet = MerchantPacketWriter.StallInserted(
            MerchantPacketWriter.Succeeded, "ab", characterId: 70179, flags: 0, itemIds: [900, 901]);

        Convert.ToHexString(packet.GetData()).Should().Be(
            "07" +
            "0100" +
            "0200" + "6162" +
            "23120100" +
            "00" +
            "84030000" + "85030000" + new string('0', 8 * 10));
    }

    [Fact]
    public void InsertRefusedStillCarriesAResultTheClientCanRead()
    {
        var packet = MerchantPacketWriter.InsertRefused();
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.Insert);
        packet.ReadUShort().Should().Be(MerchantPacketWriter.Failed);
    }

    [Fact]
    public void StallsInViewIsACountedListOfFullWidthIds()
    {
        var packet = MerchantPacketWriter.StallsInView(
            MerchantInOut.StallsInView,
            [new MerchantPacketWriter.StallOwner(70123, false, 0),
             new MerchantPacketWriter.StallOwner(70124, true, 0x10)]);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_MERCHANT_INOUT);
        packet.ReadByte().Should().Be((byte)MerchantInOut.StallsInView);
        packet.ReadShort().Should().Be(2);
        packet.ReadInt().Should().Be(70123);
        packet.ReadByte().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(70124);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(0x10);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void StallClosedIsAnnouncedOnTheMerchantOpcodeWithAFullWidthId()
    {
        var packet = MerchantPacketWriter.StallClosed(70123);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_MERCHANT);
        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.Close);
        packet.ReadInt().Should().Be(70123);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void StallContentsCarriesAFullWidthMerchantId()
    {
        var packet = MerchantPacketWriter.StallContents(
            MerchantSubOpcode.ItemList, merchantId: 70179,
            [new MerchantPacketWriter.StallItem(156211000, 1, 30, 350_000_000), null]);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.ItemList);
        packet.ReadUShort().Should().Be(MerchantPacketWriter.Succeeded);
        packet.ReadInt().Should().Be(70179);
        packet.ReadInt().Should().Be(156211000);
        packet.ReadUShort().Should().Be(1);
        packet.ReadShort().Should().Be(30);
        packet.ReadInt().Should().Be(350_000_000);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.ReadUShort().Should().Be(0);
        packet.ReadShort().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ItemSoldNamesTheBuyerRatherThanTheCount()
    {
        var packet = MerchantPacketWriter.ItemSold(MerchantSubOpcode.ItemPurchased, 156211000, "Zeus");
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.ItemPurchased);
        packet.ReadInt().Should().Be(156211000);
        packet.ReadString().Should().Be("Zeus");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void BuyingStallInsertedIsTheRegionBroadcastThatPlacesTheStall()
    {
        var packet = MerchantPacketWriter.BuyingStallInserted(70123, [900, 901]);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_MERCHANT);
        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.BuyRegionInsert);
        packet.ReadInt().Should().Be(70123);
        packet.ReadInt().Should().Be(900);
        packet.ReadInt().Should().Be(901);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void WantedListIsTwelveFixedWidthRows()
    {
        var wanted = new List<MerchantPacketWriter.StallItem?> { new(379080000, 20, 0, 4_800_000) };
        for (var slot = 1; slot < 12; slot++) wanted.Add(null);

        var packet = MerchantPacketWriter.WantedList(70123, wanted);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.BuyList);
        packet.ReadByte().Should().Be((byte)BuyingMerchantResult.Accepted);
        packet.ReadInt().Should().Be(70123);
        packet.ReadInt().Should().Be(379080000);
        packet.ReadUShort().Should().Be(20);
        packet.ReadShort().Should().Be(0);
        packet.ReadInt().Should().Be(4_800_000);
        packet.RemainingBytes.Should().Be(11 * 12);
    }

    [Fact]
    public void StallListShowsFourIdsAndEightForAPremiumStall()
    {
        var plain = MerchantPacketWriter.StallList(
            new MerchantPacketWriter.StallOwner(70123, false, 0), [900, 901, 902, 903, 904]);
        plain.ResetOffset();
        plain.ReadByte().Should().Be((byte)MerchantSubOpcode.StallList);
        plain.ReadByte().Should().Be((byte)BuyingMerchantResult.Accepted);
        plain.ReadInt().Should().Be(70123);
        plain.ReadByte().Should().Be(0);
        plain.ReadByte().Should().Be(0);
        plain.RemainingBytes.Should().Be(4 * 4);

        var premium = MerchantPacketWriter.StallList(
            new MerchantPacketWriter.StallOwner(70123, false, 1), [900, 901, 902, 903, 904]);
        premium.ResetOffset();
        premium.ReadByte();
        premium.ReadByte();
        premium.ReadInt();
        premium.ReadByte();
        premium.ReadByte().Should().Be(1);
        premium.RemainingBytes.Should().Be(8 * 4);
    }

    [Fact]
    public void WantedItemSoldTellsTheSellerBothRemainingCounts()
    {
        var packet = MerchantPacketWriter.WantedItemSold(
            wantedSlot: 3, wantedRemaining: 12, sellerSlot: 5, sellerRemaining: 108);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)MerchantSubOpcode.BuySold);
        packet.ReadByte().Should().Be((byte)BuyingMerchantResult.Accepted);
        packet.ReadByte().Should().Be(3);
        packet.ReadUShort().Should().Be(12);
        packet.ReadByte().Should().Be(5);
        packet.ReadUShort().Should().Be(108);
        packet.RemainingBytes.Should().Be(0);
    }
}
