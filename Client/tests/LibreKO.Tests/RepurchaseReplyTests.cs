using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class RepurchaseReplyTests
{
    private const int Sword = 110110001;

    private static Packet Reply(params object[] parts)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte b: p.WriteByte(b); break;
                case short s: p.WriteShort(s); break;
                case int i: p.WriteInt(i); break;
                case uint u: p.WriteUInt(u); break;
            }
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void AListCarriesRowItemPriceAndDeletionTime()
    {
        var reply = RepurchaseReply.Read(Reply((byte)RepurchaseSubOpcode.Open, (byte)1, (byte)2,
            (byte)0, Sword, 6_000, 1_800_259_200u,
            (byte)1, Sword + 1, 9_000, 1_800_300_000u));

        Assert.True(reply.Ok);
        Assert.Equal((byte)RepurchaseSubOpcode.Open, (byte)reply.Op);
        Assert.Equal(2, reply.Rows.Count);
        Assert.Equal(new RepurchaseRow(1, Sword + 1, 9_000, 1_800_300_000u), reply.Rows[1]);
    }

    [Fact]
    public void ABoughtReplySkipsOneByteBeforeTheBagSlot()
    {
        var reply = RepurchaseReply.Read(Reply((byte)RepurchaseSubOpcode.Buy, (byte)RepurchaseResult.Succeeded, (byte)0,
            (byte)4, Sword, (byte)0));

        Assert.True(reply.Ok);
        Assert.Equal(4, reply.BagPosition);
        Assert.Equal(Sword, reply.ItemId);
    }

    [Fact]
    public void ARefusedReplyCarriesTheErrorCode()
    {
        var reply = RepurchaseReply.Read(Reply((byte)RepurchaseSubOpcode.Buy, (byte)2, (byte)0, Sword, (short)RepurchaseError.Expired));

        Assert.False(reply.Ok);
        Assert.Equal((short)RepurchaseError.Expired, (short)reply.Error);
    }
}
