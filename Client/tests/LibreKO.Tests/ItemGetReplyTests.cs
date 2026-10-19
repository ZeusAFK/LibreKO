using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ItemGetReplyTests
{
    private const int BundleId = 77;
    private const int ItemId = 379_022_000;
    private const int GoldItemId = 900_000_000;

    private static Packet Taken(byte position, int itemId, ushort count, int money, ushort bundleSlot)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteByte((byte)ItemGetResult.Taken);
        p.WriteInt(BundleId);
        p.WriteByte(position);
        p.WriteInt(itemId);
        p.WriteUShort(count);
        p.WriteInt(money);
        p.WriteUShort(bundleSlot);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void ATakenItemReadsItsSlotCountAndBundleCell()
    {
        var p = Taken(3, ItemId, 5, 12_345, 2);

        var reply = ItemGetReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal((byte)ItemGetResult.Taken, (byte)reply.Value.Result);
        Assert.Equal(BundleId, reply.Value.BundleId);
        Assert.Equal(3, reply.Value.Position);
        Assert.Equal(ItemId, reply.Value.ItemId);
        Assert.Equal(5, reply.Value.Count);
        Assert.Equal(12_345, reply.Value.Money);
        Assert.Equal(2, reply.Value.BundleSlot);
        Assert.False(reply.Value.IsGold);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void TakenGoldCarriesTheNewPurse()
    {
        var reply = ItemGetReply.Read(Taken(ItemGetReply.GoldPosition, GoldItemId, 1, 4_500, 0));

        Assert.NotNull(reply);
        Assert.True(reply.Value.IsGold);
        Assert.Equal(4_500, reply.Value.Money);
        Assert.Equal(0, reply.Value.BundleSlot);
    }

    [Fact]
    public void AReplyWithoutTheBundleCellStillCounts()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteByte((byte)ItemGetResult.TakenToSlot);
        p.WriteInt(BundleId);
        p.WriteByte(0);
        p.WriteInt(ItemId);
        p.WriteUShort(1);
        p.WriteInt(0);
        p.ResetOffset();

        var reply = ItemGetReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(ItemId, reply.Value.ItemId);
        Assert.Equal(ItemGetReply.NoBundleSlot, reply.Value.BundleSlot);
    }

    [Fact]
    public void PartyCoinsAreGoldWithoutABundleCell()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteByte((byte)ItemGetResult.PartyCoins);
        p.WriteInt(BundleId);
        p.WriteByte(ItemGetReply.GoldPosition);
        p.WriteInt(GoldItemId);
        p.WriteInt(9_000);
        p.ResetOffset();

        var reply = ItemGetReply.Read(p);

        Assert.NotNull(reply);
        Assert.True(reply.Value.IsGold);
        Assert.Equal(9_000, reply.Value.Money);
        Assert.Equal(ItemGetReply.NoBundleSlot, reply.Value.BundleSlot);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(-11)]
    public void AFailureCarriesTheBundleThenTheReason(int reason)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteByte((byte)ItemGetResult.Failed);
        p.WriteInt(BundleId);
        p.WriteInt(reason);
        p.ResetOffset();

        var reply = ItemGetReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(BundleId, reply.Value.BundleId);
        Assert.Equal(reason, reply.Value.Failure);
    }

    [Fact]
    public void APartyMembersItemReadsTheLooterAndTheCell()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteByte((byte)ItemGetResult.PartyMember);
        p.WriteInt(BundleId);
        p.WriteInt(ItemId);
        p.WriteString("Rikka");
        p.WriteUShort(4);
        p.ResetOffset();

        var reply = ItemGetReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(ItemId, reply.Value.ItemId);
        Assert.Equal("Rikka", reply.Value.Looter);
        Assert.Equal(4, reply.Value.BundleSlot);
    }

    [Fact]
    public void ATemporaryInventoryNoticeReadsTheItem()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteByte((byte)ItemGetResult.TemporaryLost);
        p.WriteInt(BundleId);
        p.WriteByte(0);
        p.WriteInt(ItemId);
        p.ResetOffset();

        var reply = ItemGetReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal((byte)ItemGetResult.TemporaryLost, (byte)reply.Value.Result);
        Assert.Equal(ItemId, reply.Value.ItemId);
    }
}
